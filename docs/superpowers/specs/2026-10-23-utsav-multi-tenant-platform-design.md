# Utsav — multi-tenant puja committee platform

**Date:** 2026-10-23
**Status:** design, awaiting approval
**Supersedes:** nothing. The existing `Puja` FullStack app and its database are untouched by this work.

---

## 1. Why a new application

The existing app (`/home/atip/sourceCode/Puja/FullStack`) is a proven, in-production
finance and collection system for one Durga Puja committee in Barrackpore. Its schema
is multi-tenant — every table carries `org_id`, and `organizations` / `org_members` /
`org_role_mappings` exist — but its **runtime is single-tenant by construction**:

```csharp
// SessionService.cs:16
public static readonly Guid DefaultOrgId = Guid.Parse("00000000-0000-0000-0000-000000000001");
```

That constant is referenced 61 times across 20 files. `LoadSessionAsync` only ever
resolves membership against it, and `CommitteeBootstrapper` deliberately reuses the same
id on a fresh database. The documented deployment model is therefore *one app instance
plus one database per committee*.

The Kalipuja committee now wants the product, and the stated ambition is to sell it to
many committees with one shared login. That requires per-request tenant resolution,
which the current runtime cannot express.

**This is a fork, not a rewrite.** Roughly 30,000 lines of behaviour — collections,
settlements, budget views, Bengali PDF reports, the feast token system — are
battle-tested through live pujas. Rewriting them would discard the only asset that makes
a two-day deadline conceivable. Commit #1 of the new repository is the current codebase
verbatim; every subsequent commit is a change multi-tenancy actually requires.

### Hard constraints

- **The existing application is never modified.** It is read as a functional reference only.
- **The existing database is never connected to.** Not read, not copied, not migrated.
  A separate database will be provided.
- **Deadline: 2 days**, driven by the Kali Puja 2026 season (approximately two weeks out).

---

## 2. Decisions register

| # | Decision | Rationale |
|---|---|---|
| D1 | **Pooled multi-tenancy** — one database, `org_id` scoping, tenant resolved per request | Onboarding committee #12 is an `INSERT`, not an infrastructure operation. Flat cost. Isolation risk is concentrated in two code chokepoints, not diffuse. |
| D2 | **Azure SQL** | The three reporting views are already T-SQL; Row-Level Security via `SESSION_CONTEXT` is first-class and becomes our isolation backstop; the EF SqlServer path is proven in this codebase. |
| D3 | **Port the frontend verbatim** — no toolchain upgrade | React 18 / Vite 5 / Tailwind 3 stay. Tailwind 3→4 rewrites the config model and touches all 26 pages; it is the fastest way to turn war-tested UI into UI needing re-testing. The upgrade is its own future project. |
| D4 | **Own identity, hardened** | Keep email/password + Google + Microsoft sign-in. Move the session token from `localStorage` + `Authorization: Bearer` into an httpOnly/Secure/SameSite cookie, add CSRF defence, rotation on login, expiry and revocation. No vendor, no per-user cost. |
| D5 | **One account, many memberships, in-app switcher** | `org_members.role` is already per-membership and `sessions.org_id` is already nullable — the schema models this correctly today. |
| D6 | **Platform-provisioned tenants** | A `PlatformAdmin` capability above `AppRole`. No self-serve signup, so no abuse controls, quotas, or abandoned-tenant lifecycle in scope. |
| D7 | **Branding on the tenant row** | Committee name, logo, accent colour → CSS variables in the shell and login, plus QuestPDF/ClosedXML letterhead on exports. |
| D8 | **Asura ported behind its existing flag** | Unchanged behaviour, off unless `Asura:Enabled`. Its read-only SQL guard gets a multi-tenancy audit because generated queries must never cross tenants. |
| D9 | **Name: Utsav** (উৎসব) | Festival-neutral; covers Durga, Kali, Saraswati, Lakshmi equally. Repo and folder `Utsav`, namespaces `Utsav.Api` / `Utsav.Domain` / `Utsav.Infrastructure` / `Utsav.Web`. |
| D10 | **Location:** `/home/atip/sourceCode/Utsav` | Sibling of `/home/atip/sourceCode/Puja`, so no stray edit can land in `FullStack`. |
| D11 | **Git: local now, remote later** | `git init` locally with a deploy workflow ready; remote added when a URL is provided. Nothing published without explicit approval. |
| D12 | **Cross-tenant isolation tests are mandatory** | Non-negotiable. This is other committees' money and donor lists. |
| D13 | **Durga Puja cutover: undecided** | The import pipeline stays generic rather than Durga-specific. No cutover designed. |

### Explicitly deferred

These were requested but do not fit two days without endangering the isolation work.
None require redesign to add later.

| Deferred | Why, and what it needs when revisited |
|---|---|
| **Custom domain per committee** | Wildcard TLS, host→tenant mapping, and — because an httpOnly cookie is scoped to one host — a silent SSO handoff (short-lived signed token minted by a canonical host) so the switcher still works across domains. Multi-day on its own. Branding works on a single URL meanwhile. |
| **Per-tenant PWA identity** | The manifest and install icons are build-time artefacts; per-tenant means a dynamic manifest and generated icon sets per committee. |
| **Toolchain modernisation** (React 19, Vite 7, Tailwind 4) | See D3. Own project, own verification pass. |
| **Kalipuja historical data import** | Blocked: the source format is unknown. Day-2 reality is that Kalipuja gets a seeded empty tenant and starts collecting immediately; history is imported once the files have been seen. |
| **Platform admin console** | v1 ships provisioning as endpoints plus one minimal screen, not a management UI. |
| **Self-serve signup, billing, metering** | Not needed while tenants are hand-provisioned. |

---

## 3. Functionality that must survive the port

Preserved verbatim unless tenancy forces a change:

**Income** — subscriptions/chanda collections, donors, zones and lanes, collectors,
collection payments, advertisements and ad bookings, offerings, special donations,
other incomes, feast collection.

**Expenditure** — expenses, budget lines and budget-vs-actual, vendors, contracts and
contract payments, vendor issue log, advances and settlements, feast expenses.

**Money position** — cash ledger, fund transfers, opening balances, fund position
(cash in hand vs bank), advance balances.

**Operations** — workflow items and notes, schedule, puja calendar days, documents,
notifications and web push, feast QR token issue/redeem/range-assign.

**Reporting** — audit report, expense voucher book, Bengali PDF generation (QuestPDF
with embedded fonts), Excel export (ClosedXML), CSV export, dashboard charts, history,
year rollover.

**Platform** — authentication (local, Google, Entra), membership requests and approval,
five-role permission matrix, API keys, Cosmos-backed audit trail, Asura AI assistant.

**Counts:** 26 frontend pages, 41 entities, 3 SQL views, 17 endpoint groups,
~14k LOC backend, ~16.5k LOC frontend, ~3.4k LOC unit tests.

---

## 4. Architecture

### 4.1 Solution layout

Project structure is inherited, because it is already sound — a four-project separation
with dependencies pointing inward:

```
Utsav/
  src/
    Utsav.Domain/           roles, permission matrix, password hashing — no I/O
    Utsav.Infrastructure/   EF context, entities, migrations, auth services,
                            audit sink, blob storage, push
    Utsav.Api/              minimal-API endpoint groups, authn/authz, reports
    Utsav.Web/              React SPA, served from Utsav.Api/wwwroot on publish
  tests/
    Utsav.UnitTests/        ported, plus new tenancy units
    Utsav.IsolationTests/   NEW — cross-tenant leak suite
  docs/superpowers/
```

Two deliberate changes to the inherited structure:

- **`Utsav.IsolationTests` is a new project**, separate from unit tests, because it needs
  a real database with two seeded tenants and must be runnable as its own CI gate. The
  current repo's `Puja.IntegrationTests` is an empty stub; this replaces that intent with
  something that earns its existence.
- **`Utsav.Web/package.json` gets pruned.** It currently carries `express`, `pg`, and
  `serverless-http` — dead dependencies left from the superseded Node/Netlify generation.
  They are not used by the Vite app and should not be inherited into a new product's
  supply chain.

### 4.2 Fork provenance

The new repository's history starts clean. The first commit message records that the tree
is a verbatim copy of `Puja/FullStack` at its then-current commit, so provenance is
traceable without importing 200 commits of single-tenant history that no longer describe
this product.

The rename (`Puja.*` → `Utsav.*`) is mechanical and happens in that same early commit,
before any behavioural change, so that later diffs show only real changes.

---

## 5. Tenancy

This section is the heart of the design. Everything else is ordinary product work.

### 5.1 The tenant context

`SessionService.DefaultOrgId` is **deleted**. In its place, a scoped service:

```csharp
public interface ITenantContext
{
    /// Throws if no tenant is resolved. Never returns a default.
    Guid TenantId { get; }
    bool HasTenant { get; }
}
```

Deleting the static turns all 61 call sites into **compile errors**. That is the point:
no site can be silently missed, and the compiler — not a code review — guarantees
coverage. There is deliberately no `TenantId ?? default` fallback anywhere; absence of a
tenant is an error, never a guess. The same fail-closed principle the existing
`AppRoleExtensions.TryParseWire` already applies to roles.

### 5.2 Resolution

The tenant comes from the **session row**, not from the request. A client cannot name its
own tenant — no header, no query parameter, no body field is trusted. The authentication
handler loads `sessions.org_id`, verifies an `active` membership for
`(user_id, org_id)`, and populates `ITenantContext`.

Switching committees is an explicit authenticated action (`POST /api/auth/switch-org`)
that re-verifies membership and updates the session row. This means a request can never
be mid-flight against an ambiguous tenant, and a revoked membership stops working on the
next request rather than at session expiry.

### 5.3 Four enforcement layers

Isolation does not depend on anyone remembering to add a filter.

**Layer 1 — the read chokepoint.** `ModuleEndpoints.QueryAsync` funnels **59 read call
sites**. It gains tenant binding: the `@org` parameter is supplied by the method itself
from `ITenantContext`, and a statement that references an org-scoped table without an
`org_id` predicate is **refused at runtime**, not silently executed.

**Layer 2 — the write chokepoint.** `DynamicSql.ExecuteAsync` is already the enforced
single point for all **27 mutations**, with identifiers resolved through
`TableRegistry` and values always bound as parameters. It gains the same treatment:
`@org` bound by the executor, and `INSERT` statements stamped with the tenant rather
than trusting the caller to pass it.

This inverts today's contract. Currently each caller hand-writes `@org` and passes
`Org()`; a new endpoint that forgets it compiles and runs. After this change, tenant
scoping is the executor's responsibility and omitting it is impossible.

**Layer 3 — EF global query filters.** `HasQueryFilter(e => e.OrgId == tenant.TenantId)`
on every org-scoped entity, so LINQ paths (documents, API keys, push, notifications,
feast tokens) are covered without per-query filters. Applied in
`UtsavDbContext.Customizations`, as one table-driven loop over the org-scoped entity
types rather than 35 hand-written lines.

**Layer 4 — Azure SQL Row-Level Security.** A security predicate on every org-scoped
table filtering on `SESSION_CONTEXT('TenantId')`, set per connection by an EF
`DbConnectionInterceptor` calling `sp_set_session_context` with `@read_only = 1` on
connection open.

RLS is the backstop: if layers 1–3 all fail for some table, the database still returns
nothing. For this to be real, two things must hold, and both are design requirements:

- The application's SQL principal is **least-privilege** — it owns no schema and holds no
  `ALTER ANY SECURITY POLICY` permission, so it cannot disable the policy it runs under.
  Migrations run under a separate, higher-privileged principal used only by deployment.
- The connection interceptor sets the context on **every** connection open, including
  pooled reuse, and fails the request if it cannot.

### 5.4 Reporting views

The three views in `DatabaseViews.cs` already carry `org_id` through to their output, and
their internal joins key on `year_id` — which is itself a tenant-unique GUID — so they do
not aggregate across tenants. They are ported as-is, and the isolation suite asserts this
rather than trusting the reading: each view is queried as tenant A with tenant B's data
present, and must return only A's rows.

### 5.5 Storage outside the SQL database

- **Cosmos audit trail** — already partitioned on `/orgId`. Ported unchanged; a new
  container in the new account. Reads must always pass a partition key, never a
  cross-partition query.
- **Blob documents** — already keyed `{orgId}/{guid}-{filename}`. Ported unchanged, which
  means tenant prefixing comes free. Download endpoints must re-verify the blob's prefix
  matches the caller's tenant rather than trusting the stored path.
- **Web push** — `push_subscriptions` is org-scoped; dispatch is already per-org.

---

## 6. Identity and authentication

### 6.1 One account, many memberships

`users` has no `org_id` — identity is already global. The changes are:

- `/api/auth/me` returns the user, the **list of memberships** (org id, name, slug, logo,
  role, status), and the active org. Today it returns a single nullable org.
- Sign-in with exactly one active membership selects it automatically. With several, the
  session starts with `org_id = NULL` and the UI shows a committee chooser. With none,
  the existing `PendingApproval` screen is shown — unchanged behaviour.
- A committee switcher in `AppShell`, which is also the natural place to surface
  "you have a pending request at Kalipuja".
- Membership requests become per-committee: a user can request access to a second
  committee without affecting the first.

### 6.2 Hardening (D4)

Current state: an opaque 32-byte session token in `localStorage`, sent as
`Authorization: Bearer`. The token is therefore readable by any script on the origin, has
no expiry, and is not rotated.

Changes, all contained to `api.ts` (2 call sites), `AuthContext.tsx`, and the
authentication handler:

| Control | Implementation |
|---|---|
| Transport | httpOnly, `Secure`, `SameSite=Lax` cookie. Removes the XSS token-theft path entirely. |
| CSRF | Double-submit token on every mutating request, since cookies are now ambient. |
| Rotation | New token minted on sign-in and on privilege change; old one deleted. |
| Expiry | Absolute and idle expiry on `sessions`, enforced server-side at load. |
| Revocation | Sign-out and role change delete server-side session rows — already possible because tokens are opaque and stored, not self-contained JWTs. |

Scrypt password hashing is retained — `ScryptPasswordHasher` is a sound choice and
changing it would invalidate nothing but add risk.

**This item is first to be cut if day 2 runs short.** It is the only work with no
multi-tenancy dependency, and the new app has no live users during development, so
deferring it endangers nobody.

### 6.3 Platform administration

A `PlatformAdmin` capability sits above `AppRole`, which stays strictly
committee-scoped. It is **not** a sixth `AppRole` value — conflating "admin of a
committee" with "admin of the platform" is how a committee treasurer ends up able to read
another committee's books.

Represented as a flag on `users` plus a separate authorization policy. Platform admins
can provision committees and assign a first admin; they get no implicit read access to
committee financial data.

---

## 7. Provisioning and seeding

`CommitteeBootstrapper` is generalised from "seed this one empty database" into
"provision a tenant":

- `POST /api/platform/committees` — creates the organization row with a generated id
  (**not** the fixed `00000000-...-0001`), seeds its lookups, zones, and first puja year,
  and creates the first admin membership.
- Seed data becomes a **per-festival template** rather than the current hardcoded Durga
  arrays. Kali Puja's `puja_day` lookups are not Mahalaya→Dasami. Because `puja_day`,
  `expense_category`, `offering_type`, and zones are all `lookups` rows and not code, this
  is seed data, not a domain change — the single luckiest fact about this port.
- The ~10 hardcoded `"Durga Puja"` strings (nav subtitle, page title, rollover year-name
  default, login copy, Asura prompts, `index.html` metadata) become tenant-derived or
  neutral.

---

## 8. Branding

Columns added to `organizations`: display name, logo blob reference, accent colour,
optional secondary colour.

- **App shell and login** — the design system is already CSS-variable driven via Tailwind
  and Radix, so branding applies by overriding custom properties on a root element from
  the active tenant. No per-tenant build.
- **Documents and exports** — QuestPDF reports and ClosedXML exports take the committee
  name and logo in their header. This matters most: these are the artefacts a committee
  hands to auditors and donors.
- Logo upload reuses the existing blob storage path, so it is tenant-prefixed already.

---

## 9. Database

- A **new, separate Azure SQL database**, provided by Boss. The existing database is never
  connected to, by any code path or tooling, including during development.
- EF migrations are **re-baselined**: one `InitialCreate` describing the current schema
  plus tenancy changes, rather than inheriting the 17 incremental migrations that narrate a
  single-tenant history. The three views are applied from the same migration, as they are
  today.
- Tenancy schema changes: `organizations` gains branding columns; `users` gains the
  platform-admin flag; `sessions` gains expiry columns; RLS predicates and the security
  policy are created per org-scoped table.

---

## 10. Azure resources

New and separate from the existing deployment: App Service, SQL database (provided),
Cosmos container, Blob container, and **new Google OAuth client plus new Entra app
registration** for the new hostname.

Those last two are on the critical path and cannot be worked around in code: Google and
Microsoft sign-in will not function until the new origin is registered as an authorized
redirect URI. They are listed in §14 as blockers.

---

## 11. Testing and verification

| Suite | Content |
|---|---|
| **`Utsav.IsolationTests`** (new, mandatory) | Two seeded committees with overlapping data shapes. Every read endpoint, every view, and every document/blob/audit path asserted to return nothing belonging to the other tenant. A test that a statement missing its tenant predicate is *refused* rather than run. A test that `sp_set_session_context` is set on pooled connection reuse. A test that a user with no membership in the active org is rejected. |
| **`Utsav.UnitTests`** (ported) | The existing ~3.4k LOC — permission matrix, fund position, settlements, payment-mode parity, audit formatting — carried over so the ported business logic is verified in its new home. |
| **Visual verification** | Because D3 ports the frontend verbatim, UI regressions should be nil; the browser preview is used to confirm the shell, login, switcher, and branded PDF render correctly, and that is the evidence shared rather than a claim. |

CI runs unit + isolation tests on every push, matching the existing workflow's
build-test-deploy shape.

---

## 12. Security policies

1. **Tenant identity is server-derived.** Never from a header, query parameter, or body.
2. **Fail closed.** Unresolved tenant, unknown role, unset session context → reject. No defaults.
3. **Least privilege at the database.** The application principal cannot alter the security policy it runs under. Migrations use a separate principal.
4. **One place for runtime SQL.** `DynamicSql` remains the only site permitted to assemble SQL text, keeping the `EF1002` suppression in a single reviewable location with identifiers whitelisted via `TableRegistry`.
5. **Platform admin ≠ committee admin.** Separate capability, separate policy, no implicit data access.
6. **Secrets stay out of the repository.** Connection strings, OAuth client secrets, VAPID keys, and Asura keys via App Service configuration and user secrets locally.
7. **Asura is read-only and tenant-scoped.** Its `SqlGuard` and `ReadOnlyQueryExecutor` are re-audited so generated queries cannot cross tenants, and it runs under the same RLS session context as everything else.
8. **Audit everything.** The existing automatic audit capture on all 27 mutation sites is preserved and gains the tenant dimension.

---

## 13. Risks

| Risk | Severity | Mitigation |
|---|---|---|
| A cross-tenant data leak — one committee reads another's donors or finances | **Critical** | Four independent enforcement layers; RLS as the backstop; a dedicated isolation suite as a CI gate. This is why D12 is non-negotiable. |
| Two days is genuinely tight | **High** | Scope cut to the bone (§2). The port is mechanical; the thinking is concentrated in §5. Cookie hardening is pre-designated as the cut line. |
| Entra/Google registration not ready | **High** | Boss-blocking, flagged in §14. Local email/password sign-in works without it, so development is not blocked — only social sign-in in the deployed app. |
| Kalipuja's data format still unknown | **Medium** | Decoupled by design. They start on a seeded empty tenant; import is a later, independent project. |
| Re-baselined migrations hide a schema detail an incremental migration encoded | **Medium** | The baseline is generated from the existing model snapshot rather than hand-written, and the views are copied verbatim. |
| Kalipuja is a committee's busiest fortnight and the app will be new | **Medium** | Honest expectation-setting: v1 is the proven feature set on new tenancy plumbing, not new features. |

---

## 14. Sequencing

**Day 1 — isolation.** Fork, rename, re-baseline, new DB wiring. Delete
`DefaultOrgId` → `ITenantContext`. Tenant binding in both chokepoints. EF query filters.
RLS plus connection interceptor. Isolation test suite.

**Day 2 — product.** Session org selection, `/auth/me` memberships, switcher.
Provisioning endpoint plus per-festival seed templates. Branding (shell, login, PDF).
De-hardcode the Durga strings. Cookie and CSRF hardening *(cut line)*. Deploy.

### Blocked on Boss

1. The new Azure SQL connection string.
2. New App Service, Cosmos container, Blob container.
3. **New Google OAuth client** for the new hostname.
4. **New Entra app registration** for the new hostname.
5. Kalipuja's committee name, logo, accent colour, and first admin's email.
6. Kalipuja's old data files, when available (not blocking v1).

---

## 15. Open questions

None blocking. Deliberately unresolved and recorded so they are not mistaken for
oversights: whether Durga Puja eventually migrates onto this platform (D13), and the
source format of Kalipuja's historical data.
