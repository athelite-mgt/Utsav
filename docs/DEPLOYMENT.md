# Deploying Utsav

Utsav is one deployable: `dotnet publish src/Utsav.Api` builds the API and
copies the React app into `wwwroot`. It runs on Azure App Service against Azure
SQL, with Cosmos DB (audit trail) and Blob Storage (documents, committee logos).

> **This product is separate from the original single-committee app.** It must
> get its own App Service, database, Cosmos account, storage account, OAuth
> clients and deployment identity. Do not point any setting below at the original
> app's resources.

## Environment

| Item | Value |
|---|---|
| Repository | https://github.com/athelite-mgt/Utsav |
| App Service | `Utsav` (Central India; default host `utsav-dyeedqdbhpazg8es.centralindia-01.azurewebsites.net`) |
| Public host | `https://utsav.athelitesm.com` |
| Key Vault | `Utsav` |

## 1. Azure resources

| Resource | Notes |
|---|---|
| App Service (Linux, .NET 10) | HTTPS only. `ASPNETCORE_FORWARDEDHEADERS_ENABLED` is set by the platform; the app also honours `X-Forwarded-Proto`, which the session cookie's `Secure` flag depends on. |
| Azure SQL database | New, empty. Two logins — see section 2. |
| Cosmos DB (NoSQL) | Database `utsav`, container `audit-events`, partition key `/orgId`. The app creates the container if its identity may. |
| Storage account | One private blob container. Documents and committee logos are stored under per-committee prefixes. |

## 2. Database principals (security-critical)

Tenant isolation is enforced by row-level security, which is only a real
backstop if the application cannot switch it off.

| Login | Used by | Membership |
|---|---|---|
| deploy login (any high-privilege login) | `dotnet ef database update` only | owner of the database |
| `utsav_app` | the running App Service | `utsav_app_role` **only** (datareader + datawriter) |

**Never** make the app login `db_owner`, `db_ddladmin` or `sysadmin`: it could
then drop the security policy it runs under. `scripts/sql/create-app-principal.sql`
creates the app login and grants it the role (the `utsav_app_role` role itself is
created by the migrations). Run it once, after the first migration, as the deploy
login, in two steps:

```bash
sqlcmd -S <server> -U <deploy login> -d master -v APP_PASSWORD="<new password>" STEP=login -i scripts/sql/create-app-principal.sql
sqlcmd -S <server> -U <deploy login> -d <db>     -v APP_PASSWORD="<new password>" STEP=user  -i scripts/sql/create-app-principal.sql
```

## 3. App Service application settings

| Setting | Purpose |
|---|---|
| `ConnectionStrings__AzureSql` | Connection string for **`utsav_app`**. The running app never receives the deploy login. |
| `Database__MigrateOnStartup` | Set to `true`. The app applies pending EF Core migrations itself on startup (safe to repeat, safe when several instances start together) and refuses to start if one fails. |
| `ConnectionStrings__AzureSqlMigrations` | Login allowed to change the schema (database owner), used **only** for that startup step. Optional: when absent, `ConnectionStrings__AzureSql` is used, which is only acceptable for the very first start (see section 6). |
| `Platform__BootstrapAdminEmail` | Email of an *existing* account to promote to platform operator. Inert once any operator exists. |
| `AzureAd__TenantId`, `AzureAd__ClientId` | New Entra app registration for this hostname. |
| `Google__ClientId` | New Google OAuth client for this hostname. |
| `Cosmos__Endpoint`, `Cosmos__Key` (`Cosmos__Database`, `Cosmos__Container` default to `utsav` / `audit-events`) | Audit trail. **Required**: with no Cosmos configuration the app refuses to start rather than run without an audit trail. |
| `Storage__ConnectionString`, `Storage__ContainerName` | Blob storage for documents and committee logos. |
| `Push__VapidPublicKey`, `Push__VapidPrivateKey`, `Push__Subject` | **New** VAPID key pair and a `mailto:`/`https:` contact. Optional: without them push is silently off. |
| `Asura__Enabled` | Leave unset. The AI assistant stays off unless deliberately enabled; if enabled it also needs `Asura__GroqApiKey` / `Asura__NvidiaApiKey`. |
| `Auth__RateLimit__PermitsPerMinute` | Optional. Per-client-address budget shared by sign-in, sign-up, Google/Entra sign-in and invite redemption (default 20/minute; further requests get 429). Keyed on the address after forwarded headers, so it needs the platform proxy's `X-Forwarded-For`; add Front Door/WAF rules as defence in depth. |
| `Audit__Mode` | **Do not set in production.** `Log` writes the audit trail to the log instead of Cosmos and exists for local development only. |

No secret belongs in the repository. Keep them in the `Utsav` Key Vault and
reference them from App Service so the values never appear in the portal or the
workflow:

```
ConnectionStrings__AzureSql = @Microsoft.KeyVault(VaultName=Utsav;SecretName=<secret name>)
```

Current vault contents and where each belongs:

| Key Vault secret | App Service setting | Notes |
|---|---|---|
| `ConnectionStringSQL` | `ConnectionStrings__AzureSql` | Must connect as **`utsav_app`**, not the deploy/admin login. |
| `GoogleClientId` | `Google__ClientId` | Server-side ID-token audience check. The browser gets it from the app itself (`GET /api/auth/config`), so nothing is compiled into the build. |
| `GoogleClientSecret` | *(none)* | Not used by this app (ID-token flow). Do not map it; delete it from the vault if you prefer. |
| `ConnectionStringAD` | *(to be confirmed)* | The app has no setting that takes a connection string for Entra. Microsoft sign-in uses only a client ID and tenant ID, and is switched off for now. |
| `CosmosURI` | `Cosmos__Endpoint` | Required; the app will not start without it. |
| `CosmosKey` | `Cosmos__Key` | Required. |
| `StorageConnectionString` | `Storage__ConnectionString` | Documents and committee logos. |
| `StorageContainerNameProd` | `Storage__ContainerName` | Private container. |

```bash
az webapp config appsettings set -g <resource-group> -n utsav-dyeedqdbhpazg8es --settings \
  "ConnectionStrings__AzureSql=@Microsoft.KeyVault(VaultName=Utsav;SecretName=ConnectionStringSQL)" \
  "Google__ClientId=@Microsoft.KeyVault(VaultName=Utsav;SecretName=GoogleClientId)" \
  "Cosmos__Endpoint=@Microsoft.KeyVault(VaultName=Utsav;SecretName=CosmosURI)" \
  "Cosmos__Key=@Microsoft.KeyVault(VaultName=Utsav;SecretName=CosmosKey)" \
  "Storage__ConnectionString=@Microsoft.KeyVault(VaultName=Utsav;SecretName=StorageConnectionString)" \
  "Storage__ContainerName=@Microsoft.KeyVault(VaultName=Utsav;SecretName=StorageContainerNameProd)" \
  "Database__MigrateOnStartup=true" \
  "Platform__BootstrapAdminEmail=<your email>"
```

This needs the App Service's managed identity (Identity → System assigned: On)
to hold **Key Vault Secrets User** on the vault. A setting whose reference does
not resolve shows a red status in the portal's configuration blade.

## 4. Identity provider redirect URIs

Both providers must list the new origin or social sign-in fails, with no
code-level workaround:

- Google OAuth client → **Authorised JavaScript origins**: `https://utsav.athelitesm.com` (the app uses Google Identity Services, which checks the *origin*; the `redirect_uris` entry alone is not enough). Add `http://localhost:5173` too if sign-in should work in local development.
- Entra app registration → Redirect URI (SPA): `https://utsav.athelitesm.com`

**Microsoft sign-in is not set up yet.** Until `AzureAd__ClientId` and `AzureAd__TenantId` are set, the login page shows the Microsoft button greyed out with "Not available right now". Setting `AzureAd__ClientId` and `AzureAd__TenantId` on the App Service enables it with no code change or rebuild.

## 5. GitHub Actions

`.github/workflows/deploy.yml` is the single pipeline. On every push to `main` it
builds with warnings as errors, runs the unit tests, starts SQL Server in a
container and runs the cross-tenant isolation suite (a failure there is a data
leak and blocks the deploy), type-checks the frontend, and then deploys the
published API + frontend to the `Utsav` App Service.

Authentication is OIDC (`azure/login`): the three repository secrets
`AZUREAPPSERVICE_CLIENTID_…`, `…TENANTID_…` and `…SUBSCRIPTIONID_…` were created
by the App Service Deployment Center. There is no publish profile and no other
repository variable to set. (The Deployment Center's own generated workflow was
removed so two pipelines do not race; if it is ever regenerated, delete it again.)

Migrations are applied by the app on startup, not by the pipeline, so the pipeline
never holds database credentials.

## 6. First run

The schema is applied automatically, but the database needs its two logins set up
once, in this order:

1. **First deploy.** Set `Database__MigrateOnStartup=true` and point
   `ConnectionStrings__AzureSql` at a login that owns the database (temporarily).
   Push to `main`. The app starts, applies every migration (creating the tables, the
   row-level-security policy and the `utsav_app_role` role) and logs
   "Database migrations applied". It will also log an **error** that the runtime
   login is privileged; that is expected for this one start.
2. **Create the least-privilege login** with `scripts/sql/create-app-principal.sql`
   (section 2).
3. **Switch the logins.** Point `ConnectionStrings__AzureSql` at `utsav_app`, and add
   `ConnectionStrings__AzureSqlMigrations` with the owner login. Restart the app.
   The privilege error no longer appears; if it ever does, the app is serving
   requests with a login that could switch tenant isolation off.
4. **No committee exists yet** — deliberate; a pooled platform must not conjure a
   tenant. Sign up with the address in `Platform__BootstrapAdminEmail`, then restart
   so the promotion applies. A **Platform** section appears in the sidebar.
5. In the Platform console, create the first committee: name, festival
   (`durga` or `kali`), city, first year and the first admin's email. The console
   shows a **one-time invite code** (also returned by
   `POST /api/platform/committees` as `invite_code`).
6. Give the code to the first admin through a channel you trust. They sign up
   (or sign in) and enter it under **Have an invite code?**. The code is bound to
   their email, expires after 14 days and works once; the database holds only its hash.
7. The admin then invites the rest of the committee and sets branding
   (Settings → Committee: name, logo, accent colour), which flows into the
   committee's PDF and Excel letterhead.

Later releases need nothing beyond pushing to `main`: new migrations run on the
next start. Lost invite code: re-issue it from the Platform console. This works
**only while the committee has no active members**, so an operator can never mint
access to a committee that already has people in it.

## 7. Sessions and API clients

- Browser sessions are an `HttpOnly`, `Secure`, `SameSite=Lax` cookie
  (`utsav_session`); the server stores only a hash. They expire after 14 idle
  days or 30 absolute days. Every non-GET request must carry the
  `X-Utsav-Csrf` header (the token comes back in the sign-in / `/api/auth/me`
  response); a missing or wrong header is a 403.
- Scripts and integrations use **API keys** (`X-Api-Key`), not sessions.

## 8. Browser security headers

Every response carries a Content-Security-Policy (scripts only from the app
itself and Google's sign-in library; no framing, plugins or base-URL changes),
`X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` and
`Permissions-Policy`; production also sends HSTS (one year). The policy lives in
`src/Utsav.Api/Security/SecurityHeaders.cs`. **If a new third-party script,
frame, font or API origin is ever added, it must be added there too**, or the
browser will refuse it (check the console for "Refused to ...").

Client addresses come from the platform's `X-Forwarded-For` (trusted only when
`WEBSITE_SITE_NAME` is present, i.e. on App Service), which the sign-in throttle
relies on.

## 9. Known gaps

- Custom domain per committee, per-tenant PWA manifest/icons and a self-serve
  signup flow are intentionally not built.
- Historical data import for a committee (e.g. Kalipuja) is a separate piece of
  work once its format is known.
