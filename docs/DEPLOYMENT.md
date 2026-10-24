# Deploying Utsav

Utsav is one deployable: `dotnet publish src/Utsav.Api` builds the API and
copies the React app into `wwwroot`. It runs on Azure App Service against Azure
SQL, with Cosmos DB (audit trail) and Blob Storage (documents, committee logos).

> **This product is separate from the original single-committee app.** It must
> get its own App Service, database, Cosmos account, storage account, OAuth
> clients and publish profile. Do not point any setting below at the original
> app's resources.

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
creates the app login and role; run it once, after the first migration, as the
deploy login.

## 3. App Service application settings

| Setting | Purpose |
|---|---|
| `ConnectionStrings__AzureSql` | Connection string for **`utsav_app`**. The running app never receives the deploy login. |
| `Platform__BootstrapAdminEmail` | Email of an *existing* account to promote to platform operator. Inert once any operator exists. |
| `AzureAd__TenantId`, `AzureAd__ClientId` | New Entra app registration for this hostname. |
| `Google__ClientId` | New Google OAuth client for this hostname. |
| `Cosmos__Endpoint`, `Cosmos__Key` (`Cosmos__Database`, `Cosmos__Container` default to `utsav` / `audit-events`) | Audit trail. **Required**: with no Cosmos configuration the app refuses to start rather than run without an audit trail. |
| `Storage__ConnectionString`, `Storage__ContainerName` | Blob storage for documents and committee logos. |
| `Push__VapidPublicKey`, `Push__VapidPrivateKey`, `Push__Subject` | **New** VAPID key pair and a `mailto:`/`https:` contact. Optional: without them push is silently off. |
| `Asura__Enabled` | Leave unset. The AI assistant stays off unless deliberately enabled; if enabled it also needs `Asura__GroqApiKey` / `Asura__NvidiaApiKey`. |
| `Audit__Mode` | **Do not set in production.** `Log` writes the audit trail to the log instead of Cosmos and exists for local development only. |

No secret belongs in the repository. Store them as App Service settings (or
Key Vault references).

## 4. Identity provider redirect URIs

Both providers must list the new origin or social sign-in fails, with no
code-level workaround:

- Google OAuth client → Authorised JavaScript origins: `https://<host>`
- Entra app registration → Redirect URI (SPA): `https://<host>`

## 5. GitHub Actions

`.github/workflows/deploy.yml` builds with warnings as errors, runs the unit
tests, starts SQL Server in a container and runs the cross-tenant isolation
suite (a failure there is a data leak and blocks the deploy), then type-checks
the frontend. The deploy job is gated so nothing deploys by accident:

1. Create the new App Service and download its publish profile.
2. Store it as the repository secret `UTSAV_AZURE_WEBAPP_PUBLISH_PROFILE`.
3. Set `AZURE_WEBAPP_NAME` in the workflow to the new App Service name.
4. Set the repository variable `UTSAV_DEPLOY_ENABLED` to `true`.

Migrations are deliberately **not** part of the deploy: schema changes are
reviewed and applied by hand (section 6).

## 6. First run

1. Apply migrations as the deploy login:

   ```bash
   export ConnectionStrings__AzureSql="Server=<server>.database.windows.net;Database=<db>;User Id=<deploy login>;Password=<...>"
   dotnet ef database update -p src/Utsav.Infrastructure -s src/Utsav.Api
   ```

2. Run `scripts/sql/create-app-principal.sql` to create `utsav_app`.
3. Deploy / start the app. **No committee exists** — deliberate; a pooled
   platform must not conjure a tenant.
4. Sign up with the address in `Platform__BootstrapAdminEmail`, then restart the
   app so the promotion applies. A **Platform** entry appears in the account menu.
5. In the Platform console, create the first committee: name, festival
   (`durga` or `kali`), city, first year and the first admin's email. The
   console shows a **one-time invite code** (also returned by
   `POST /api/platform/committees` as `invite_code`).
6. Give the code to the first admin through a channel you trust. They sign up
   (or sign in), choose **I have an invite code**, and enter it. The code is
   bound to their email, expires after 14 days and works once; the database holds
   only its hash.
7. The admin then invites the rest of the committee and sets branding
   (Settings → Committee: name, logo, accent colour), which flows into the
   committee's PDF and Excel letterhead.

Lost code: re-issue it from the Platform console. This works **only while the
committee has no active members**, so an operator can never mint access to a
committee that already has people in it.

## 7. Sessions and API clients

- Browser sessions are an `HttpOnly`, `Secure`, `SameSite=Lax` cookie
  (`utsav_session`); the server stores only a hash. They expire after 14 idle
  days or 30 absolute days. Every non-GET request must carry the
  `X-Utsav-Csrf` header (the token comes back in the sign-in / `/api/auth/me`
  response); a missing or wrong header is a 403.
- Scripts and integrations use **API keys** (`X-Api-Key`), not sessions.

## 8. Known gaps to close before wider rollout

- **No rate limiting on `/api/auth/login`, `/signup` and `/redeem-invite`.**
  Put Azure Front Door / WAF rate rules in front, or add an ASP.NET Core rate
  limiter policy, before opening sign-up beyond trusted committees.
- Custom domain per committee, per-tenant PWA manifest/icons and a self-serve
  signup flow are intentionally not built.
- Historical data import for a committee (e.g. Kalipuja) is a separate piece of
  work once its format is known.
