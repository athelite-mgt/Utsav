# Utsav Multi-Tenant Platform Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fork the proven single-tenant Durga Puja app into `Utsav`, a pooled multi-tenant platform where many puja committees share one deployment, one database, and one login, with per-tenant branding.

**Architecture:** Commit #1 is the existing codebase verbatim. Tenancy is then introduced by deleting the `DefaultOrgId` constant (making all 61 call sites compile errors) and binding the tenant inside the two existing SQL chokepoints — `QueryAsync` (59 reads) and `DynamicSql.ExecuteAsync` (27 writes) — backed by EF global query filters and Azure SQL Row-Level Security. Tenant identity is always derived from the `sessions` row server-side, never from the request.

**Tech Stack:** .NET 10, EF Core 10 + Azure SQL, React 18 + Vite 5 + Tailwind 3 + Radix, TanStack Query, QuestPDF, ClosedXML, Cosmos DB (audit), Azure Blob Storage, xUnit, Docker (local SQL Server 2022 for tests).

**Spec:** `docs/superpowers/specs/2026-10-23-utsav-multi-tenant-platform-design.md`

## Global Constraints

- **Never read, write, or connect to the existing database.** No connection string from `Puja/FullStack` appears anywhere in this repo or its user secrets.
- **Never modify `/home/atip/sourceCode/Puja/FullStack`.** It is a read-only functional reference.
- Target framework `net10.0`; `TreatWarningsAsErrors=true`; `Nullable=enable`; `InvariantGlobalization=false` (Bengali collation + `Microsoft.Data.SqlClient` require it).
- Frontend stays React 18 / Vite 5 / Tailwind 3. **No toolchain upgrade.**
- Namespaces are `Utsav.Api`, `Utsav.Domain`, `Utsav.Infrastructure`; frontend package name `utsav-web`.
- No `?? default`, `?? Guid.Empty`, or fallback tenant anywhere. Unresolved tenant is an error.
- `org_id` is never accepted from a request body, header, or query string.
- The fixed id `00000000-0000-0000-0000-000000000001` must not appear in the new codebase.
- Commit after every task. Conventional-commit prefixes (`feat:`, `fix:`, `test:`, `chore:`, `refactor:`).

## Review Focus

1. **Pooled connection reuse** — a connection returned to the pool and handed to a different tenant's request must not retain the previous `SESSION_CONTEXT('TenantId')`. Covered in Task 9, Step 7.
2. **Membership revoked mid-session** — an existing session whose `org_members` row is deleted or set non-active must be rejected on its very next request, not at session expiry. Covered in Task 5, Step 5.
3. **Session with `org_id = NULL`** — a multi-membership user who has not yet chosen a committee must get a clean 403 from every data endpoint, never an empty-but-200 response and never a 500. Covered in Task 6, Step 7.
4. **API-key caller has no session row** — the report-export key must be scoped to its own `api_keys.org_id` and must never resolve to another tenant. Covered in Task 5, Step 7.
5. **Blob download path mismatch** — a document whose stored blob prefix is a different tenant's org id must be refused even if the `documents` row was somehow reachable. Covered in Task 10, Step 7.
6. **Asura's generated queries** — the AI assistant picks which pre-written query to run, so an unscoped entry in its catalogue is reachable by asking the right question. Covered in Task 7, Step 6c and Task 10, Step 7b.
7. **Audit events filed under the wrong partition** — the Cosmos container is partitioned on `/orgId`, so a stale tenant there puts one committee's audit trail in another's partition. Covered in Task 7, Step 6b.

---

## File Structure

| Path | Responsibility |
|---|---|
| `src/Utsav.Domain/Authorization/PlatformCapability.cs` | **New.** Platform-admin capability, deliberately separate from `AppRole`. |
| `src/Utsav.Domain/Tenancy/ITenantContext.cs` | **New.** The tenant abstraction, in Domain so Infrastructure and Api both depend inward on it. |
| `src/Utsav.Infrastructure/Tenancy/TenantContext.cs` | **New.** Scoped, mutable-once implementation. |
| `src/Utsav.Infrastructure/Tenancy/TenantSessionInterceptor.cs` | **New.** `DbConnectionInterceptor` setting `sp_set_session_context`. |
| `src/Utsav.Infrastructure/Data/UtsavDbContext.Tenancy.cs` | **New.** Global query filters over the 35 org-scoped entities. |
| `src/Utsav.Infrastructure/Data/TenantProvisioner.cs` | Replaces `CommitteeBootstrapper`. Creates a tenant + seeds from a festival template. |
| `src/Utsav.Infrastructure/Data/FestivalTemplates.cs` | **New.** Per-festival lookup/zone/day seed data (Durga, Kali, generic). |
| `src/Utsav.Api/Endpoints/ModuleEndpoints.cs` | `QueryAsync` gains tenant binding + predicate enforcement. |
| `src/Utsav.Api/Endpoints/DynamicSql.cs` | `ExecuteAsync` gains tenant binding + insert stamping. |
| `src/Utsav.Api/Endpoints/PlatformEndpoints.cs` | **New.** Committee provisioning. |
| `src/Utsav.Api/Endpoints/TenancyGuard.cs` | **New.** The org-scoped table list + predicate check shared by both chokepoints. |
| `src/Utsav.Web/src/context/TenantContext.tsx` | **New.** Active committee + switcher state. |
| `src/Utsav.Web/src/components/layout/CommitteeSwitcher.tsx` | **New.** Switcher UI in the app shell. |
| `src/Utsav.Web/src/pages/ChooseCommittee.tsx` | **New.** Shown when a user has >1 membership and none selected. |
| `tests/Utsav.IsolationTests/` | **New project.** Two-tenant leak suite against Dockerised SQL Server. |
| `docker-compose.dev.yml` | **New.** Local SQL Server 2022 for development and tests. |

---

## Phase 0 — Fork and foundation

### Task 1: Fork the tree into Utsav and prove it still builds

**Files:**
- Create: `/home/atip/sourceCode/Utsav/**` (copy of `Puja/FullStack`, excluding build artefacts and git history)
- Create: `.gitignore`, `README.md`

**Interfaces:**
- Consumes: nothing.
- Produces: a building solution at `/home/atip/sourceCode/Utsav/Utsav.sln` with projects `Utsav.Domain`, `Utsav.Infrastructure`, `Utsav.Api`, `Utsav.UnitTests`.

- [ ] **Step 1: Copy the tree, excluding artefacts and history**

```bash
cd /home/atip/sourceCode
rsync -a --delete \
  --exclude '.git/' --exclude 'bin/' --exclude 'obj/' \
  --exclude 'node_modules/' --exclude 'wwwroot/' \
  --exclude '.vite/' --exclude 'dist/' \
  --exclude '.code-review-graph/' --exclude '.superpowers/' \
  --exclude 'docs/' \
  Puja/FullStack/ Utsav/
ls Utsav
```

Expected: `src`, `tests`, `tools`, `Directory.Build.props`, `Directory.Packages.props`, `.github`, `CLAUDE.md`.
`docs/` is excluded because `Utsav/docs/superpowers/` already holds the spec and this plan — do not overwrite them.

- [ ] **Step 2: Rename projects, folders, and namespaces**

```bash
cd /home/atip/sourceCode/Utsav
for old in Puja.Api Puja.Domain Puja.Infrastructure Puja.Web Puja.UnitTests Puja.IntegrationTests Puja.DataMigration; do
  new="Utsav.${old#Puja.}"
  [ -d "src/$old" ] && git mv 2>/dev/null || true
  for base in src tests tools; do
    [ -d "$base/$old" ] && mv "$base/$old" "$base/$new"
    [ -f "$base/$new/$old.csproj" ] && mv "$base/$new/$old.csproj" "$base/$new/$new.csproj"
  done
done
# Identifiers and references inside every text file
grep -rl --binary-files=without-match 'Puja' \
  --include='*.cs' --include='*.csproj' --include='*.sln' --include='*.json' \
  --include='*.props' --include='*.yml' --include='*.ts' --include='*.tsx' --include='*.html' . \
  | xargs sed -i 's/\bPujaDbContext\b/UtsavDbContext/g; s/\bPuja\.Api\b/Utsav.Api/g; s/\bPuja\.Domain\b/Utsav.Domain/g; s/\bPuja\.Infrastructure\b/Utsav.Infrastructure/g; s/\bPuja\.Web\b/Utsav.Web/g; s/\bPuja\.UnitTests\b/Utsav.UnitTests/g; s/\bPuja\.IntegrationTests\b/Utsav.IntegrationTests/g; s/\bPuja\.DataMigration\b/Utsav.DataMigration/g'
mv Puja.sln Utsav.sln 2>/dev/null; mv src/Utsav.Infrastructure/Data/PujaDbContext.cs src/Utsav.Infrastructure/Data/UtsavDbContext.cs
mv src/Utsav.Infrastructure/Data/PujaDbContext.Customizations.cs src/Utsav.Infrastructure/Data/UtsavDbContext.Customizations.cs
```

**Do NOT rename** `PujaYear`, `PujaSchedule`, `PujaCalendarDay`, `puja_day`, `puja_years` — those are domain vocabulary and database column names, not product branding. Renaming them would change the schema.

- [ ] **Step 3: Verify the rename left nothing behind**

```bash
cd /home/atip/sourceCode/Utsav
grep -rn --include='*.cs' --include='*.csproj' --include='*.sln' '\bPuja\.\(Api\|Domain\|Infrastructure\|Web\|UnitTests\)\b\|PujaDbContext' . | grep -v bin/ | grep -v obj/
```

Expected: no output.

- [ ] **Step 4: Build and run the inherited unit tests**

```bash
cd /home/atip/sourceCode/Utsav
dotnet build Utsav.sln
dotnet test tests/Utsav.UnitTests
```

Expected: build succeeds with zero warnings (warnings are errors), and the inherited unit tests pass. If a test references a connection string, note it — Task 3 fixes configuration; do not point it at the old database to make it pass.

- [ ] **Step 5: Write .gitignore and README, then init git**

`.gitignore`:

```gitignore
bin/
obj/
node_modules/
dist/
.vite/
src/Utsav.Api/wwwroot/
*.user
.env
.DS_Store
```

`README.md`:

```markdown
# Utsav

Multi-tenant finance and collection management for puja committees.

Forked from the single-committee Durga Puja application; see
`docs/superpowers/specs/2026-10-23-utsav-multi-tenant-platform-design.md`
for the design and `docs/superpowers/plans/` for implementation history.

## Local development

    docker compose -f docker-compose.dev.yml up -d   # SQL Server 2022
    dotnet ef database update -p src/Utsav.Infrastructure -s src/Utsav.Api
    dotnet run --project src/Utsav.Api                # API on :5080
    cd src/Utsav.Web && npm install && npm run dev    # Vite on :5173
```

- [ ] **Step 6: Commit the verbatim fork**

```bash
cd /home/atip/sourceCode/Utsav
git init -b main
git add -A
git commit -m "chore: fork Durga Puja application as Utsav

Verbatim copy of Puja/FullStack with projects and namespaces renamed
Puja.* -> Utsav.*. No behavioural change. Domain vocabulary (PujaYear,
puja_day, puja_years) is deliberately unchanged — it is schema and
domain language, not product branding.

History starts clean rather than importing single-tenant commits that no
longer describe this product."
```

---

### Task 2: Prune dead dependencies and single-tenant configuration

**Files:**
- Modify: `src/Utsav.Web/package.json`
- Modify: `src/Utsav.Api/appsettings.json`, `src/Utsav.Api/appsettings.Development.json`
- Create: `docker-compose.dev.yml`

**Interfaces:**
- Consumes: Task 1's building solution.
- Produces: `docker compose -f docker-compose.dev.yml up -d` yields SQL Server on `localhost:1433`, password from `MSSQL_SA_PASSWORD`.

- [ ] **Step 1: Remove the dead Node-generation dependencies**

In `src/Utsav.Web/package.json`, delete these four `dependencies` entries — they are leftovers from the superseded Netlify/Express generation and are not imported by the Vite app:

```
"express": "^5.2.1",
"pg": "^8.22.0",
"serverless-http": "^3.2.0",
"google-auth-library": "^11.0.0",
```

Also delete the `server`, `start`, `import:excel`, `seed`, and `db:reset` scripts (they invoke `server/index.mjs` and `scripts/`, neither of which exists in this repo), change `"name"` to `"utsav-web"`, and change `"description"` to `"Multi-tenant puja committee finance & collection management"`.

Keep `xlsx` in devDependencies only if `scripts/` is absent — verify with `grep -rn "from \"xlsx\"\|require(\"xlsx\")" src/`. If no hit, remove it too.

- [ ] **Step 2: Verify the frontend still installs, type-checks and builds**

```bash
cd /home/atip/sourceCode/Utsav/src/Utsav.Web
rm -rf node_modules package-lock.json
npm install
npx tsc -b
npm run build
```

Expected: install clean, `tsc -b` silent, `vite build` succeeds. If `tsc` reports a missing module for one of the removed packages, that package *was* in use — restore it and note why.

- [ ] **Step 3: Write the local SQL Server compose file**

`docker-compose.dev.yml`:

```yaml
# Local SQL Server for development and the isolation test suite.
#
# 2022 rather than Azure SQL Edge: Row-Level Security and
# sp_set_session_context must behave exactly as they will in production,
# and Edge omits features we depend on.
services:
  sql:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "${MSSQL_SA_PASSWORD:?set MSSQL_SA_PASSWORD}"
      MSSQL_PID: Developer
    ports:
      - "1433:1433"
    volumes:
      - utsav-sql:/var/opt/mssql
    healthcheck:
      test: ["CMD-SHELL", "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"$$MSSQL_SA_PASSWORD\" -C -Q 'SELECT 1' || exit 1"]
      interval: 10s
      timeout: 5s
      retries: 12

volumes:
  utsav-sql:
```

- [ ] **Step 4: Neutralise inherited single-tenant and production configuration**

`src/Utsav.Api/appsettings.json` — remove the inherited `AzureAd` tenant/client ids and the `Cosmos` endpoint (they belong to the old deployment), leaving structure without values:

```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*",
  "AzureAd": { "Instance": "https://login.microsoftonline.com/", "TenantId": "", "ClientId": "" },
  "Cosmos": { "Endpoint": "", "Database": "utsav", "Container": "audit-events" }
}
```

`src/Utsav.Api/appsettings.Development.json`:

```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "ConnectionStrings": {
    "Default": "Server=localhost,1433;Database=utsav_dev;User Id=sa;Password=${MSSQL_SA_PASSWORD};TrustServerCertificate=True;MultipleActiveResultSets=True"
  }
}
```

- [ ] **Step 5: Confirm no old-deployment secret or connection string survived**

```bash
cd /home/atip/sourceCode/Utsav
grep -rn --include='*.json' --include='*.cs' --include='*.yml' \
  'REDACTED-ID\|REDACTED-ID\|REDACTED-AUDIT-ACCOUNT\|REDACTED-DB\|REDACTED-HOST\|database.windows.net' . | grep -v bin/ | grep -v obj/
```

Expected: no output. Any hit is a value belonging to the existing deployment and must be removed.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "chore: prune dead deps, add local SQL Server, clear inherited config

Drops express/pg/serverless-http/google-auth-library — dead since the
Netlify generation and not imported by the Vite app. Clears the old
deployment's Entra and Cosmos identifiers so this app cannot
accidentally authenticate against, or write audit events into, the
existing committee's resources."
```

---

### Task 3: Re-baseline EF migrations against a fresh database

**Files:**
- Delete: `src/Utsav.Infrastructure/Data/Migrations/2026*.cs` (17 migrations + their `.Designer.cs`)
- Keep: `src/Utsav.Infrastructure/Data/Migrations/DatabaseViews.cs`
- Create: `src/Utsav.Infrastructure/Data/Migrations/<stamp>_InitialCreate.cs`

**Interfaces:**
- Consumes: Task 2's local SQL Server.
- Produces: `dotnet ef database update` creates the full 41-table / 3-view schema from empty.

- [ ] **Step 1: Capture the current table and view inventory as the acceptance target**

```bash
cd /home/atip/sourceCode/Puja/FullStack
grep -c 'public partial class' src/Puja.Infrastructure/Data/Entities/*.cs | wc -l
```

Record the expected object list from the model snapshot before deleting anything:

```bash
grep -o 'b\.ToTable("[a-z_]*")' src/Puja.Infrastructure/Data/Migrations/PujaDbContextModelSnapshot.cs \
  | sed 's/.*"\(.*\)".*/\1/' | sort -u > /tmp/claude-1000/-home-atip-sourceCode-Puja/2db3ff7e-b448-4f1d-8540-dbcf10f1aaa1/scratchpad/expected-tables.txt
wc -l < /tmp/claude-1000/-home-atip-sourceCode-Puja/2db3ff7e-b448-4f1d-8540-dbcf10f1aaa1/scratchpad/expected-tables.txt
```

- [ ] **Step 2: Delete the inherited migrations, keeping the views helper**

```bash
cd /home/atip/sourceCode/Utsav/src/Utsav.Infrastructure/Data/Migrations
rm -f 2026*.cs UtsavDbContextModelSnapshot.cs
ls
```

Expected: only `DatabaseViews.cs` remains.

- [ ] **Step 3: Generate the baseline migration**

```bash
cd /home/atip/sourceCode/Utsav
export MSSQL_SA_PASSWORD='<dev password>'
docker compose -f docker-compose.dev.yml up -d
dotnet ef migrations add InitialCreate \
  -p src/Utsav.Infrastructure -s src/Utsav.Api \
  -o Data/Migrations
```

- [ ] **Step 4: Re-attach the three views to the baseline**

The generated `InitialCreate.cs` will not contain the views — EF does not model them. Add them at the end of `Up` and the start of `Down`, exactly as the inherited migration did:

```csharp
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ... generated table creation ...

        // EF does not model views; applied here so a brand-new database is
        // one `dotnet ef database update` away from complete.
        foreach (var view in DatabaseViews.CreateAll)
        {
            migrationBuilder.Sql(view);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var statement in DatabaseViews.DropAll)
        {
            migrationBuilder.Sql(statement);
        }

        // ... generated table drops ...
    }
```

Add `using Utsav.Infrastructure.Data.Migrations;` if the migration lands in a different namespace.

- [ ] **Step 5: Apply to an empty database and verify the object inventory matches**

```bash
cd /home/atip/sourceCode/Utsav
dotnet ef database update -p src/Utsav.Infrastructure -s src/Utsav.Api
docker exec -i $(docker compose -f docker-compose.dev.yml ps -q sql) \
  /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d utsav_dev -h -1 -W \
  -Q "SET NOCOUNT ON; SELECT name FROM sys.tables ORDER BY name; SELECT name FROM sys.views ORDER BY name;"
```

Expected: every table in `expected-tables.txt` present (minus `__EFMigrationsHistory`, which is extra and expected), plus exactly `v_advance_balances`, `v_budget_vs_actual`, `v_contract_status`.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: re-baseline EF migrations as a single InitialCreate

Replaces 17 incremental migrations that narrated single-tenant history
with one baseline generated from the model snapshot, so a new tenant
database is one \`database update\` from complete. The three reporting
views are re-attached from DatabaseViews.cs, which EF cannot model."
```

---

## Phase 1 — Day 1: Isolation

### Task 4: Introduce ITenantContext and delete DefaultOrgId

**Files:**
- Create: `src/Utsav.Domain/Tenancy/ITenantContext.cs`
- Create: `src/Utsav.Infrastructure/Tenancy/TenantContext.cs`
- Modify: `src/Utsav.Infrastructure/Authentication/SessionService.cs` (delete `DefaultOrgId`)
- Modify: `src/Utsav.Infrastructure/DependencyInjection.cs` (register the scoped service)
- Test: `tests/Utsav.UnitTests/Tenancy/TenantContextTests.cs`

**Interfaces:**
- Consumes: Task 1's solution.
- Produces:
  - `Utsav.Domain.Tenancy.ITenantContext` with `Guid TenantId { get; }` (throws `TenantNotResolvedException` when unset) and `bool HasTenant { get; }`
  - `Utsav.Infrastructure.Tenancy.TenantContext : ITenantContext` with `void Resolve(Guid tenantId)` (throws `InvalidOperationException` on second call)
  - `Utsav.Domain.Tenancy.TenantNotResolvedException`

- [ ] **Step 1: Write the failing tests**

`tests/Utsav.UnitTests/Tenancy/TenantContextTests.cs`:

```csharp
using Utsav.Domain.Tenancy;
using Utsav.Infrastructure.Tenancy;
using Xunit;

namespace Utsav.UnitTests.Tenancy;

public class TenantContextTests
{
    [Fact]
    public void TenantId_throws_when_unresolved()
    {
        var context = new TenantContext();

        Assert.False(context.HasTenant);
        // Fail closed: there is no default tenant, and asking for one that
        // was never established is a bug, not an empty result.
        Assert.Throws<TenantNotResolvedException>(() => context.TenantId);
    }

    [Fact]
    public void Resolve_sets_the_tenant_once()
    {
        var tenant = Guid.NewGuid();
        var context = new TenantContext();

        context.Resolve(tenant);

        Assert.True(context.HasTenant);
        Assert.Equal(tenant, context.TenantId);
    }

    [Fact]
    public void Resolve_refuses_to_change_an_established_tenant()
    {
        var context = new TenantContext();
        context.Resolve(Guid.NewGuid());

        // A request's tenant must be fixed for its whole lifetime. Allowing a
        // second Resolve would make mid-request tenant switching expressible,
        // which is exactly the bug class this type exists to prevent.
        Assert.Throws<InvalidOperationException>(() => context.Resolve(Guid.NewGuid()));
    }

    [Fact]
    public void Resolve_refuses_an_empty_tenant_id()
    {
        var context = new TenantContext();

        Assert.Throws<ArgumentException>(() => context.Resolve(Guid.Empty));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail to compile**

```bash
cd /home/atip/sourceCode/Utsav
dotnet test tests/Utsav.UnitTests --filter FullyQualifiedName~TenantContextTests
```

Expected: build failure — `ITenantContext`, `TenantContext`, `TenantNotResolvedException` do not exist.

- [ ] **Step 3: Write the Domain abstraction**

`src/Utsav.Domain/Tenancy/ITenantContext.cs`:

```csharp
namespace Utsav.Domain.Tenancy;

/// <summary>
/// The committee whose data the current request is allowed to see.
///
/// Lives in Domain, with no dependency on ASP.NET or EF, so Infrastructure
/// and Api both depend inward on it rather than on each other.
///
/// There is deliberately no default and no nullable accessor that silently
/// yields <see cref="Guid.Empty"/>. In a pooled multi-tenant database an
/// unresolved tenant that degrades to "some default" is a cross-tenant data
/// leak, so the only two states are "resolved" and "throws".
/// </summary>
public interface ITenantContext
{
    /// <summary>The active committee. Throws <see cref="TenantNotResolvedException"/> if none.</summary>
    Guid TenantId { get; }

    /// <summary>True when a tenant has been established for this request.</summary>
    bool HasTenant { get; }
}

/// <summary>
/// Thrown when code reaches for the tenant on a request that never
/// established one — an anonymous request, or a signed-in user who has not
/// yet chosen a committee. Surfaces as 403, never as a query against an
/// arbitrary tenant.
/// </summary>
public sealed class TenantNotResolvedException()
    : InvalidOperationException(
        "No committee is active for this request. The caller must be signed in " +
        "with an active membership and a selected committee.");
```

- [ ] **Step 4: Write the Infrastructure implementation**

`src/Utsav.Infrastructure/Tenancy/TenantContext.cs`:

```csharp
using Utsav.Domain.Tenancy;

namespace Utsav.Infrastructure.Tenancy;

/// <summary>
/// Scoped per request. Populated once by the authentication handler, then
/// read by the SQL chokepoints, the EF query filters and the RLS connection
/// interceptor.
///
/// Write-once by design: a request's tenant is fixed the moment
/// authentication resolves it. Switching committees is a separate,
/// authenticated round trip that updates the session row, not a mutation of
/// an in-flight request.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private Guid? _tenantId;

    public bool HasTenant => _tenantId.HasValue;

    public Guid TenantId => _tenantId ?? throw new TenantNotResolvedException();

    public void Resolve(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id must not be empty.", nameof(tenantId));
        }

        if (_tenantId.HasValue)
        {
            throw new InvalidOperationException(
                $"Tenant is already resolved to {_tenantId}. A request's tenant cannot change.");
        }

        _tenantId = tenantId;
    }
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

```bash
dotnet test tests/Utsav.UnitTests --filter FullyQualifiedName~TenantContextTests
```

Expected: 4 passing.

- [ ] **Step 6: Register the service**

In `src/Utsav.Infrastructure/DependencyInjection.cs`, inside `AddUtsavInfrastructure`:

```csharp
        // One instance per request: resolved by the auth handler, read by the
        // query filters, the SQL chokepoints and the RLS interceptor. Both the
        // interface and the concrete type are registered against the same
        // instance — consumers take ITenantContext, only the auth handler
        // takes TenantContext to call Resolve.
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
```

Add `using Utsav.Domain.Tenancy;` and `using Utsav.Infrastructure.Tenancy;`.

- [ ] **Step 7: Delete DefaultOrgId and inventory the breakage**

In `src/Utsav.Infrastructure/Authentication/SessionService.cs`, delete the `DefaultOrgId` field entirely. Then:

```bash
cd /home/atip/sourceCode/Utsav
dotnet build Utsav.sln 2>&1 | grep -c 'error CS'
dotnet build Utsav.sln 2>&1 | grep 'error CS' | sed 's/(.*//' | sort -u
```

Expected: a non-zero error count across roughly 20 files. **This is the designed outcome** — the compiler is now enumerating every site that needs a tenant. Record the file list; Tasks 5–8 work through it. Do not add a stub constant to make the build green.

- [ ] **Step 8: Commit the red state deliberately**

```bash
git add -A
git commit -m "feat!: replace DefaultOrgId with ITenantContext

Deletes the hardcoded single-tenant org id. Every one of its 61 call
sites is now a compile error, which is the point: the compiler
enumerates the work instead of a code review hoping to catch it.

The build is intentionally red at this commit. Tasks 5-8 resolve it.
TenantContext has no default and no nullable accessor — in a pooled
database, an unresolved tenant that degrades to a default is a
cross-tenant leak."
```

---

### Task 5: Resolve the tenant from the session, never from the request

**Files:**
- Modify: `src/Utsav.Infrastructure/Authentication/SessionService.cs`
- Modify: `src/Utsav.Infrastructure/Authentication/SessionPayload.cs`
- Modify: `src/Utsav.Api/Authentication/LegacySessionAuthenticationHandler.cs`
- Modify: `src/Utsav.Api/Authentication/ApiKeyAuthenticationHandler.cs`
- Test: `tests/Utsav.UnitTests/Authentication/SessionTenantResolutionTests.cs`

**Interfaces:**
- Consumes: `TenantContext.Resolve(Guid)`, `ITenantContext` from Task 4.
- Produces:
  - `SessionService.LoadSessionAsync(string token, CancellationToken)` → `SessionPayload?` where `SessionPayload` is now
    `(SessionUser User, SessionOrganization? Org, string? Role, string Status, IReadOnlyList<SessionMembership> Memberships)`
  - `SessionMembership(Guid OrgId, string Name, string Slug, string? LogoUrl, string? AccentColour, string Role, string Status)`
  - `SessionService.SwitchOrgAsync(Guid userId, Guid orgId, string token, CancellationToken)` → `bool` (false when the user has no active membership in that org)

- [ ] **Step 1: Write the failing tests**

`tests/Utsav.UnitTests/Authentication/SessionTenantResolutionTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;
using Xunit;

namespace Utsav.UnitTests.Authentication;

public class SessionTenantResolutionTests
{
    private static UtsavDbContext InMemory(string name) =>
        new(new DbContextOptionsBuilder<UtsavDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task<(UtsavDbContext Db, Guid UserId, Guid OrgA, Guid OrgB)> SeedAsync(string name)
    {
        var db = InMemory(name);
        var userId = Guid.NewGuid();
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();

        db.Users.Add(new User { Id = userId, Email = "a@b.c", AuthProvider = "local", PasswordHash = "x" });
        db.Organizations.Add(new Organization { Id = orgA, Name = "Durga", Slug = "durga", IsActive = true });
        db.Organizations.Add(new Organization { Id = orgB, Name = "Kali", Slug = "kali", IsActive = true });
        await db.SaveChangesAsync();
        return (db, userId, orgA, orgB);
    }

    [Fact]
    public async Task Session_with_null_org_resolves_no_tenant_but_lists_memberships()
    {
        var (db, userId, orgA, orgB) = await SeedAsync(nameof(Session_with_null_org_resolves_no_tenant_but_lists_memberships));
        db.OrgMembers.Add(new OrgMember { Id = Guid.NewGuid(), OrgId = orgA, UserId = userId, Role = "admin", Status = "active" });
        db.OrgMembers.Add(new OrgMember { Id = Guid.NewGuid(), OrgId = orgB, UserId = userId, Role = "viewer", Status = "active" });
        db.Sessions.Add(new Session { Token = "t1", UserId = userId, OrgId = null });
        await db.SaveChangesAsync();

        var session = await new SessionService(db).LoadSessionAsync("t1");

        Assert.NotNull(session);
        Assert.Null(session!.Org);
        Assert.Equal(2, session.Memberships.Count);
        Assert.Equal("active", session.Status);
    }

    [Fact]
    public async Task Session_pinned_to_an_org_resolves_that_org_and_its_role()
    {
        var (db, userId, orgA, _) = await SeedAsync(nameof(Session_pinned_to_an_org_resolves_that_org_and_its_role));
        db.OrgMembers.Add(new OrgMember { Id = Guid.NewGuid(), OrgId = orgA, UserId = userId, Role = "finance", Status = "active" });
        db.Sessions.Add(new Session { Token = "t2", UserId = userId, OrgId = orgA });
        await db.SaveChangesAsync();

        var session = await new SessionService(db).LoadSessionAsync("t2");

        Assert.Equal(orgA, session!.Org!.Id);
        Assert.Equal("finance", session.Role);
    }

    [Fact]
    public async Task Membership_revoked_after_sign_in_stops_working_immediately()
    {
        // Review Focus #2. The session row still names the org, but the
        // membership is gone. The next request must not see that org's data.
        var (db, userId, orgA, _) = await SeedAsync(nameof(Membership_revoked_after_sign_in_stops_working_immediately));
        var member = new OrgMember { Id = Guid.NewGuid(), OrgId = orgA, UserId = userId, Role = "admin", Status = "active" };
        db.OrgMembers.Add(member);
        db.Sessions.Add(new Session { Token = "t3", UserId = userId, OrgId = orgA });
        await db.SaveChangesAsync();

        db.OrgMembers.Remove(member);
        await db.SaveChangesAsync();

        var session = await new SessionService(db).LoadSessionAsync("t3");

        Assert.NotNull(session);
        Assert.Null(session!.Org);
        Assert.Null(session.Role);
        Assert.NotEqual("active", session.Status);
    }

    [Fact]
    public async Task Session_pinned_to_an_org_the_user_only_has_pending_access_to_is_not_active()
    {
        var (db, userId, orgA, _) = await SeedAsync(nameof(Session_pinned_to_an_org_the_user_only_has_pending_access_to_is_not_active));
        db.OrgMembers.Add(new OrgMember { Id = Guid.NewGuid(), OrgId = orgA, UserId = userId, Role = "viewer", Status = "pending" });
        db.Sessions.Add(new Session { Token = "t4", UserId = userId, OrgId = orgA });
        await db.SaveChangesAsync();

        var session = await new SessionService(db).LoadSessionAsync("t4");

        Assert.Null(session!.Org);
        Assert.Equal("pending", session.Status);
    }

    [Fact]
    public async Task SwitchOrg_refuses_an_org_the_user_is_not_an_active_member_of()
    {
        var (db, userId, orgA, orgB) = await SeedAsync(nameof(SwitchOrg_refuses_an_org_the_user_is_not_an_active_member_of));
        db.OrgMembers.Add(new OrgMember { Id = Guid.NewGuid(), OrgId = orgA, UserId = userId, Role = "admin", Status = "active" });
        db.Sessions.Add(new Session { Token = "t5", UserId = userId, OrgId = orgA });
        await db.SaveChangesAsync();
        var sessions = new SessionService(db);

        // No membership in orgB at all — the switch must be refused and the
        // session must stay pinned where it was.
        Assert.False(await sessions.SwitchOrgAsync(userId, orgB, "t5"));
        Assert.Equal(orgA, (await db.Sessions.SingleAsync(s => s.Token == "t5")).OrgId);
    }

    [Fact]
    public async Task SwitchOrg_moves_the_session_to_an_org_the_user_belongs_to()
    {
        var (db, userId, orgA, orgB) = await SeedAsync(nameof(SwitchOrg_moves_the_session_to_an_org_the_user_belongs_to));
        db.OrgMembers.Add(new OrgMember { Id = Guid.NewGuid(), OrgId = orgA, UserId = userId, Role = "admin", Status = "active" });
        db.OrgMembers.Add(new OrgMember { Id = Guid.NewGuid(), OrgId = orgB, UserId = userId, Role = "viewer", Status = "active" });
        db.Sessions.Add(new Session { Token = "t6", UserId = userId, OrgId = orgA });
        await db.SaveChangesAsync();

        Assert.True(await new SessionService(db).SwitchOrgAsync(userId, orgB, "t6"));
        Assert.Equal(orgB, (await db.Sessions.SingleAsync(s => s.Token == "t6")).OrgId);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
cd /home/atip/sourceCode/Utsav
dotnet test tests/Utsav.UnitTests --filter FullyQualifiedName~SessionTenantResolutionTests
```

Expected: build failure — `SessionPayload` has no `Memberships`, and `SwitchOrgAsync` does not exist. Add the `Microsoft.EntityFrameworkCore.InMemory` package reference to `Utsav.UnitTests.csproj` if it is absent.

- [ ] **Step 3: Extend the session payload**

Replace `src/Utsav.Infrastructure/Authentication/SessionPayload.cs`:

```csharp
namespace Utsav.Infrastructure.Authentication;

public sealed record SessionUser(Guid Id, string Email, string? FullName, string AuthProvider);

public sealed record SessionOrganization(
    Guid Id, string Name, string Slug, string? LogoUrl, string? AccentColour);

/// <summary>
/// One committee this user belongs to. The switcher renders this list, so it
/// carries enough branding to show a logo and colour per entry without a
/// second round trip.
/// </summary>
public sealed record SessionMembership(
    Guid OrgId, string Name, string Slug, string? LogoUrl, string? AccentColour, string Role, string Status);

/// <summary>
/// What <c>/auth/me</c> returns, and what the authentication handler resolves
/// on every request.
///
/// <see cref="Org"/> and <see cref="Role"/> are populated only when the
/// session is pinned to a committee the user is an <em>active</em> member of
/// right now — membership is re-verified on every load, so revoking access
/// takes effect on the member's next request rather than at session expiry.
///
/// <see cref="Memberships"/> is always populated, including when
/// <see cref="Org"/> is null: that is the state a multi-committee user is in
/// before choosing one, and the chooser screen needs the list.
/// </summary>
public sealed record SessionPayload(
    SessionUser User,
    SessionOrganization? Org,
    string? Role,
    string Status,
    IReadOnlyList<SessionMembership> Memberships);
```

- [ ] **Step 4: Rewrite SessionService for many memberships**

Replace the body of `src/Utsav.Infrastructure/Authentication/SessionService.cs`:

```csharp
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;

namespace Utsav.Infrastructure.Authentication;

/// <summary>
/// Opaque session tokens stored server-side, so revocation is a row delete
/// rather than a token-expiry wait.
///
/// A session is pinned to at most one committee at a time
/// (<c>sessions.org_id</c>). It starts unpinned when the user belongs to
/// several, and the client pins it with <see cref="SwitchOrgAsync"/>. The
/// tenant therefore always comes from this row — never from a header, query
/// string or body, which a caller could forge.
/// </summary>
public sealed class SessionService(UtsavDbContext db)
{
    public async Task<string> CreateSessionAsync(Guid userId, CancellationToken ct = default)
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

        // Pin automatically when there is exactly one active membership:
        // the overwhelmingly common case, and showing a one-item chooser
        // would be a worse experience than just going in.
        var active = await db.OrgMembers
            .Where(m => m.UserId == userId && m.Status == "active")
            .Select(m => m.OrgId)
            .ToListAsync(ct);

        db.Sessions.Add(new Session
        {
            Token = token,
            UserId = userId,
            OrgId = active.Count == 1 ? active[0] : null,
        });
        await db.SaveChangesAsync(ct);
        return token;
    }

    public Task DestroySessionAsync(string token, CancellationToken ct = default) =>
        db.Sessions.Where(s => s.Token == token).ExecuteDeleteAsync(ct);

    /// <summary>
    /// Pins an existing session to a different committee. Returns false —
    /// changing nothing — when the caller is not an active member there, so a
    /// forged org id cannot move a session somewhere it does not belong.
    /// </summary>
    public async Task<bool> SwitchOrgAsync(Guid userId, Guid orgId, string token, CancellationToken ct = default)
    {
        var allowed = await db.OrgMembers
            .AnyAsync(m => m.UserId == userId && m.OrgId == orgId && m.Status == "active", ct);
        if (!allowed)
        {
            return false;
        }

        var session = await db.Sessions.SingleOrDefaultAsync(s => s.Token == token && s.UserId == userId, ct);
        if (session is null)
        {
            return false;
        }

        session.OrgId = orgId;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Null if the token doesn't resolve to a user at all (missing/expired/invalid).</summary>
    public async Task<SessionPayload?> LoadSessionAsync(string token, CancellationToken ct = default)
    {
        var row = await (
            from s in db.Sessions
            join u in db.Users on s.UserId equals u.Id
            where s.Token == token
            select new { Session = s, User = u }
        ).SingleOrDefaultAsync(ct);

        if (row is null)
        {
            return null;
        }

        var user = new SessionUser(row.User.Id, row.User.Email, row.User.FullName, row.User.AuthProvider);

        var memberships = await (
            from m in db.OrgMembers
            join o in db.Organizations on m.OrgId equals o.Id
            where m.UserId == row.User.Id && o.IsActive
            orderby o.Name
            select new SessionMembership(o.Id, o.Name, o.Slug, o.LogoUrl, o.AccentColour, m.Role, m.Status)
        ).ToListAsync(ct);

        var pinned = row.Session.OrgId;
        if (pinned is null)
        {
            // Signed in, no committee chosen. Status reflects whether there is
            // anything to choose: "active" means the chooser has options,
            // anything else means the PendingApproval screen.
            var any = memberships.Any(m => m.Status == "active");
            return new SessionPayload(user, null, null, any ? "active" : BestStatus(memberships), memberships);
        }

        // Re-verified every request, deliberately: this is what makes a
        // revoked membership stop working immediately rather than at expiry.
        var current = memberships.SingleOrDefault(m => m.OrgId == pinned.Value);
        if (current is null || current.Status != "active")
        {
            return new SessionPayload(user, null, null, current?.Status ?? "none", memberships);
        }

        var org = new SessionOrganization(current.OrgId, current.Name, current.Slug, current.LogoUrl, current.AccentColour);
        return new SessionPayload(user, org, current.Role, "active", memberships);
    }

    private static string BestStatus(IReadOnlyList<SessionMembership> memberships) =>
        memberships.Any(m => m.Status == "pending") ? "pending" : "none";
}
```

`Organization.LogoUrl` and `Organization.AccentColour` do not exist yet — Task 15 adds them. To keep this task independently testable, add the two nullable string properties to `src/Utsav.Infrastructure/Data/Entities/Organization.cs` now and generate the migration in Task 15 where the rest of branding lands:

```csharp
    public string? LogoUrl { get; set; }

    public string? AccentColour { get; set; }
```

- [ ] **Step 5: Run the tests and confirm they pass**

```bash
dotnet test tests/Utsav.UnitTests --filter FullyQualifiedName~SessionTenantResolutionTests
```

Expected: 6 passing. The revoked-membership and refused-switch tests are Review Focus items #2 and part of #3 — they must pass, not be adjusted.

- [ ] **Step 6: Resolve the tenant in the session authentication handler**

In `src/Utsav.Api/Authentication/LegacySessionAuthenticationHandler.cs`, take `TenantContext` in the constructor and resolve it once the session is known to be active:

```csharp
public sealed class LegacySessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SessionService sessions,
    TenantContext tenant)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
```

and after `Context.Items[nameof(SessionPayload)] = session;`:

```csharp
        // The one place a tenant enters the request. Only an active
        // membership in the pinned committee establishes it, so a pending or
        // revoked member reaches the app with no tenant at all and every
        // data endpoint refuses them.
        if (session.Org is not null && session.Status == "active")
        {
            tenant.Resolve(session.Org.Id);
        }
```

Add `using Utsav.Infrastructure.Tenancy;`.

- [ ] **Step 7: Resolve the tenant for API-key callers from the key's own org**

Review Focus #4. In `src/Utsav.Api/Authentication/ApiKeyAuthenticationHandler.cs`, take `TenantContext` the same way, and after the key resolves successfully:

```csharp
        // An API key belongs to exactly one committee. Its tenant comes from
        // the key record, so a key issued by one committee can never read
        // another's report — and because it carries no membership status
        // claim, it still satisfies only the report-export policy.
        tenant.Resolve(key.OrgId);
```

- [ ] **Step 8: Build, run the whole unit suite, commit**

```bash
dotnet build Utsav.sln 2>&1 | grep -c 'error CS'
dotnet test tests/Utsav.UnitTests
git add -A
git commit -m "feat: resolve the tenant from the session row

/auth/me now returns every membership, and a session is pinned to at
most one committee. Membership is re-verified on every session load, so
revoking access takes effect on the member's next request instead of at
session expiry.

The tenant enters the request in exactly two places — the session
handler and the API-key handler — and in both it comes from a server-side
row, never from anything the caller sends."
```

The error count will still be non-zero: the chokepoints and LINQ sites are Tasks 6–8.

---

### Task 6: Bind the tenant inside the read chokepoint

**Files:**
- Create: `src/Utsav.Api/Endpoints/TenancyGuard.cs`
- Modify: `src/Utsav.Api/Endpoints/ModuleEndpoints.cs` (`QueryAsync`, around line 305)
- Test: `tests/Utsav.UnitTests/Endpoints/TenancyGuardTests.cs`

**Interfaces:**
- Consumes: `ITenantContext` from Task 4.
- Produces:
  - `TenancyGuard.OrgScopedTables` → `FrozenSet<string>` of the 35 org-scoped table and view names
  - `TenancyGuard.ReferencedOrgScopedTables(string sql)` → `IReadOnlyList<string>`
  - `TenancyGuard.AssertScoped(string sql, IEnumerable<string> parameterNames)` → throws `TenancyViolationException` when an org-scoped table is referenced without an `org_id` predicate and an `@org` parameter
  - `TenancyViolationException : InvalidOperationException`
  - `ModuleEndpoints.QueryAsync(UtsavDbContext db, string sql, SqlParameter[] parameters, ITenantContext tenant, CancellationToken ct)`

- [ ] **Step 1: Write the failing tests**

`tests/Utsav.UnitTests/Endpoints/TenancyGuardTests.cs`:

```csharp
using Utsav.Api.Endpoints;
using Xunit;

namespace Utsav.UnitTests.Endpoints;

public class TenancyGuardTests
{
    [Fact]
    public void Statement_against_an_org_scoped_table_without_a_predicate_is_refused()
    {
        var violation = Assert.Throws<TenancyViolationException>(() =>
            TenancyGuard.AssertScoped("SELECT id, name FROM donors ORDER BY name", ["@year"]));

        Assert.Contains("donors", violation.Message);
    }

    [Fact]
    public void Statement_with_an_org_predicate_and_an_org_parameter_is_allowed()
    {
        TenancyGuard.AssertScoped(
            "SELECT id, name FROM donors WHERE org_id=@org ORDER BY name", ["@org"]);
    }

    [Fact]
    public void Predicate_without_the_bound_parameter_is_refused()
    {
        // A literal or a differently-named parameter means the executor is not
        // the one supplying the tenant, which is the whole point of the guard.
        Assert.Throws<TenancyViolationException>(() =>
            TenancyGuard.AssertScoped("SELECT id FROM donors WHERE org_id='...'", ["@org"]));
    }

    [Fact]
    public void Aliased_and_qualified_predicates_are_recognised()
    {
        TenancyGuard.AssertScoped(
            """
            SELECT m.id, u.email FROM org_members m JOIN users u ON u.id = m.user_id
             WHERE m.org_id=@org AND m.status='pending'
            """,
            ["@org"]);
    }

    [Fact]
    public void Reporting_views_are_treated_as_org_scoped()
    {
        Assert.Throws<TenancyViolationException>(() =>
            TenancyGuard.AssertScoped("SELECT * FROM v_budget_vs_actual WHERE year_id=@year", ["@year"]));
    }

    [Fact]
    public void Tables_with_no_org_column_need_no_predicate()
    {
        // users and subscription_categories are global; demanding org_id on
        // them would be a false positive that forces developers to work
        // around the guard, which is worse than not having it.
        TenancyGuard.AssertScoped("SELECT id, email FROM users WHERE id=@id", ["@id"]);
    }

    [Fact]
    public void Every_org_scoped_entity_is_listed_in_the_guard()
    {
        // The guard's table list and the EF model must not drift apart. A new
        // org-scoped table that nobody adds here would be unprotected.
        var fromModel = typeof(Utsav.Infrastructure.Data.UtsavDbContext).Assembly
            .GetTypes()
            .Where(t => t.Namespace == "Utsav.Infrastructure.Data.Entities"
                        && t.GetProperty("OrgId") is not null)
            .Count();

        Assert.Equal(fromModel, TenancyGuard.OrgScopedTables.Count);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test tests/Utsav.UnitTests --filter FullyQualifiedName~TenancyGuardTests
```

Expected: build failure — `TenancyGuard` does not exist.

- [ ] **Step 3: Write the guard**

`src/Utsav.Api/Endpoints/TenancyGuard.cs`:

```csharp
using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Utsav.Api.Endpoints;

/// <summary>
/// Thrown when runtime SQL would touch an org-scoped table without the
/// tenant predicate that keeps it inside one committee. A bug, not a user
/// error: it means a statement was written in a way the executor cannot
/// prove is tenant-safe.
/// </summary>
public sealed class TenancyViolationException(string message) : InvalidOperationException(message);

/// <summary>
/// Proves that a runtime-assembled statement is scoped to one committee.
///
/// This exists because the previous single-tenant design made tenant scoping
/// the *caller's* job: every endpoint hand-wrote <c>org_id=@org</c> and passed
/// the constant. Forgetting it compiled and ran, and returned another
/// committee's rows. Here the executor supplies <c>@org</c> and refuses any
/// statement that does not constrain on it, so the failure mode is a loud
/// exception in development rather than a silent leak in production.
///
/// Belt and braces, not the only defence: EF global query filters cover the
/// LINQ paths and Azure SQL row-level security covers everything, including
/// a statement that somehow evades this check.
/// </summary>
public static class TenancyGuard
{
    /// <summary>
    /// Every table and view carrying <c>org_id</c>. Kept in sync with the EF
    /// model by a test, so a new org-scoped table cannot be added without
    /// being protected here.
    /// </summary>
    public static readonly FrozenSet<string> OrgScopedTables = new[]
    {
        "ad_bookings", "advance_settlements", "advances", "advertisers", "api_keys",
        "budget_lines", "cash_ledger", "collection_payments", "collectors",
        "contract_payments", "contracts", "documents", "donors", "expenses",
        "feast_expenses", "feast_tokens", "feasts", "fund_transfers", "lookups",
        "notifications", "offerings", "org_members", "org_role_mappings",
        "puja_calendar_days", "puja_schedule", "puja_years", "push_subscriptions",
        "sessions", "special_donations", "subscriptions", "vendor_issues", "vendors",
        "workflow_items", "workflow_notes", "zones",
        // The reporting views project org_id through and must be scoped too.
        "v_advance_balances", "v_budget_vs_actual", "v_contract_status",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex TableReference = new(
        @"\b(?:FROM|JOIN|INTO|UPDATE)\s+\[?(?<table>\w+)\]?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Matches <c>org_id=@org</c>, <c>m.org_id = @org</c>, <c>[org_id]=@org</c>
    /// and the INSERT form where org_id is a column fed by the @org parameter.
    /// </summary>
    private static readonly Regex OrgPredicate = new(
        @"\[?org_id\]?\s*=\s*@org\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<string> ReferencedOrgScopedTables(string sql) =>
        TableReference.Matches(sql)
            .Select(m => m.Groups["table"].Value)
            .Where(OrgScopedTables.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static void AssertScoped(string sql, IEnumerable<string> parameterNames)
    {
        var scoped = ReferencedOrgScopedTables(sql);
        if (scoped.Count == 0)
        {
            return;
        }

        var hasOrgParameter = parameterNames.Any(n =>
            string.Equals(n.TrimStart('@'), "org", StringComparison.OrdinalIgnoreCase));

        // An INSERT names org_id in its column list rather than a predicate;
        // the parameter being bound is what matters there.
        var isInsert = sql.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase);
        var constrained = isInsert
            ? Regex.IsMatch(sql, @"\borg_id\b", RegexOptions.IgnoreCase)
            : OrgPredicate.IsMatch(sql);

        if (constrained && hasOrgParameter)
        {
            return;
        }

        throw new TenancyViolationException(
            $"Statement touches org-scoped table(s) [{string.Join(", ", scoped)}] without " +
            $"an org_id=@org constraint bound by the executor. " +
            (hasOrgParameter
                ? "The @org parameter is present but the statement does not constrain on it."
                : "No @org parameter was supplied.") +
            " Add the predicate, or query a global table instead.");
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

```bash
dotnet test tests/Utsav.UnitTests --filter FullyQualifiedName~TenancyGuardTests
```

Expected: 7 passing. If `Every_org_scoped_entity_is_listed_in_the_guard` fails with a count mismatch, reconcile `OrgScopedTables` against the model rather than loosening the test — the drift it catches is exactly an unprotected table.

- [ ] **Step 5: Make QueryAsync bind the tenant itself**

In `src/Utsav.Api/Endpoints/ModuleEndpoints.cs`, change the signature and prepend the guard and the parameter:

```csharp
    internal static async Task<List<Dictionary<string, object?>>> QueryAsync(
        UtsavDbContext db, string sql, SqlParameter[] parameters, ITenantContext tenant, CancellationToken ct)
    {
        // The executor supplies the tenant, so no caller can forget it and no
        // caller can override it. Appended rather than expected from the
        // caller: 59 call sites write the predicate, none of them should be
        // trusted to also pass the value.
        TenancyGuard.AssertScoped(sql, parameters.Select(p => p.ParameterName));
        if (TenancyGuard.ReferencedOrgScopedTables(sql).Count > 0)
        {
            parameters = [.. parameters, new SqlParameter("@org", tenant.TenantId)];
        }

        var connection = db.Database.GetDbConnection();
```

Change the `Org()` helper in `AdvanceEndpoints.cs` to take the tenant rather than deleting it — 59 call sites reference it, and keeping the helper is far less churn than inlining a parameter everywhere:

```csharp
    /// <summary>
    /// The @org parameter for hand-written statements. Still needed at call
    /// sites that build their own parameter array for other reasons; the
    /// executor also appends one, and a duplicate is harmless because
    /// QueryAsync only appends when no @org is already present.
    /// </summary>
    internal static SqlParameter Org(ITenantContext tenant) => new("@org", tenant.TenantId);
```

Then thread `ITenantContext` through the endpoint lambdas. Minimal APIs inject it automatically once it is registered, so each lambda gains an `ITenantContext tenant` parameter and each `Org()` becomes `Org(tenant)`.

Make `QueryAsync`'s append conditional to match, so a call site that passes `Org(tenant)` explicitly does not end up with two `@org` parameters:

```csharp
        if (TenancyGuard.ReferencedOrgScopedTables(sql).Count > 0
            && !parameters.Any(p => p.ParameterName is "@org"))
        {
            parameters = [.. parameters, new SqlParameter("@org", tenant.TenantId)];
        }
```

- [ ] **Step 6: Work through the call sites until the read paths compile**

```bash
dotnet build Utsav.sln 2>&1 | grep 'error CS' | sed 's/:.*//' | sort | uniq -c | sort -rn
```

Fix file by file, highest count first. For each: add `ITenantContext tenant` to the endpoint delegate, pass `tenant` to `QueryAsync`, delete the `Org()` entry from the parameter array, keep the `org_id=@org` text in the SQL.

- [ ] **Step 7: Add the unresolved-tenant behaviour test**

Review Focus #3. Append to `tests/Utsav.UnitTests/Endpoints/TenancyGuardTests.cs`:

```csharp
    [Fact]
    public void Unresolved_tenant_surfaces_as_TenantNotResolved_not_an_empty_result()
    {
        // A signed-in user who has not chosen a committee has no tenant. The
        // guard passes (the SQL is well-formed), and reading TenantId is what
        // must fail — loudly, so the endpoint returns 403 rather than
        // quietly querying with Guid.Empty and returning an empty list that
        // looks like "this committee has no donors".
        var tenant = new Utsav.Infrastructure.Tenancy.TenantContext();

        Assert.Throws<Utsav.Domain.Tenancy.TenantNotResolvedException>(() => tenant.TenantId);
    }
```

- [ ] **Step 8: Map TenantNotResolvedException to 403**

In `src/Utsav.Api/Program.cs`, before `app.UseAuthentication()`:

```csharp
// A request that reaches data access without a committee is refused, not
// served from an arbitrary tenant. 403 rather than 401: the caller is
// authenticated, they simply have not selected a committee they belong to.
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (error is TenantNotResolvedException or TenancyViolationException)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "No committee selected" });
        return;
    }

    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
}));
```

Add `using Microsoft.AspNetCore.Diagnostics;` and `using Utsav.Domain.Tenancy;`.

A `TenancyViolationException` is a programming error and would ideally be a 500, but it is mapped here too so a missed predicate in production fails closed — refusing the request — rather than returning a 500 body that some client retries.

- [ ] **Step 9: Run all tests and commit**

```bash
dotnet test tests/Utsav.UnitTests
git add -A
git commit -m "feat: bind the tenant inside the read chokepoint

QueryAsync (59 call sites) now supplies @org itself and refuses any
statement touching an org-scoped table without an org_id=@org
constraint. This inverts the old contract: tenant scoping was the
caller's job and forgetting it compiled, ran, and returned another
committee's rows.

A test asserts the guard's table list matches the EF model, so a new
org-scoped table cannot be added without being protected."
```

---

### Task 7: Bind the tenant inside the write chokepoint

**Files:**
- Modify: `src/Utsav.Api/Endpoints/DynamicSql.cs`
- Modify: `src/Utsav.Api/Endpoints/CrudEndpoints.cs`
- Test: `tests/Utsav.UnitTests/Endpoints/DynamicSqlTenancyTests.cs`

**Interfaces:**
- Consumes: `TenancyGuard.AssertScoped`, `ITenantContext`.
- Produces: `DynamicSql.ExecuteAsync(UtsavDbContext db, string sql, IEnumerable<object> parameters, ITenantContext tenant, CancellationToken ct)` → `Task<int>`

- [ ] **Step 1: Write the failing tests**

`tests/Utsav.UnitTests/Endpoints/DynamicSqlTenancyTests.cs`:

```csharp
using Utsav.Api.Endpoints;
using Xunit;

namespace Utsav.UnitTests.Endpoints;

public class DynamicSqlTenancyTests
{
    [Fact]
    public void Insert_into_an_org_scoped_table_must_name_org_id()
    {
        // TableRegistry deliberately excludes org_id from every column
        // whitelist, so the value can only come from the executor. An INSERT
        // that does not name the column would create an unowned row.
        Assert.Throws<TenancyViolationException>(() =>
            TenancyGuard.AssertScoped(
                "INSERT INTO [donors] ([id],[name]) VALUES (@id,@name)", ["@id", "@name"]));
    }

    [Fact]
    public void Insert_naming_org_id_with_the_bound_parameter_is_allowed()
    {
        TenancyGuard.AssertScoped(
            "INSERT INTO [donors] ([id],[org_id],[name]) VALUES (@id,@org,@name)",
            ["@id", "@org", "@name"]);
    }

    [Fact]
    public void Update_without_an_org_predicate_is_refused()
    {
        // Without the predicate this would update a matching id in ANY
        // committee — the single most dangerous shape in the codebase.
        Assert.Throws<TenancyViolationException>(() =>
            TenancyGuard.AssertScoped("UPDATE [expenses] SET amount=@amount WHERE id=@id", ["@amount", "@id"]));
    }

    [Fact]
    public void Delete_without_an_org_predicate_is_refused()
    {
        Assert.Throws<TenancyViolationException>(() =>
            TenancyGuard.AssertScoped("DELETE FROM org_members WHERE id=@id", ["@id"]));
    }

    [Fact]
    public void Delete_with_an_org_predicate_is_allowed()
    {
        TenancyGuard.AssertScoped("DELETE FROM org_members WHERE id=@id AND org_id=@org", ["@id", "@org"]);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
cd /home/atip/sourceCode/Utsav
dotnet test tests/Utsav.UnitTests --filter FullyQualifiedName~DynamicSqlTenancyTests
```

Expected: the INSERT tests fail. `TenancyGuard.AssertScoped` currently accepts any INSERT that mentions `org_id`, but it does not yet require the mention at all when the column list omits it — confirm which assertions fail before changing the guard.

- [ ] **Step 3: Tighten the guard's INSERT rule**

In `src/Utsav.Api/Endpoints/TenancyGuard.cs`, the INSERT branch of `AssertScoped` already requires `\borg_id\b`. Verify that an INSERT whose column list omits `org_id` therefore throws, and that the message names the table. No code change is needed if Step 2 showed those assertions passing; if the regex matched a trailing comment or string literal, strip comments before matching:

```csharp
    private static readonly Regex LineComments = new(@"--[^\n]*", RegexOptions.Compiled);

    private static string Strip(string sql) => LineComments.Replace(sql, " ");
```

and call `Strip(sql)` at the top of `AssertScoped` and `ReferencedOrgScopedTables`. This matters because several inherited statements carry `--` comments explaining their joins, and a commented-out `org_id` must not satisfy the check.

- [ ] **Step 4: Make ExecuteAsync bind and stamp the tenant**

In `src/Utsav.Api/Endpoints/DynamicSql.cs`:

```csharp
    public static async Task<int> ExecuteAsync(
        UtsavDbContext db, string sql, IEnumerable<object> parameters, ITenantContext tenant, CancellationToken ct)
    {
        var parameterList = parameters as IReadOnlyCollection<object> ?? parameters.ToList();

        // Same inversion as the read path: the executor owns the tenant. This
        // is also the single audit chokepoint for all 27 mutations, so the
        // tenant it binds is the tenant the audit event records — they cannot
        // disagree.
        var names = parameterList.OfType<SqlParameter>().Select(p => p.ParameterName);
        TenancyGuard.AssertScoped(sql, names.Append("@org"));
        if (TenancyGuard.ReferencedOrgScopedTables(sql).Count > 0
            && !parameterList.OfType<SqlParameter>().Any(p => p.ParameterName is "@org"))
        {
            parameterList = [.. parameterList, new SqlParameter("@org", tenant.TenantId)];
        }

        var (verb, table) = ParseVerbAndTable(sql);
```

Note the `names.Append("@org")` in the assert: the guard is told the parameter *will* be bound, so it checks the statement's shape (does it constrain on `org_id`?) rather than the caller's parameter list.

- [ ] **Step 5: Thread the tenant through the CRUD endpoints**

In `src/Utsav.Api/Endpoints/CrudEndpoints.cs`, the generic insert builds its own column list. Make `org_id` unconditional for org-scoped tables:

```csharp
        // TableRegistry never lists org_id, so it cannot arrive from the
        // request body. It is added here, from the session's tenant, for
        // every org-scoped table — which is all of them in the registry.
        columns.Add("org_id");
        values.Add("@org");
        command.Parameters.Add(new SqlParameter("@org", tenant.TenantId));
```

Replace every `new SqlParameter("@org", SessionService.DefaultOrgId)` and `new("@org", SessionService.DefaultOrgId)` with the injected `tenant.TenantId`.

- [ ] **Step 6: Clear the remaining compile errors**

```bash
dotnet build Utsav.sln 2>&1 | grep 'error CS' | sed 's/:.*//' | sort | uniq -c | sort -rn
```

Work through the list. Expected remaining families: `PushEndpoints`, `DocumentEndpoints`, `ApiKeyEndpoints`, `FeastTokenEndpoints`, `AuditEndpoints`, `AuditReportEndpoint`, `ReferenceEndpoints`, `CollectionEndpoints`, `PujaCalendarEndpoints`, `AdvanceEndpoints`, `AdminEndpoints`, `Asura/*`. Each is the same mechanical change: inject `ITenantContext`, use `tenant.TenantId`.

For `PushEndpoints`, the `PushNotificationRequest(SessionService.DefaultOrgId, …)` constructions become `PushNotificationRequest(tenant.TenantId, …)`.

For `DocumentEndpoints`, the blob name `$"{SessionService.DefaultOrgId}/{Guid.NewGuid()}-{…}"` becomes `$"{tenant.TenantId}/{Guid.NewGuid()}-{…}"` — tenant prefixing then comes free, and Task 10 Step 9 asserts the download path check.

- [ ] **Step 6b: Stamp the audit entry with the request's tenant**

`DynamicSql.cs` builds the `AuditEntry` itself (around line 212, `OrgId: SessionService.DefaultOrgId`). The Cosmos container is partitioned on `/orgId`, so this value decides which partition the event lands in — a wrong tenant here files one committee's audit trail under another's.

Thread the tenant into the entry builder and use it:

```csharp
        return new AuditEntry(
            Id: Guid.NewGuid().ToString(),
            // Same tenant the mutation itself was bound to. Taken from the
            // executor rather than re-derived from claims, so the audit event
            // and the row it describes cannot disagree about who owns them.
            OrgId: tenantId,
```

Pass `tenant.TenantId` from `ExecuteAsync` down to the builder. Also confirm the audit *read* path (`AuditEndpoints.cs:69`, which currently does `SessionService.DefaultOrgId.ToString()`) passes the request's tenant as the Cosmos partition key, and that it never issues a cross-partition query — a cross-partition read would scan every committee's audit events.

- [ ] **Step 6c: Scope Asura's pre-written queries**

`src/Utsav.Api/Asura/ToolCatalogue.cs` writes its predicates as `org_id = @orgId` — camelCase, which `TenancyGuard`'s `@org` pattern deliberately does not match (`@org\b` does not match `@orgId`, since `g`→`I` is not a word boundary). Left alone, Asura's queries would be *refused* by the guard rather than silently unscoped, which is the safe failure but still a broken feature.

Rename the parameter to `@org` throughout `ToolCatalogue.cs` so the catalogue and the guard agree, and have `ReadOnlyQueryExecutor` bind it from `ITenantContext` rather than accepting it from the orchestrator:

```bash
sed -i 's/@orgId/@org/g' src/Utsav.Api/Asura/ToolCatalogue.cs
grep -n '@org' src/Utsav.Api/Asura/ToolCatalogue.cs | head
```

Then verify `ReadOnlyQueryExecutor` runs under the same RLS session context as everything else — it uses the same `UtsavDbContext` connection, so the Task 9 interceptor covers it, but confirm it does not open its own connection.

- [ ] **Step 7: Verify the build is fully green and nothing reintroduced a constant**

```bash
dotnet build Utsav.sln
grep -rn --include='*.cs' 'DefaultOrgId\|00000000-0000-0000-0000-000000000001' src tests | grep -v bin/ | grep -v obj/
dotnet test tests/Utsav.UnitTests
```

Expected: build succeeds with zero warnings, the grep is silent, all unit tests pass.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: bind the tenant inside the write chokepoint

DynamicSql.ExecuteAsync (27 mutations) now stamps org_id on inserts and
refuses updates or deletes that lack an org_id predicate — the shape
that would otherwise mutate a matching id in any committee.

The build is green again: every one of the 61 former DefaultOrgId sites
now takes its tenant from the request. A grep for the old constant and
the fixed GUID returns nothing."
```

---

### Task 8: EF global query filters for the LINQ paths

**Files:**
- Create: `src/Utsav.Infrastructure/Data/UtsavDbContext.Tenancy.cs`
- Modify: `src/Utsav.Infrastructure/Data/UtsavDbContext.Customizations.cs` (call the new configuration)
- Test: `tests/Utsav.UnitTests/Data/QueryFilterTests.cs`

**Interfaces:**
- Consumes: `ITenantContext`.
- Produces: `UtsavDbContext.ApplyTenantFilters(ModelBuilder)`; `UtsavDbContext` constructor additionally accepts `ITenantContext?`.

- [ ] **Step 1: Write the failing tests**

`tests/Utsav.UnitTests/Data/QueryFilterTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Utsav.Domain.Tenancy;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;
using Utsav.Infrastructure.Tenancy;
using Xunit;

namespace Utsav.UnitTests.Data;

public class QueryFilterTests
{
    private static UtsavDbContext For(Guid tenant, string name)
    {
        var context = new TenantContext();
        context.Resolve(tenant);
        return new UtsavDbContext(
            new DbContextOptionsBuilder<UtsavDbContext>().UseInMemoryDatabase(name).Options,
            context);
    }

    [Fact]
    public async Task Linq_reads_see_only_the_active_tenants_rows()
    {
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        const string store = nameof(Linq_reads_see_only_the_active_tenants_rows);

        await using (var seed = For(orgA, store))
        {
            seed.Documents.Add(new Document { Id = Guid.NewGuid(), OrgId = orgA, FileName = "a.pdf", BlobName = $"{orgA}/a.pdf" });
            seed.Documents.Add(new Document { Id = Guid.NewGuid(), OrgId = orgB, FileName = "b.pdf", BlobName = $"{orgB}/b.pdf" });
            // IgnoreQueryFilters on the write path is not needed — filters
            // apply to reads. Both rows land regardless of the active tenant.
            await seed.SaveChangesAsync();
        }

        await using var asA = For(orgA, store);
        var visible = await asA.Documents.ToListAsync();

        Assert.Single(visible);
        Assert.Equal("a.pdf", visible[0].FileName);
    }

    [Fact]
    public async Task A_row_belonging_to_another_tenant_is_not_findable_by_id()
    {
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        var foreignId = Guid.NewGuid();
        const string store = nameof(A_row_belonging_to_another_tenant_is_not_findable_by_id);

        await using (var seed = For(orgB, store))
        {
            seed.ApiKeys.Add(new ApiKey { Id = foreignId, OrgId = orgB, Name = "theirs", TokenHash = "h", Prefix = "p" });
            await seed.SaveChangesAsync();
        }

        await using var asA = For(orgA, store);

        // Guessing an id must not be enough. This is the shape that makes an
        // IDOR a cross-committee breach rather than a nuisance.
        Assert.Null(await asA.ApiKeys.SingleOrDefaultAsync(k => k.Id == foreignId));
    }

    [Fact]
    public async Task Global_tables_are_not_filtered()
    {
        var store = nameof(Global_tables_are_not_filtered);
        var orgA = Guid.NewGuid();

        await using (var seed = For(orgA, store))
        {
            seed.Users.Add(new User { Id = Guid.NewGuid(), Email = "x@y.z", AuthProvider = "local", PasswordHash = "h" });
            await seed.SaveChangesAsync();
        }

        await using var asOther = For(Guid.NewGuid(), store);

        // users is global identity — filtering it would break sign-in for
        // everyone outside the active tenant.
        Assert.Single(await asOther.Users.ToListAsync());
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test tests/Utsav.UnitTests --filter FullyQualifiedName~QueryFilterTests
```

Expected: build failure — `UtsavDbContext` has no constructor taking `ITenantContext`.

- [ ] **Step 3: Write the filter configuration**

`src/Utsav.Infrastructure/Data/UtsavDbContext.Tenancy.cs`:

```csharp
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Utsav.Domain.Tenancy;

namespace Utsav.Infrastructure.Data;

/// <summary>
/// Tenant filtering for the LINQ query paths — documents, API keys, push
/// subscriptions, notifications, feast tokens, membership and the entity
/// reads scattered across the endpoints.
///
/// Applied by reflection over every entity with an <c>OrgId</c> property
/// rather than 35 hand-written lines, so adding an org-scoped entity gets
/// filtered automatically instead of needing a developer to remember. The
/// matching test in TenancyGuardTests asserts the raw-SQL guard's table list
/// stays in step with this set.
///
/// This is layer three of four. It cannot be the only one: the generic CRUD
/// and reporting endpoints build SQL at runtime and never go through the EF
/// query pipeline, which is what the chokepoint guards cover, and row-level
/// security covers anything that evades both.
/// </summary>
public partial class UtsavDbContext
{
    private readonly ITenantContext? _tenant;

    public UtsavDbContext(DbContextOptions<UtsavDbContext> options, ITenantContext tenant)
        : this(options) => _tenant = tenant;

    private void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var orgId = entityType.FindProperty("OrgId");
            if (orgId is null || entityType.ClrType is null)
            {
                continue;
            }

            // Organization itself carries Id, not OrgId, so it is untouched —
            // and must be, since the switcher lists committees the user is a
            // member of while a different one is active.
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var property = Expression.Property(parameter, orgId.PropertyInfo!);

            // Reads the tenant lazily, at query time, not at model-build time:
            // the model is cached for the application's lifetime while the
            // tenant changes per request.
            Expression<Func<Guid>> current = () => CurrentTenantOrEmpty();
            Expression tenantValue = Expression.Invoke(current);

            // Nullable OrgId (sessions) compares through the underlying value.
            Expression comparison = orgId.ClrType == typeof(Guid?)
                ? Expression.Equal(property, Expression.Convert(tenantValue, typeof(Guid?)))
                : Expression.Equal(property, tenantValue);

            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(Expression.Lambda(comparison, parameter));
        }
    }

    /// <summary>
    /// Guid.Empty when no tenant is resolved, which matches no row. A
    /// background job or a migration running without a request therefore sees
    /// nothing rather than everything — the safe direction to fail. Code that
    /// genuinely needs cross-tenant reads must say so explicitly with
    /// <c>IgnoreQueryFilters()</c>, which is greppable.
    /// </summary>
    private Guid CurrentTenantOrEmpty() =>
        _tenant is { HasTenant: true } ? _tenant.TenantId : Guid.Empty;
}
```

- [ ] **Step 4: Call it from the model configuration**

At the end of `OnModelCreating` in `src/Utsav.Infrastructure/Data/UtsavDbContext.Customizations.cs` (or wherever the partial's `OnModelCreating` lives), add:

```csharp
        // Last, so it layers on top of the generated configuration rather than
        // being overwritten by it.
        ApplyTenantFilters(modelBuilder);
```

- [ ] **Step 5: Exempt the sessions table from filtering**

`sessions` carries `org_id`, but the authentication handler must read a session row *before* a tenant exists — filtering it would make sign-in impossible. In `ApplyTenantFilters`, skip it:

```csharp
            // Sessions are read to establish the tenant, so they cannot be
            // filtered by it. Scoping is enforced instead by always querying
            // sessions by their 32-byte random token, and by SessionService
            // re-verifying membership on every load.
            if (entityType.ClrType == typeof(Entities.Session))
            {
                continue;
            }
```

Apply the same skip to `OrgMember`: the switcher and the chooser screen must list memberships across committees, and `SessionService` already constrains them by `UserId`.

- [ ] **Step 6: Run the tests and confirm they pass**

```bash
dotnet test tests/Utsav.UnitTests --filter FullyQualifiedName~QueryFilterTests
dotnet test tests/Utsav.UnitTests
```

Expected: the three filter tests pass and nothing else regresses. If inherited tests that construct `UtsavDbContext` with the single-argument constructor now fail, they are exercising the unfiltered path deliberately — leave them, since `_tenant` is null there and `CurrentTenantOrEmpty()` returns `Guid.Empty`, which is the documented safe default.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: EF global query filters over every org-scoped entity

Applied by reflection over entities carrying OrgId, so a new org-scoped
entity is filtered without anyone remembering to add it. sessions and
org_members are deliberately exempt — both are read to establish or
list tenancy, and filtering them would make sign-in and the committee
switcher impossible.

No tenant resolved means Guid.Empty, which matches no row: a job or
migration running outside a request sees nothing rather than everything."
```

---

### Task 9: Azure SQL Row-Level Security as the backstop

**Files:**
- Create: `src/Utsav.Infrastructure/Tenancy/TenantSessionInterceptor.cs`
- Create: `src/Utsav.Infrastructure/Data/Migrations/RowLevelSecurity.cs`
- Create: `src/Utsav.Infrastructure/Data/Migrations/<stamp>_AddRowLevelSecurity.cs`
- Modify: `src/Utsav.Infrastructure/DependencyInjection.cs`

**Interfaces:**
- Consumes: `ITenantContext`.
- Produces:
  - `TenantSessionInterceptor : DbConnectionInterceptor` — sets `SESSION_CONTEXT('TenantId')` on every physical connection open
  - `RowLevelSecurity.CreateAll` / `RowLevelSecurity.DropAll` → `string[]`
  - SQL function `dbo.fn_tenant_predicate(@org_id)` and security policy `dbo.TenantIsolationPolicy`

- [ ] **Step 1: Write the predicate and policy SQL**

`src/Utsav.Infrastructure/Data/Migrations/RowLevelSecurity.cs`:

```csharp
namespace Utsav.Infrastructure.Data.Migrations;

/// <summary>
/// The fourth and last isolation layer: the database itself refuses to return
/// another committee's rows, regardless of what the application asked for.
///
/// The first three layers (the two SQL chokepoint guards and the EF query
/// filters) are application code and can be bypassed by a bug. This one
/// cannot, which is why it exists even though the layers above it should
/// already be sufficient. A missed predicate becomes "no rows returned"
/// instead of "another committee's donor list".
///
/// FILTER controls what reads can see; BLOCK AFTER INSERT/UPDATE stops a row
/// being written into, or moved to, a tenant other than the active one — so a
/// forged org_id in a write cannot plant data in someone else's committee.
///
/// SCHEMABINDING on the predicate is required by SQL Server for a security
/// policy and has a useful side effect: a column rename that would break the
/// predicate fails at migration time rather than silently disabling scoping.
/// </summary>
internal static class RowLevelSecurity
{
    /// <summary>
    /// Every table carrying org_id, excluding sessions and org_members.
    ///
    /// Those two are read to establish and list tenancy — a session must be
    /// loaded before any tenant exists, and the committee switcher must see
    /// memberships across committees. Blocking them would make sign-in
    /// impossible. They are protected instead by the 32-byte random session
    /// token and by membership re-verification on every session load.
    /// </summary>
    private static readonly string[] ProtectedTables =
    [
        "ad_bookings", "advance_settlements", "advances", "advertisers", "api_keys",
        "budget_lines", "cash_ledger", "collection_payments", "collectors",
        "contract_payments", "contracts", "documents", "donors", "expenses",
        "feast_expenses", "feast_tokens", "feasts", "fund_transfers", "lookups",
        "notifications", "offerings", "org_role_mappings", "puja_calendar_days",
        "puja_schedule", "puja_years", "push_subscriptions", "special_donations",
        "subscriptions", "vendor_issues", "vendors", "workflow_items",
        "workflow_notes", "zones",
    ];

    public const string CreatePredicate = """
        CREATE FUNCTION dbo.fn_tenant_predicate(@org_id uniqueidentifier)
            RETURNS TABLE
            WITH SCHEMABINDING
        AS
            RETURN SELECT 1 AS allowed
            WHERE @org_id = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)
               -- The deployment principal runs migrations and the data
               -- import with no tenant set. It is a different SQL login from
               -- the application's, which never has this role.
               OR IS_ROLEMEMBER(N'utsav_deploy') = 1;
        """;

    public static string[] CreateAll =>
    [
        CreatePredicate,
        $"""
        CREATE SECURITY POLICY dbo.TenantIsolationPolicy
        {string.Join(",\n", ProtectedTables.Select(t =>
            $"  ADD FILTER PREDICATE dbo.fn_tenant_predicate(org_id) ON dbo.{t},\n" +
            $"  ADD BLOCK PREDICATE dbo.fn_tenant_predicate(org_id) ON dbo.{t} AFTER INSERT,\n" +
            $"  ADD BLOCK PREDICATE dbo.fn_tenant_predicate(org_id) ON dbo.{t} AFTER UPDATE"))}
        WITH (STATE = ON);
        """,
    ];

    public static string[] DropAll =>
    [
        "DROP SECURITY POLICY IF EXISTS dbo.TenantIsolationPolicy;",
        "DROP FUNCTION IF EXISTS dbo.fn_tenant_predicate;",
    ];

    /// <summary>
    /// The least-privileged role the application connects as. Critically it is
    /// NOT db_owner and holds no ALTER ANY SECURITY POLICY, so the app cannot
    /// disable the policy it runs under even if compromised.
    /// </summary>
    public const string CreateRoles = """
        IF DATABASE_PRINCIPAL_ID(N'utsav_app') IS NULL
            CREATE ROLE utsav_app;
        IF DATABASE_PRINCIPAL_ID(N'utsav_deploy') IS NULL
            CREATE ROLE utsav_deploy;
        ALTER ROLE db_datareader ADD MEMBER utsav_app;
        ALTER ROLE db_datawriter ADD MEMBER utsav_app;
        """;
}
```

- [ ] **Step 2: Create the migration**

```bash
cd /home/atip/sourceCode/Utsav
dotnet ef migrations add AddRowLevelSecurity -p src/Utsav.Infrastructure -s src/Utsav.Api -o Data/Migrations
```

Then make its body:

```csharp
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(RowLevelSecurity.CreateRoles);
        foreach (var statement in RowLevelSecurity.CreateAll)
        {
            migrationBuilder.Sql(statement);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var statement in RowLevelSecurity.DropAll)
        {
            migrationBuilder.Sql(statement);
        }
    }
```

- [ ] **Step 3: Write the connection interceptor**

`src/Utsav.Infrastructure/Tenancy/TenantSessionInterceptor.cs`:

```csharp
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Utsav.Domain.Tenancy;

namespace Utsav.Infrastructure.Tenancy;

/// <summary>
/// Stamps the active committee onto every physical database connection, which
/// is what the row-level security predicate reads.
///
/// This must run on connection OPEN, not once per DbContext: connections are
/// pooled, and a connection handed back to the pool and reissued to another
/// committee's request would otherwise still carry the previous tenant's
/// session context — a cross-tenant read with no code bug anywhere above it.
///
/// @read_only = 1 means the value cannot be changed again for the life of the
/// connection, so nothing downstream (a stored procedure, an injected
/// statement) can re-point it at another committee.
/// </summary>
public sealed class TenantSessionInterceptor(ITenantContext tenant) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken ct = default)
    {
        await ApplyAsync(connection, ct);
        await base.ConnectionOpenedAsync(connection, eventData, ct);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyAsync(connection, CancellationToken.None).GetAwaiter().GetResult();
        base.ConnectionOpened(connection, eventData);
    }

    private async Task ApplyAsync(DbConnection connection, CancellationToken ct)
    {
        if (!tenant.HasTenant)
        {
            // No tenant: leave the context unset. The predicate then matches
            // nothing, so an unscoped connection reads no rows rather than
            // all of them.
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC sp_set_session_context @key = N'TenantId', @value = @tenant, @read_only = 1;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@tenant";
        parameter.Value = tenant.TenantId;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(ct);
    }
}
```

- [ ] **Step 4: Register the interceptor**

In `src/Utsav.Infrastructure/DependencyInjection.cs`, where `AddDbContext` is configured:

```csharp
        services.AddScoped<TenantSessionInterceptor>();
        services.AddDbContext<UtsavDbContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString);
            // Scoped interceptor resolved per request so it sees that
            // request's tenant, not a singleton's stale one.
            options.AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>());
        });
```

- [ ] **Step 5: Apply the migration and verify the policy is on**

```bash
cd /home/atip/sourceCode/Utsav
dotnet ef database update -p src/Utsav.Infrastructure -s src/Utsav.Api
docker exec -i $(docker compose -f docker-compose.dev.yml ps -q sql) \
  /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d utsav_dev -h -1 -W \
  -Q "SET NOCOUNT ON; SELECT name, is_enabled FROM sys.security_policies; SELECT COUNT(*) FROM sys.security_predicates;"
```

Expected: `TenantIsolationPolicy  1`, and a predicate count of `99` (33 tables × 3 predicates).

- [ ] **Step 6: Verify the predicate actually blocks at the SQL level**

```bash
docker exec -i $(docker compose -f docker-compose.dev.yml ps -q sql) \
  /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d utsav_dev -h -1 -W -Q "
SET NOCOUNT ON;
DECLARE @a uniqueidentifier = NEWID(), @b uniqueidentifier = NEWID();
INSERT INTO organizations (id, name, slug, is_active, created_at) VALUES (@a,'A','a',1,SYSUTCDATETIME()),(@b,'B','b',1,SYSUTCDATETIME());
EXEC sp_set_session_context @key=N'TenantId', @value=@a;
INSERT INTO zones (id, org_id, name, sort_order) VALUES (NEWID(), @a, 'A zone', 1);
SELECT 'as-A' = 'as-A', COUNT(*) AS visible FROM zones;
"
```

Expected: `visible = 1`. Then confirm the block predicate refuses a foreign write — this must fail:

```bash
docker exec -i $(docker compose -f docker-compose.dev.yml ps -q sql) \
  /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d utsav_dev -h -1 -W -Q "
DECLARE @a uniqueidentifier = (SELECT id FROM organizations WHERE slug='a');
DECLARE @b uniqueidentifier = (SELECT id FROM organizations WHERE slug='b');
EXEC sp_set_session_context @key=N'TenantId', @value=@a;
INSERT INTO zones (id, org_id, name, sort_order) VALUES (NEWID(), @b, 'planted', 1);
"
```

Expected: error 33504, "The attempted operation failed because the target object ... blocked by the security policy". If this *succeeds*, the BLOCK predicates are not attached — stop and fix before continuing.

- [ ] **Step 7: Write the pooled-connection-reuse test**

Review Focus #1. This is the failure mode with no application-code symptom, so it needs its own test. It belongs in the isolation project created in Task 10 — add it there as `tests/Utsav.IsolationTests/PooledConnectionTests.cs` and run it as part of Task 10 Step 8.

```csharp
using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Data;
using Xunit;

namespace Utsav.IsolationTests;

[Collection(nameof(DatabaseFixture))]
public class PooledConnectionTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task A_connection_reused_by_another_tenant_does_not_retain_the_previous_context()
    {
        // Open, use, and release a connection as tenant A, enough times that
        // the pool is certain to hand the same physical connection back.
        for (var i = 0; i < 5; i++)
        {
            await using var asA = fixture.ContextFor(fixture.TenantA);
            await asA.Zones.ToListAsync();
        }

        await using var asB = fixture.ContextFor(fixture.TenantB);
        var zones = await asB.Zones.ToListAsync();

        // B has exactly one zone of its own in the fixture seed. Seeing A's
        // would mean the pooled connection carried A's session context.
        Assert.All(zones, z => Assert.Equal(fixture.TenantB, z.OrgId));
        Assert.Single(zones);
    }
}
```

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: Azure SQL row-level security as the isolation backstop

FILTER predicates scope reads; BLOCK predicates on INSERT and UPDATE
stop a forged org_id planting or moving a row into another committee.
A connection interceptor sets SESSION_CONTEXT on every physical
connection open — not once per DbContext — because a pooled connection
reissued to another tenant would otherwise carry the previous one's
context, which is a leak with no bug anywhere in application code.

The predicate exempts a separate utsav_deploy role used only by
migrations; the application's own principal is db_datareader/writer
only and cannot alter the policy it runs under."
```

---

### Task 10: The cross-tenant isolation suite

**Files:**
- Create: `tests/Utsav.IsolationTests/Utsav.IsolationTests.csproj`
- Create: `tests/Utsav.IsolationTests/DatabaseFixture.cs`
- Create: `tests/Utsav.IsolationTests/ReadIsolationTests.cs`
- Create: `tests/Utsav.IsolationTests/ViewIsolationTests.cs`
- Create: `tests/Utsav.IsolationTests/WriteIsolationTests.cs`
- Create: `tests/Utsav.IsolationTests/DocumentIsolationTests.cs`
- Create: `tests/Utsav.IsolationTests/PooledConnectionTests.cs` (from Task 9 Step 7)
- Delete: `tests/Utsav.IntegrationTests/` (the inherited empty stub)

**Interfaces:**
- Consumes: everything from Tasks 4–9.
- Produces: `DatabaseFixture` with `Guid TenantA`, `Guid TenantB`, `UtsavDbContext ContextFor(Guid tenant)`, `HttpClient ClientFor(Guid tenant)`.

- [ ] **Step 1: Create the project and delete the inherited stub**

```bash
cd /home/atip/sourceCode/Utsav
rm -rf tests/Utsav.IntegrationTests
dotnet new xunit -o tests/Utsav.IsolationTests -f net10.0
rm tests/Utsav.IsolationTests/UnitTest1.cs
dotnet sln Utsav.sln remove tests/Utsav.IntegrationTests/Utsav.IntegrationTests.csproj 2>/dev/null
dotnet sln Utsav.sln add tests/Utsav.IsolationTests/Utsav.IsolationTests.csproj
dotnet add tests/Utsav.IsolationTests reference src/Utsav.Api src/Utsav.Infrastructure src/Utsav.Domain
dotnet add tests/Utsav.IsolationTests package Microsoft.AspNetCore.Mvc.Testing
```

- [ ] **Step 2: Write the two-tenant fixture**

`tests/Utsav.IsolationTests/DatabaseFixture.cs`:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;
using Utsav.Infrastructure.Tenancy;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// Two committees with deliberately identical-looking data, so a leak shows
/// up as a wrong count rather than as an obviously foreign row. Both have a
/// donor named "Ramen Das" and a zone named "16 Feet", because a test that
/// only catches leaks of distinctive data does not catch much.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public Guid TenantA { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public Guid TenantB { get; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public Guid UserInBoth { get; } = Guid.NewGuid();
    public Guid UserInAOnly { get; } = Guid.NewGuid();
    public string TokenForA { get; } = "aaaa" + Guid.NewGuid().ToString("N");
    public string TokenForB { get; } = "bbbb" + Guid.NewGuid().ToString("N");
    public string TokenUnpinned { get; } = "cccc" + Guid.NewGuid().ToString("N");

    private string _connectionString = "";
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        var password = Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD")
            ?? throw new InvalidOperationException(
                "Set MSSQL_SA_PASSWORD and run: docker compose -f docker-compose.dev.yml up -d");

        var database = $"utsav_iso_{Guid.NewGuid():N}";
        _connectionString =
            $"Server=localhost,1433;Database={database};User Id=sa;Password={password};TrustServerCertificate=True";

        await using (var master = new UtsavDbContext(Options(
            _connectionString.Replace($"Database={database}", "Database=master")), Unscoped()))
        {
            await master.Database.ExecuteSqlRawAsync($"CREATE DATABASE [{database}]");
        }

        await using (var db = new UtsavDbContext(Options(_connectionString), Unscoped()))
        {
            await db.Database.MigrateAsync();
            await SeedAsync(db);
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Default", _connectionString));
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    public UtsavDbContext ContextFor(Guid tenant)
    {
        var context = new TenantContext();
        context.Resolve(tenant);
        return new UtsavDbContext(Options(_connectionString), context);
    }

    /// <summary>An HTTP client whose session cookie/token pins it to one committee.</summary>
    public HttpClient ClientFor(string token)
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static DbContextOptions<UtsavDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<UtsavDbContext>().UseSqlServer(connectionString).Options;

    private static TenantContext Unscoped() => new();

    private async Task SeedAsync(UtsavDbContext db)
    {
        db.Organizations.AddRange(
            new Organization { Id = TenantA, Name = "Durga Committee", Slug = "durga", IsActive = true },
            new Organization { Id = TenantB, Name = "Kali Committee", Slug = "kali", IsActive = true });

        db.Users.AddRange(
            new User { Id = UserInBoth, Email = "both@test.local", AuthProvider = "local", PasswordHash = "x" },
            new User { Id = UserInAOnly, Email = "aonly@test.local", AuthProvider = "local", PasswordHash = "x" });

        db.OrgMembers.AddRange(
            new OrgMember { Id = Guid.NewGuid(), OrgId = TenantA, UserId = UserInBoth, Role = "admin", Status = "active" },
            new OrgMember { Id = Guid.NewGuid(), OrgId = TenantB, UserId = UserInBoth, Role = "viewer", Status = "active" },
            new OrgMember { Id = Guid.NewGuid(), OrgId = TenantA, UserId = UserInAOnly, Role = "finance", Status = "active" });

        db.Sessions.AddRange(
            new Session { Token = TokenForA, UserId = UserInBoth, OrgId = TenantA },
            new Session { Token = TokenForB, UserId = UserInBoth, OrgId = TenantB },
            new Session { Token = TokenUnpinned, UserId = UserInBoth, OrgId = null });

        foreach (var (tenant, year) in new[] { (TenantA, 2026), (TenantB, 2026) })
        {
            var yearId = Guid.NewGuid();
            db.PujaYears.Add(new PujaYear { Id = yearId, OrgId = tenant, Year = year, Name = $"Puja {year}", Target = 100000, ProjectionPct = 6.5m, IsActive = true });

            var zoneId = Guid.NewGuid();
            db.Zones.Add(new Zone { Id = zoneId, OrgId = tenant, Name = "16 Feet", SortOrder = 1 });

            var donorId = Guid.NewGuid();
            db.Donors.Add(new Donor { Id = donorId, OrgId = tenant, Name = "Ramen", Surname = "Das", ZoneId = zoneId });

            db.Subscriptions.Add(new Subscription
            {
                Id = Guid.NewGuid(), OrgId = tenant, DonorId = donorId, YearId = yearId,
                Projected = tenant == TenantA ? 5000 : 7000,
                Billed = tenant == TenantA ? 5000 : 7000,
                Collected = tenant == TenantA ? 5000 : 7000,
            });

            db.BudgetLines.Add(new BudgetLine { Id = Guid.NewGuid(), OrgId = tenant, YearId = yearId, Category = "Decoration", Forecast = tenant == TenantA ? 20000 : 30000, SortOrder = 1 });
            db.Expenses.Add(new Expense { Id = Guid.NewGuid(), OrgId = tenant, YearId = yearId, Category = "Decoration", Amount = tenant == TenantA ? 1000 : 2000, SpentOn = new DateOnly(year, 10, 1) });
            db.Documents.Add(new Document { Id = Guid.NewGuid(), OrgId = tenant, FileName = "receipt.pdf", BlobName = $"{tenant}/receipt.pdf" });
        }

        await db.SaveChangesAsync();
    }
}

[CollectionDefinition(nameof(DatabaseFixture))]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
```

- [ ] **Step 3: Make Program.cs reachable from the test project**

`Program.cs` is a top-level-statements file, so its implicit `Program` class is internal. Add to `src/Utsav.Api/Utsav.Api.csproj`:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Utsav.IsolationTests" />
  </ItemGroup>
```

- [ ] **Step 4: Write the read isolation tests**

`tests/Utsav.IsolationTests/ReadIsolationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Utsav.IsolationTests;

[Collection(nameof(DatabaseFixture))]
public class ReadIsolationTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Every GET route that returns committee data. A new data endpoint added
    /// without being listed here is the gap this suite exists to close, so
    /// treat an addition to the API as an addition to this list.
    /// </summary>
    public static TheoryData<string> DataRoutes =>
    [
        "/api/donors", "/api/subscriptions", "/api/advertisers", "/api/ad_bookings",
        "/api/special_donations", "/api/offerings", "/api/expenses", "/api/budget_lines",
        "/api/cash_ledger", "/api/fund_transfers", "/api/vendors", "/api/contracts",
        "/api/advances", "/api/feasts", "/api/feast_expenses", "/api/workflow_items",
        "/api/puja_schedule", "/api/zones", "/api/collectors", "/api/lookups",
        "/api/puja_years", "/api/documents", "/api/users", "/api/api_keys",
    ];

    [Theory]
    [MemberData(nameof(DataRoutes))]
    public async Task Route_returns_only_the_active_committees_rows(string route)
    {
        using var asA = fixture.ClientFor(fixture.TokenForA);
        using var asB = fixture.ClientFor(fixture.TokenForB);

        var fromA = await asA.GetAsync(route);
        var fromB = await asB.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, fromA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fromB.StatusCode);

        var rowsA = await fromA.Content.ReadFromJsonAsync<JsonElement>();
        var rowsB = await fromB.Content.ReadFromJsonAsync<JsonElement>();

        foreach (var row in rowsA.EnumerateArray())
        {
            if (row.TryGetProperty("org_id", out var org))
            {
                Assert.Equal(fixture.TenantA.ToString(), org.GetGuid().ToString());
            }
        }

        foreach (var row in rowsB.EnumerateArray())
        {
            if (row.TryGetProperty("org_id", out var org))
            {
                Assert.Equal(fixture.TenantB.ToString(), org.GetGuid().ToString());
            }
        }
    }

    [Theory]
    [MemberData(nameof(DataRoutes))]
    public async Task Route_refuses_a_session_with_no_committee_selected(string route)
    {
        // Review Focus #3: a clean 403, never an empty 200 that reads as
        // "this committee has no data", and never a 500.
        using var unpinned = fixture.ClientFor(fixture.TokenUnpinned);

        var response = await unpinned.GetAsync(route);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Fund_position_figures_do_not_include_the_other_committee()
    {
        using var asA = fixture.ClientFor(fixture.TokenForA);
        using var asB = fixture.ClientFor(fixture.TokenForB);

        var a = await (await asA.GetAsync("/api/fund_position")).Content.ReadFromJsonAsync<JsonElement>();
        var b = await (await asB.GetAsync("/api/fund_position")).Content.ReadFromJsonAsync<JsonElement>();

        // Seeded 5000 collected for A and 7000 for B. Equal figures would mean
        // both are summing the same (combined) set of rows.
        Assert.NotEqual(a.ToString(), b.ToString());
    }
}
```

- [ ] **Step 5: Write the view isolation tests**

`tests/Utsav.IsolationTests/ViewIsolationTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Utsav.IsolationTests;

[Collection(nameof(DatabaseFixture))]
public class ViewIsolationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Budget_vs_actual_does_not_aggregate_across_committees()
    {
        // The view's inner subqueries group by year_id and category without
        // naming org_id. That is safe only because year_id is itself
        // tenant-unique — this test is what proves the reasoning rather than
        // trusting it, and would fail loudly if the view were ever rewritten
        // to join on category alone.
        await using var asA = fixture.ContextFor(fixture.TenantA);

        var rows = await asA.VBudgetVsActuals.ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal(fixture.TenantA, r.OrgId));
        // A spent 1000 on Decoration, B spent 2000. Seeing 3000 means the
        // expense subquery merged both committees.
        Assert.All(rows.Where(r => r.Category == "Decoration"), r => Assert.Equal(1000m, r.Actual));
    }

    [Fact]
    public async Task Contract_status_view_is_scoped()
    {
        await using var asB = fixture.ContextFor(fixture.TenantB);
        var rows = await asB.VContractStatuses.ToListAsync();
        Assert.All(rows, r => Assert.Equal(fixture.TenantB, r.OrgId));
    }

    [Fact]
    public async Task Advance_balances_view_is_scoped()
    {
        await using var asB = fixture.ContextFor(fixture.TenantB);
        var rows = await asB.VAdvanceBalances.ToListAsync();
        Assert.All(rows, r => Assert.Equal(fixture.TenantB, r.OrgId));
    }
}
```

- [ ] **Step 6: Write the write isolation tests**

`tests/Utsav.IsolationTests/WriteIsolationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Utsav.IsolationTests;

[Collection(nameof(DatabaseFixture))]
public class WriteIsolationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task A_created_row_is_stamped_with_the_callers_committee_not_the_bodys()
    {
        using var asA = fixture.ClientFor(fixture.TokenForA);

        // Forging org_id in the body must be ignored: TableRegistry never
        // lists the column, so it is dropped before the statement is built.
        var response = await asA.PostAsJsonAsync("/api/vendors", new
        {
            name = "Forged Vendor",
            org_id = fixture.TenantB,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = fixture.ContextFor(fixture.TenantA);
        var vendor = await db.Vendors.SingleAsync(v => v.Name == "Forged Vendor");
        Assert.Equal(fixture.TenantA, vendor.OrgId);
    }

    [Fact]
    public async Task Updating_another_committees_row_by_id_affects_nothing()
    {
        await using var asB = fixture.ContextFor(fixture.TenantB);
        var theirDonor = await asB.Donors.FirstAsync();
        var originalName = theirDonor.Name;

        using var asA = fixture.ClientFor(fixture.TokenForA);
        var response = await asA.PatchAsJsonAsync($"/api/donors/{theirDonor.Id}", new { name = "Hijacked" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var verify = fixture.ContextFor(fixture.TenantB);
        Assert.Equal(originalName, (await verify.Donors.SingleAsync(d => d.Id == theirDonor.Id)).Name);
    }

    [Fact]
    public async Task Deleting_another_committees_row_by_id_affects_nothing()
    {
        await using var asB = fixture.ContextFor(fixture.TenantB);
        var theirZone = await asB.Zones.FirstAsync();

        using var asA = fixture.ClientFor(fixture.TokenForA);
        var response = await asA.DeleteAsync($"/api/zones/{theirZone.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var verify = fixture.ContextFor(fixture.TenantB);
        Assert.True(await verify.Zones.AnyAsync(z => z.Id == theirZone.Id));
    }
}
```

- [ ] **Step 7: Write the document and blob isolation test**

Review Focus #5. `tests/Utsav.IsolationTests/DocumentIsolationTests.cs`:

```csharp
using System.Net;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Utsav.IsolationTests;

[Collection(nameof(DatabaseFixture))]
public class DocumentIsolationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Downloading_another_committees_document_by_id_is_refused()
    {
        await using var asB = fixture.ContextFor(fixture.TenantB);
        var theirDocument = await asB.Documents.FirstAsync();

        using var asA = fixture.ClientFor(fixture.TokenForA);
        var response = await asA.GetAsync($"/api/documents/{theirDocument.Id}/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_documents_blob_prefix_always_matches_its_owning_committee()
    {
        // The guard against a stored path that disagrees with the row: a blob
        // named with another tenant's prefix would let a path-based fetch
        // cross committees even though the row lookup was scoped.
        await using var db = fixture.ContextFor(fixture.TenantA);

        var documents = await db.Documents.ToListAsync();

        Assert.NotEmpty(documents);
        Assert.All(documents, d =>
            Assert.StartsWith($"{d.OrgId}/", d.BlobName, StringComparison.Ordinal));
    }
}
```

Then add the matching server-side check in `src/Utsav.Api/Endpoints/DocumentEndpoints.cs`, in both the download and delete handlers, after the row is loaded:

```csharp
        // Defence in depth against a row whose stored path disagrees with its
        // owner — the row lookup is already tenant-scoped, but the blob fetch
        // uses the path, and those must not be able to diverge.
        if (!document.BlobName.StartsWith($"{tenant.TenantId}/", StringComparison.Ordinal))
        {
            return Results.NotFound();
        }
```

- [ ] **Step 7b: Assert Asura's query catalogue cannot cross tenants**

The spec calls Asura out specifically (§12.7): it generates read-only SQL, so an unscoped query there is a leak with an LLM in the loop. `tests/Utsav.IsolationTests/AsuraIsolationTests.cs`:

```csharp
using Utsav.Api.Asura;
using Utsav.Api.Endpoints;
using Xunit;

namespace Utsav.IsolationTests;

public class AsuraIsolationTests
{
    [Fact]
    public void Every_catalogued_query_is_tenant_scoped()
    {
        // Asura's value is that its queries are pre-written rather than
        // model-generated. That only holds if every one of them is scoped —
        // the assistant picks which to run, so an unscoped entry is reachable
        // by asking the right question.
        var unscoped = ToolCatalogue.All
            .Where(tool => TenancyGuard.ReferencedOrgScopedTables(tool.Sql).Count > 0)
            .Where(tool =>
            {
                try
                {
                    TenancyGuard.AssertScoped(tool.Sql, ["@org"]);
                    return false;
                }
                catch (TenancyViolationException)
                {
                    return true;
                }
            })
            .Select(tool => tool.Name)
            .ToList();

        Assert.Empty(unscoped);
    }
}
```

Adjust `ToolCatalogue.All`, `.Sql` and `.Name` to the actual member names in `ToolCatalogue.cs` — read the file first. If the catalogue is not exposed as an enumerable, make it one; a security property that cannot be enumerated cannot be tested.

- [ ] **Step 8: Run the whole isolation suite**

```bash
cd /home/atip/sourceCode/Utsav
export MSSQL_SA_PASSWORD='<dev password>'
docker compose -f docker-compose.dev.yml up -d
dotnet test tests/Utsav.IsolationTests
```

Expected: all pass, including `PooledConnectionTests` from Task 9 Step 7. **Any failure here blocks the rest of the plan** — a failing isolation test is a cross-committee data leak, not a flaky test. If a `DataRoutes` entry 404s because the route name differs, correct the route; do not remove it from the list.

- [ ] **Step 9: Wire both suites into CI**

In `.github/workflows/deploy.yml`, after the existing unit-test step:

```yaml
      - name: Start SQL Server for isolation tests
        run: docker compose -f docker-compose.dev.yml up -d --wait
        env:
          MSSQL_SA_PASSWORD: ${{ secrets.TEST_SQL_PASSWORD }}

      - name: Cross-tenant isolation tests
        run: dotnet test tests/Utsav.IsolationTests --no-build --verbosity normal
        env:
          MSSQL_SA_PASSWORD: ${{ secrets.TEST_SQL_PASSWORD }}
```

Also change the existing unit-test step's project path from `tests/Puja.UnitTests` to `tests/Utsav.UnitTests` and the deploy job's `AZURE_WEBAPP_NAME` to the new App Service name (left as a placeholder until Boss provides it — note it in the commit message rather than inventing a value).

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "test: cross-tenant isolation suite

Two committees seeded with deliberately identical-looking data — same
donor name, same zone name, different amounts — so a leak surfaces as a
wrong figure rather than an obviously foreign row.

Covers: every data GET route scoped and refusing an unpinned session;
all three reporting views (proving the year_id-keyed subqueries do not
merge committees); forged org_id in a create body ignored; update and
delete by another committee's id affecting nothing; document download
refused and blob prefixes matching their owner; and a pooled connection
reissued across tenants not retaining session context.

AZURE_WEBAPP_NAME in the deploy workflow is still a placeholder pending
the new App Service name."
```

---

**Day 1 gate.** Before starting Phase 2, all three must hold:

```bash
dotnet build Utsav.sln                 # zero warnings (warnings are errors)
dotnet test tests/Utsav.UnitTests      # all pass
dotnet test tests/Utsav.IsolationTests # all pass
```

---

## Phase 2 — Day 2: Product

### Task 11: Membership list and committee switching endpoints

**Files:**
- Modify: `src/Utsav.Api/Endpoints/AuthEndpoints.cs`
- Test: `tests/Utsav.IsolationTests/SwitchCommitteeTests.cs`

**Interfaces:**
- Consumes: `SessionService.SwitchOrgAsync` from Task 5.
- Produces: `POST /api/auth/switch-org` taking `{ "org_id": "<guid>" }`, returning the full `SessionPayload` on success and 403 on refusal.

- [ ] **Step 1: Write the failing test**

`tests/Utsav.IsolationTests/SwitchCommitteeTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Utsav.IsolationTests;

[Collection(nameof(DatabaseFixture))]
public class SwitchCommitteeTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Me_lists_every_committee_the_user_belongs_to()
    {
        using var client = fixture.ClientFor(fixture.TokenForA);

        var me = await (await client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<JsonElement>();

        var memberships = me.GetProperty("memberships").EnumerateArray().ToList();
        Assert.Equal(2, memberships.Count);
        Assert.Equal(fixture.TenantA.ToString(), me.GetProperty("org").GetProperty("id").GetGuid().ToString());
    }

    [Fact]
    public async Task Switching_changes_which_committees_data_is_returned()
    {
        using var client = fixture.ClientFor(fixture.TokenForA);

        var before = await (await client.GetAsync("/api/budget_lines")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.All(before.EnumerateArray(), r =>
            Assert.Equal(fixture.TenantA.ToString(), r.GetProperty("org_id").GetGuid().ToString()));

        var switched = await client.PostAsJsonAsync("/api/auth/switch-org", new { org_id = fixture.TenantB });
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);

        var after = await (await client.GetAsync("/api/budget_lines")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.All(after.EnumerateArray(), r =>
            Assert.Equal(fixture.TenantB.ToString(), r.GetProperty("org_id").GetGuid().ToString()));
    }

    [Fact]
    public async Task Switching_to_a_committee_the_user_does_not_belong_to_is_refused()
    {
        var outsider = Guid.NewGuid();
        using var client = fixture.ClientFor(fixture.TokenForA);

        var response = await client.PostAsJsonAsync("/api/auth/switch-org", new { org_id = outsider });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // And the session must still be pinned where it was.
        var me = await (await client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(fixture.TenantA.ToString(), me.GetProperty("org").GetProperty("id").GetGuid().ToString());
    }
}
```

- [ ] **Step 2: Run it and confirm it fails**

```bash
cd /home/atip/sourceCode/Utsav
dotnet test tests/Utsav.IsolationTests --filter FullyQualifiedName~SwitchCommitteeTests
```

Expected: the switch tests 404 — the route does not exist.

- [ ] **Step 3: Add the endpoint**

In `src/Utsav.Api/Endpoints/AuthEndpoints.cs`, register inside `MapAuthEndpoints`:

```csharp
        // Authenticated but deliberately NOT behind ActiveMember: a user with
        // several memberships and none selected has no active org yet, and
        // this is the call that gives them one.
        group.MapPost("/switch-org", SwitchOrgAsync).RequireAuthorization();
```

and the handler:

```csharp
    /// <summary>
    /// Pins the caller's session to one of their committees.
    ///
    /// Deliberately a server round trip rather than a client-side selection:
    /// the tenant every subsequent request runs under comes from the session
    /// row, so this is the only way to change it, and membership is
    /// re-verified here before anything moves.
    /// </summary>
    private static async Task<IResult> SwitchOrgAsync(
        [FromBody] SwitchOrgRequest? request,
        HttpContext http,
        SessionService sessions,
        CancellationToken ct)
    {
        if (request?.OrgId is null || request.OrgId == Guid.Empty)
        {
            return Results.Json(new { error = "org_id is required" }, statusCode: 400);
        }

        var token = http.User.FindFirst(LegacySessionAuthenticationDefaults.TokenClaimType)!.Value;
        var userId = Guid.Parse(http.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        if (!await sessions.SwitchOrgAsync(userId, request.OrgId.Value, token, ct))
        {
            // 403 not 404: telling an outsider whether a committee id exists
            // is itself a small leak.
            return Results.Json(new { error = "Not a member of that committee" }, statusCode: 403);
        }

        var session = await sessions.LoadSessionAsync(token, ct);
        return Results.Ok(session);
    }

    private sealed record SwitchOrgRequest(Guid? OrgId);
```

Add `using System.Security.Claims;`.

- [ ] **Step 4: Run the tests and confirm they pass**

```bash
dotnet test tests/Utsav.IsolationTests --filter FullyQualifiedName~SwitchCommitteeTests
```

Expected: 3 passing.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: committee switching endpoint

POST /api/auth/switch-org re-verifies membership server-side and
repins the session row. Not behind ActiveMember, because a user with
several memberships and none selected has no active org until they
call it. Refuses with 403 rather than 404 so an outsider cannot probe
which committee ids exist."
```

---

### Task 12: Committee chooser and switcher in the UI

**Files:**
- Modify: `src/Utsav.Web/src/types/db.ts`
- Modify: `src/Utsav.Web/src/context/AuthContext.tsx`
- Create: `src/Utsav.Web/src/pages/ChooseCommittee.tsx`
- Create: `src/Utsav.Web/src/components/layout/CommitteeSwitcher.tsx`
- Modify: `src/Utsav.Web/src/App.tsx:77-82`
- Modify: `src/Utsav.Web/src/components/layout/NavContent.tsx`

**Interfaces:**
- Consumes: `/api/auth/me` returning `memberships`, `POST /api/auth/switch-org`.
- Produces: `useAuth()` additionally exposes `memberships: Membership[]` and `switchCommittee(orgId: string): Promise<{ error: string | null }>`.

- [ ] **Step 1: Add the membership type**

In `src/Utsav.Web/src/types/db.ts`, alongside `Organization`:

```ts
/** One committee the signed-in user belongs to, as returned by /auth/me. */
export interface Membership {
  org_id: string;
  name: string;
  slug: string;
  logo_url: string | null;
  accent_colour: string | null;
  role: AppRole;
  status: AccessStatus;
}
```

and extend `Organization` with the two branding fields:

```ts
  logo_url: string | null;
  accent_colour: string | null;
```

- [ ] **Step 2: Thread memberships and switching through AuthContext**

In `src/Utsav.Web/src/context/AuthContext.tsx`:

```tsx
interface AuthContextValue {
  user: User | null;
  org: Organization | null;
  memberships: Membership[];
  role: AppRole | null;
  status: AccessStatus | null;
  can: (permission: Permission) => boolean;
  loading: boolean;
  serverDown: boolean;
  signIn: (email: string, password: string) => Promise<{ error: string | null }>;
  signUp: (email: string, password: string, fullName: string) => Promise<{ error: string | null }>;
  signInWithGoogle: (credential: string) => Promise<{ error: string | null }>;
  signInWithMicrosoft: (idToken: string) => Promise<{ error: string | null }>;
  switchCommittee: (orgId: string) => Promise<{ error: string | null }>;
  signOut: () => Promise<void>;
  refreshSession: () => Promise<void>;
}

interface SessionPayload {
  user: User;
  org: Organization | null;
  memberships: Membership[];
  role: AppRole | null;
  status: AccessStatus;
}
```

Add the state and wire it into `applySession`:

```tsx
  const [memberships, setMemberships] = useState<Membership[]>([]);

  function applySession(s: SessionPayload) {
    setUser(s.user);
    setOrg(s.org);
    setMemberships(s.memberships ?? []);
    setRole(s.role);
    setStatus(s.status);
  }
```

and add to `value`:

```tsx
    memberships,
    async switchCommittee(orgId: string) {
      try {
        applySession(await api.post<SessionPayload>("/auth/switch-org", { org_id: orgId }));
        // Every cached query belongs to the committee we just left. Clearing
        // rather than invalidating avoids a frame where one committee's
        // figures render under another's name.
        queryClient.clear();
        return { error: null };
      } catch (e) {
        return { error: e instanceof ApiError ? (e as Error).message : "Could not switch committee" };
      }
    },
```

Import `queryClient` from `@/lib/queryClient` and add `setMemberships([])` to `signOut`.

- [ ] **Step 3: Write the chooser page**

`src/Utsav.Web/src/pages/ChooseCommittee.tsx`:

```tsx
import { useState } from "react";
import { useAuth } from "@/context/AuthContext";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";

/**
 * Shown to a user who belongs to more than one committee and has not yet
 * picked which one they are working in. A single-membership user never sees
 * this — the session is pinned automatically at sign-in.
 */
export function ChooseCommittee() {
  const { memberships, switchCommittee, signOut } = useAuth();
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const active = memberships.filter((m) => m.status === "active");
  const pending = memberships.filter((m) => m.status === "pending");

  async function choose(orgId: string) {
    setBusy(orgId);
    setError(null);
    const { error } = await switchCommittee(orgId);
    if (error) {
      setError(error);
      setBusy(null);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center p-6 bg-muted/30">
      <Card className="w-full max-w-md p-6 space-y-4">
        <div>
          <h1 className="text-lg font-semibold">Choose a committee</h1>
          <p className="text-sm text-muted-foreground">
            You have access to more than one. You can switch at any time from the sidebar.
          </p>
        </div>

        {error && <p className="text-sm text-destructive">{error}</p>}

        <div className="space-y-2">
          {active.map((m) => (
            <button
              key={m.org_id}
              onClick={() => choose(m.org_id)}
              disabled={busy !== null}
              className="w-full flex items-center gap-3 rounded-md border p-3 text-left hover:bg-accent disabled:opacity-60"
            >
              {m.logo_url
                ? <img src={m.logo_url} alt="" className="h-8 w-8 rounded object-cover" />
                : <span
                    className="h-8 w-8 rounded grid place-items-center text-xs font-semibold text-white"
                    style={{ backgroundColor: m.accent_colour ?? "#6b7280" }}
                  >{m.name.slice(0, 1)}</span>}
              <span className="flex-1">
                <span className="block text-sm font-medium">{m.name}</span>
                <span className="block text-xs text-muted-foreground capitalize">{m.role}</span>
              </span>
              {busy === m.org_id && <span className="text-xs text-muted-foreground">Opening…</span>}
            </button>
          ))}
        </div>

        {pending.length > 0 && (
          <p className="text-xs text-muted-foreground">
            Awaiting approval at {pending.map((m) => m.name).join(", ")}.
          </p>
        )}

        <Button variant="ghost" className="w-full" onClick={signOut}>Sign out</Button>
      </Card>
    </div>
  );
}
```

If `@/components/ui/card` does not exist in the inherited component library, check with `ls src/Utsav.Web/src/components/ui/` and substitute the equivalent container the other pages use — do not add a new dependency.

- [ ] **Step 4: Write the switcher**

`src/Utsav.Web/src/components/layout/CommitteeSwitcher.tsx`:

```tsx
import { useState } from "react";
import { Check, ChevronsUpDown } from "lucide-react";
import { useAuth } from "@/context/AuthContext";
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

/**
 * Renders nothing for a single-committee user: a switcher with one entry is
 * noise, and most committees will only ever have the one.
 */
export function CommitteeSwitcher() {
  const { org, memberships, switchCommittee } = useAuth();
  const [busy, setBusy] = useState(false);
  const active = memberships.filter((m) => m.status === "active");

  if (!org || active.length < 2) return null;

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        disabled={busy}
        className="w-full flex items-center gap-2 rounded-md border px-2 py-1.5 text-left text-sm hover:bg-accent disabled:opacity-60"
      >
        <span className="flex-1 truncate font-medium">{org.name}</span>
        <ChevronsUpDown className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start" className="w-56">
        {active.map((m) => (
          <DropdownMenuItem
            key={m.org_id}
            onSelect={async () => {
              if (m.org_id === org.id) return;
              setBusy(true);
              await switchCommittee(m.org_id);
              setBusy(false);
            }}
          >
            <span className="flex-1 truncate">{m.name}</span>
            {m.org_id === org.id && <Check className="h-3.5 w-3.5" />}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
```

- [ ] **Step 5: Gate the app on committee selection**

In `src/Utsav.Web/src/App.tsx`, change the guard at lines 77–82:

```tsx
  const { loading, user, org, memberships, status, serverDown } = useAuth();
  // ... existing loading / serverDown / !user branches unchanged ...
  if (status !== "active") return <PendingApproval />;
  // Active, but no committee pinned: a multi-membership user who has not
  // chosen yet. Every data endpoint would 403 until they do.
  if (!org && memberships.some((m) => m.status === "active")) return <ChooseCommittee />;
```

Import `ChooseCommittee` alongside `PendingApproval`.

- [ ] **Step 6: Mount the switcher in the sidebar**

In `src/Utsav.Web/src/components/layout/NavContent.tsx`, render `<CommitteeSwitcher />` directly above the navigation list, and replace the hardcoded footer text (line 66, `Durga Puja Manager · by Athelite`) with:

```tsx
          <p className="text-[11px] text-muted-foreground">{org?.name ?? "Utsav"} · by Athelite</p>
```

- [ ] **Step 7: Type-check, build, and verify in the browser**

```bash
cd /home/atip/sourceCode/Utsav/src/Utsav.Web
npx tsc -b
npm run build
```

Then start the API and Vite, sign in as the two-committee test user, and confirm with the browser tools: the chooser appears, selecting a committee enters the app, the switcher shows both, and switching changes the figures on the Dashboard. Capture a screenshot of the switcher open as the evidence for this task.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: committee chooser and switcher

A multi-committee user picks one after sign-in and switches from the
sidebar; a single-committee user sees neither, since the session pins
automatically and a one-entry switcher is noise.

Switching clears the query cache rather than invalidating it — an
invalidate leaves a frame where the previous committee's figures render
under the new committee's name."
```

---

### Task 13: Platform administration capability

**Files:**
- Create: `src/Utsav.Domain/Authorization/PlatformCapability.cs`
- Modify: `src/Utsav.Infrastructure/Data/Entities/User.cs`
- Modify: `src/Utsav.Api/Authentication/LegacySessionAuthenticationHandler.cs`
- Modify: `src/Utsav.Api/Authentication/AuthorizationPolicies.cs`
- Modify: `src/Utsav.Api/Program.cs`
- Test: `tests/Utsav.UnitTests/Authorization/PlatformAdminTests.cs`

**Interfaces:**
- Consumes: `SessionPayload` from Task 5.
- Produces:
  - `User.IsPlatformAdmin` (bool, default false)
  - `AuthorizationPolicies.PlatformAdmin` (const string)
  - `LegacySessionAuthenticationDefaults.PlatformAdminClaimType`
  - `SessionPayload` gains `bool IsPlatformAdmin`

- [ ] **Step 1: Write the failing test**

`tests/Utsav.UnitTests/Authorization/PlatformAdminTests.cs`:

```csharp
using Utsav.Domain.Authorization;
using Xunit;

namespace Utsav.UnitTests.Authorization;

public class PlatformAdminTests
{
    [Fact]
    public void Platform_admin_is_not_an_AppRole_value()
    {
        // The whole point of a separate capability: a committee admin must
        // never be able to provision committees or reach another committee's
        // data. If "platform_admin" were a sixth AppRole, approving a
        // membership into it would hand that power to a treasurer.
        Assert.DoesNotContain("platform", AppRoleExtensions.AllWireValues);
        Assert.DoesNotContain("platform_admin", AppRoleExtensions.AllWireValues);
    }

    [Fact]
    public void Committee_admin_holds_no_platform_permission()
    {
        // Admin is the most powerful committee role. It must still not imply
        // any platform capability.
        Assert.All(
            Enum.GetValues<AppRole>(),
            role => Assert.False(PlatformCapability.IsImpliedBy(role)));
    }
}
```

- [ ] **Step 2: Run it and confirm it fails**

```bash
cd /home/atip/sourceCode/Utsav
dotnet test tests/Utsav.UnitTests --filter FullyQualifiedName~PlatformAdminTests
```

Expected: build failure — `PlatformCapability` does not exist.

- [ ] **Step 3: Write the capability**

`src/Utsav.Domain/Authorization/PlatformCapability.cs`:

```csharp
namespace Utsav.Domain.Authorization;

/// <summary>
/// The ability to operate the platform itself — provisioning committees and
/// naming their first administrator.
///
/// Deliberately NOT a sixth <see cref="AppRole"/>. AppRole is a member's role
/// <em>within</em> one committee, granted by that committee's own admin
/// through the membership approval flow. If platform administration lived
/// there, approving someone as "platform admin" at one committee would hand
/// them every other committee's books — so the two are different kinds of
/// authority and are modelled separately.
///
/// Holding this capability grants no implicit read access to any committee's
/// financial data: a platform admin still needs an ordinary membership to see
/// a committee's figures, and the tenancy layers apply to them unchanged.
/// </summary>
public static class PlatformCapability
{
    /// <summary>Always false. No committee role implies platform authority.</summary>
    public static bool IsImpliedBy(AppRole role) => false;
}
```

- [ ] **Step 4: Run the test and confirm it passes**

```bash
dotnet test tests/Utsav.UnitTests --filter FullyQualifiedName~PlatformAdminTests
```

Expected: 2 passing.

- [ ] **Step 5: Add the flag to the user entity and the session**

In `src/Utsav.Infrastructure/Data/Entities/User.cs`:

```csharp
    /// <summary>
    /// Platform operator. Grants committee provisioning only — never implicit
    /// access to any committee's data. See PlatformCapability.
    /// </summary>
    public bool IsPlatformAdmin { get; set; }
```

In `SessionPayload`, add `bool IsPlatformAdmin` as the final positional member, and populate it in all three `return new SessionPayload(...)` sites in `SessionService.LoadSessionAsync` from `row.User.IsPlatformAdmin`.

- [ ] **Step 6: Expose it as a claim and a policy**

In `LegacySessionAuthenticationDefaults`:

```csharp
    /// <summary>Present and "true" only for a platform operator.</summary>
    public const string PlatformAdminClaimType = "utsav:platform_admin";
```

In the handler's claim list:

```csharp
        if (session.IsPlatformAdmin)
        {
            claims.Add(new Claim(LegacySessionAuthenticationDefaults.PlatformAdminClaimType, "true"));
        }
```

In `AuthorizationPolicies`, add `public const string PlatformAdmin = "PlatformAdmin";` and in `Program.cs`:

```csharp
    .AddPolicy(AuthorizationPolicies.PlatformAdmin, policy => policy
        // Note: no status or role requirement. A platform operator need not be
        // a member of any committee, and being an admin of one grants nothing
        // here.
        .RequireClaim(LegacySessionAuthenticationDefaults.PlatformAdminClaimType, "true"))
```

- [ ] **Step 7: Generate the migration**

```bash
dotnet ef migrations add AddPlatformAdminFlag -p src/Utsav.Infrastructure -s src/Utsav.Api -o Data/Migrations
dotnet ef database update -p src/Utsav.Infrastructure -s src/Utsav.Api
```

- [ ] **Step 8: Run everything and commit**

```bash
dotnet test tests/Utsav.UnitTests
dotnet test tests/Utsav.IsolationTests
git add -A
git commit -m "feat: platform administration as a capability, not a role

A separate flag and policy rather than a sixth AppRole. AppRole is a
member's authority within one committee, granted by that committee's own
admin — if platform administration lived there, approving someone at one
committee would hand them every other committee's books.

The capability grants provisioning only. A platform admin still needs an
ordinary membership to read a committee's figures, and every tenancy
layer applies to them unchanged."
```

---

### Task 14: Tenant provisioning and per-festival seed templates

**Files:**
- Create: `src/Utsav.Infrastructure/Data/FestivalTemplates.cs`
- Create: `src/Utsav.Infrastructure/Data/TenantProvisioner.cs`
- Delete: `src/Utsav.Infrastructure/Data/CommitteeBootstrapper.cs`
- Create: `src/Utsav.Api/Endpoints/PlatformEndpoints.cs`
- Modify: `src/Utsav.Api/Program.cs` (drop the bootstrap call, map the new group)
- Test: `tests/Utsav.IsolationTests/ProvisioningTests.cs`

**Interfaces:**
- Consumes: `AuthorizationPolicies.PlatformAdmin`, `UtsavDbContext`.
- Produces:
  - `FestivalTemplates.For(string festival)` → `FestivalTemplate(string[] PujaDays, string[] Zones, (string Category, string[] Labels)[] Lookups, string YearNameFormat)`
  - `TenantProvisioner.ProvisionAsync(ProvisionRequest, CancellationToken)` → `Guid` (the new org id)
  - `ProvisionRequest(string Name, string? Slug, string? City, string Festival, int FirstYear, string FirstAdminEmail)`
  - `POST /api/platform/committees`, `GET /api/platform/committees`

- [ ] **Step 1: Write the failing test**

`tests/Utsav.IsolationTests/ProvisioningTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Utsav.IsolationTests;

[Collection(nameof(DatabaseFixture))]
public class ProvisioningTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task A_committee_admin_cannot_provision_a_committee()
    {
        // TokenForA belongs to an admin of Durga. Committee admin is the most
        // powerful committee role and must still be refused here.
        using var committeeAdmin = fixture.ClientFor(fixture.TokenForA);

        var response = await committeeAdmin.PostAsJsonAsync("/api/platform/committees", new
        {
            name = "Should Not Exist",
            festival = "kali",
            first_year = 2026,
            first_admin_email = "x@test.local",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Provisioning_creates_an_isolated_committee_with_its_own_festival_seed()
    {
        using var platform = fixture.ClientFor(fixture.TokenForPlatformAdmin);

        var response = await platform.PostAsJsonAsync("/api/platform/committees", new
        {
            name = "Barrackpore Kali Puja",
            festival = "kali",
            first_year = 2026,
            first_admin_email = "kali.admin@test.local",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newOrgId = created.GetProperty("id").GetGuid();

        // Not the old fixed single-tenant id, and not either seeded tenant.
        Assert.NotEqual(Guid.Parse("00000000-0000-0000-0000-000000000001"), newOrgId);
        Assert.NotEqual(fixture.TenantA, newOrgId);
        Assert.NotEqual(fixture.TenantB, newOrgId);

        await using var db = fixture.ContextFor(newOrgId);

        // Kali Puja's days are not Mahalaya..Dasami. Seeding Durga's calendar
        // into a Kali committee would be visibly wrong on the Schedule page.
        var days = await db.Lookups.Where(l => l.Category == "puja_day").Select(l => l.Label).ToListAsync();
        Assert.Contains("Kali Puja", days);
        Assert.DoesNotContain("Mahalaya", days);

        Assert.NotEmpty(await db.Zones.ToListAsync());
        Assert.Single(await db.PujaYears.ToListAsync());
        Assert.Single(await db.OrgMembers.IgnoreQueryFilters().Where(m => m.OrgId == newOrgId).ToListAsync());
    }

    [Fact]
    public async Task Provisioning_does_not_touch_an_existing_committees_data()
    {
        await using var before = fixture.ContextFor(fixture.TenantA);
        var donorsBefore = await before.Donors.CountAsync();
        var lookupsBefore = await before.Lookups.CountAsync();

        using var platform = fixture.ClientFor(fixture.TokenForPlatformAdmin);
        await platform.PostAsJsonAsync("/api/platform/committees", new
        {
            name = "Another Committee",
            festival = "durga",
            first_year = 2026,
            first_admin_email = "another@test.local",
        });

        await using var after = fixture.ContextFor(fixture.TenantA);
        Assert.Equal(donorsBefore, await after.Donors.CountAsync());
        Assert.Equal(lookupsBefore, await after.Lookups.CountAsync());
    }

    [Fact]
    public async Task A_duplicate_slug_is_rejected_rather_than_silently_suffixed()
    {
        using var platform = fixture.ClientFor(fixture.TokenForPlatformAdmin);

        var first = await platform.PostAsJsonAsync("/api/platform/committees", new
        {
            name = "Slug Clash", slug = "clash", festival = "durga",
            first_year = 2026, first_admin_email = "a@test.local",
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await platform.PostAsJsonAsync("/api/platform/committees", new
        {
            name = "Slug Clash Again", slug = "clash", festival = "durga",
            first_year = 2026, first_admin_email = "b@test.local",
        });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }
}
```

Extend `DatabaseFixture` with a platform admin:

```csharp
    public Guid PlatformAdminUser { get; } = Guid.NewGuid();
    public string TokenForPlatformAdmin { get; } = "pppp" + Guid.NewGuid().ToString("N");
```

and in `SeedAsync`:

```csharp
        // Deliberately a member of NO committee: provisioning must not require
        // membership anywhere, and this proves the capability stands alone.
        db.Users.Add(new User
        {
            Id = PlatformAdminUser, Email = "platform@test.local",
            AuthProvider = "local", PasswordHash = "x", IsPlatformAdmin = true,
        });
        db.Sessions.Add(new Session { Token = TokenForPlatformAdmin, UserId = PlatformAdminUser, OrgId = null });
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
cd /home/atip/sourceCode/Utsav
dotnet test tests/Utsav.IsolationTests --filter FullyQualifiedName~ProvisioningTests
```

Expected: 404 on the provisioning route.

- [ ] **Step 3: Write the festival templates**

`src/Utsav.Infrastructure/Data/FestivalTemplates.cs`:

```csharp
namespace Utsav.Infrastructure.Data;

/// <summary>
/// What a brand-new committee's reference data starts as.
///
/// All of it is <c>lookups</c> and <c>zones</c> rows rather than code, which
/// is the single luckiest fact about making this app multi-festival: Kali
/// Puja's calendar differs completely from Durga Puja's, and accommodating it
/// is seed data, not a domain change.
///
/// Expense categories are shared across festivals because committees spend on
/// broadly the same things; the festival-specific part is the day list and a
/// handful of ritual categories. Admins rename and extend all of it from
/// Settings afterwards, so these are starting points, not constraints.
/// </summary>
public sealed record FestivalTemplate(
    string DisplayName,
    string[] PujaDays,
    string[] Zones,
    (string Category, string[] Labels)[] Lookups);

public static class FestivalTemplates
{
    private static readonly (string Category, string[] Labels)[] CommonLookups =
    [
        ("payment_mode", ["Cash", "Online - Gpay", "Cheque", "Bank Transfer", "Pending"]),
        ("ad_type", ["Banner", "Leaflet", "Banner + Leaflet", "Front Page Leaflet", "Others"]),
        ("ad_status", [
            "Write-up received", "Banner received", "Banner received, Writeup Pending",
            "Write-up received, Banner Pending", "Pending All", "NA",
        ]),
        ("offering_type", ["Vog", "Saree", "Dhuti", "Flower", "Others"]),
        ("donation_purpose", ["Idol", "Feast", "Vog", "General", "Others"]),
        ("vendor_category", [
            "Pandal", "Decoration", "Light & Sound", "Idol", "Catering",
            "Dhak & Music", "Printing", "Transport", "Security", "Others",
        ]),
        ("expense_category", [
            "Siddhi", "IDOL", "Purohit", "Puja Essentials & Offerings", "Decoration",
            "Electricity", "Light & Sound with Vasan", "Cleaning Stuffs", "Transportation",
            "Bisarjan & Feast", "Pandal", "Management & Stationary", "Dhak",
            "Cultural Program", "Advertisment", "Flowers", "New Assets",
            "Khuti Puja", "Pritivoj", "Others",
            "Catering", "Finance", "Idol & Puja", "Logistics", "Permissions & Legal", "Publicity",
        ]),
    ];

    private static readonly string[] DefaultZones = ["16 Feet", "20 Feet", "14 Feet +"];

    public static FestivalTemplate For(string festival) => festival.Trim().ToLowerInvariant() switch
    {
        "kali" => new FestivalTemplate(
            "Kali Puja",
            // Kali Puja runs on a far shorter calendar than Durga Puja: the
            // amavasya night itself, with the preparation and immersion days
            // either side.
            ["Chaturdashi", "Kali Puja", "Dipanwita", "Bhai Phota", "Bisarjan"],
            DefaultZones,
            CommonLookups),

        "durga" => new FestivalTemplate(
            "Durga Puja",
            ["Mahalaya", "Panchami", "Sasthi", "Saptami", "Astami", "Sandhi Puja", "Nabami", "Dasami"],
            DefaultZones,
            CommonLookups),

        "saraswati" => new FestivalTemplate(
            "Saraswati Puja",
            ["Panchami", "Sasthi", "Bisarjan"],
            DefaultZones,
            CommonLookups),

        "lakshmi" => new FestivalTemplate(
            "Lakshmi Puja",
            ["Purnima", "Bisarjan"],
            DefaultZones,
            CommonLookups),

        // An unrecognised festival gets a generic calendar rather than being
        // refused: a committee with a festival we have not met should still be
        // able to start, and the day list is editable in Settings.
        _ => new FestivalTemplate("Puja", ["Puja Day", "Bisarjan"], DefaultZones, CommonLookups),
    };

    public static IReadOnlyList<string> Known => ["durga", "kali", "saraswati", "lakshmi"];
}
```

- [ ] **Step 4: Write the provisioner**

`src/Utsav.Infrastructure/Data/TenantProvisioner.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Data.Entities;

namespace Utsav.Infrastructure.Data;

public sealed record ProvisionRequest(
    string Name, string? Slug, string? City, string Festival, int FirstYear, string FirstAdminEmail);

public sealed class SlugTakenException(string slug)
    : InvalidOperationException($"A committee with the slug '{slug}' already exists.");

/// <summary>
/// Creates a committee and everything it needs to be usable on day one.
///
/// Replaces CommitteeBootstrapper, which assumed one committee per database
/// and reused a fixed organization id. Here the id is generated, so there is
/// no single-tenant identity to collide with and no ceremony around "the"
/// committee.
///
/// Runs in one transaction: a committee that exists with no reference data or
/// no administrator is worse than one that was never created, because someone
/// has to work out what is missing.
/// </summary>
public sealed class TenantProvisioner(UtsavDbContext db)
{
    public async Task<Guid> ProvisionAsync(ProvisionRequest request, CancellationToken ct = default)
    {
        var slug = string.IsNullOrWhiteSpace(request.Slug) ? Slugify(request.Name) : Slugify(request.Slug);

        // Rejected rather than suffixed: a slug is how a committee is
        // identified in URLs and support conversations, so silently handing
        // back "clash-2" invites someone to use the wrong one.
        if (await db.Organizations.AnyAsync(o => o.Slug == slug, ct))
        {
            throw new SlugTakenException(slug);
        }

        var template = FestivalTemplates.For(request.Festival);
        var orgId = Guid.NewGuid();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        db.Organizations.Add(new Organization
        {
            Id = orgId,
            Name = request.Name,
            Slug = slug,
            City = request.City,
            IsActive = true,
        });

        db.Zones.AddRange(template.Zones.Select((zone, index) => new Zone
        {
            Id = Guid.NewGuid(), OrgId = orgId, Name = zone, SortOrder = index + 1,
        }));

        var lookups = template.Lookups
            .Append(("puja_day", template.PujaDays))
            .ToArray();

        db.Lookups.AddRange(lookups.SelectMany(group =>
            group.Labels.Select((label, index) => new Lookup
            {
                Id = Guid.NewGuid(), OrgId = orgId, Category = group.Category,
                Label = label, SortOrder = index + 1, Active = true,
            })));

        db.PujaYears.Add(new PujaYear
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            Year = request.FirstYear,
            Name = $"{template.DisplayName} {request.FirstYear}",
            Target = 0,
            ProjectionPct = 6.50m,
            IsActive = true,
        });

        // No collectors seeded: those are a specific committee's volunteers,
        // exactly the kind of thing a new committee must not inherit.

        // The first admin, by email. An existing user is reused so someone
        // already on another committee does not end up with two accounts —
        // which is the whole point of global identity.
        var email = request.FirstAdminEmail.Trim();
        var admin = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (admin is null)
        {
            admin = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                AuthProvider = "pending",
                // No usable password: they sign up or use Google/Microsoft
                // with this address and land straight into an active
                // membership. An empty hash can never verify.
                PasswordHash = "",
            };
            db.Users.Add(admin);
        }

        db.OrgMembers.Add(new OrgMember
        {
            Id = Guid.NewGuid(), OrgId = orgId, UserId = admin.Id,
            Role = "admin", Status = "active",
        });

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return orgId;
    }

    private static string Slugify(string value) =>
        new string(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray())
            .Trim('-')
            .Replace("--", "-");
}
```

- [ ] **Step 5: Write the platform endpoints**

`src/Utsav.Api/Endpoints/PlatformEndpoints.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Utsav.Api.Authentication;
using Utsav.Infrastructure.Data;

namespace Utsav.Api.Endpoints;

/// <summary>
/// Operating the platform, as distinct from operating a committee. Behind
/// <see cref="AuthorizationPolicies.PlatformAdmin"/>, which no committee role
/// can satisfy.
/// </summary>
public static class PlatformEndpoints
{
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        var platform = app.MapGroup("/api/platform").RequireAuthorization(AuthorizationPolicies.PlatformAdmin);

        platform.MapGet("/committees", async (UtsavDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Organizations
                .OrderBy(o => o.Name)
                .Select(o => new
                {
                    o.Id, o.Name, o.Slug, o.City, o.IsActive, o.CreatedAt,
                    members = db.OrgMembers.Count(m => m.OrgId == o.Id && m.Status == "active"),
                })
                .ToListAsync(ct)));

        platform.MapPost("/committees", CreateAsync);
        platform.MapGet("/festivals", () => Results.Ok(FestivalTemplates.Known));

        return app;
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateCommitteeRequest? request, TenantProvisioner provisioner, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.Name))
        {
            return Results.Json(new { error = "name is required" }, statusCode: 400);
        }
        if (string.IsNullOrWhiteSpace(request.FirstAdminEmail))
        {
            return Results.Json(new { error = "first_admin_email is required" }, statusCode: 400);
        }

        var year = request.FirstYear is > 2000 and < 2100 ? request.FirstYear.Value : DateTime.UtcNow.Year;

        try
        {
            var id = await provisioner.ProvisionAsync(new ProvisionRequest(
                request.Name.Trim(), request.Slug, request.City,
                request.Festival ?? "durga", year, request.FirstAdminEmail), ct);

            return Results.Ok(new { id });
        }
        catch (SlugTakenException e)
        {
            return Results.Json(new { error = e.Message }, statusCode: 409);
        }
    }

    private sealed record CreateCommitteeRequest(
        string? Name, string? Slug, string? City, string? Festival, int? FirstYear, string? FirstAdminEmail);
}
```

- [ ] **Step 6: Remove the single-tenant bootstrap from startup**

In `src/Utsav.Api/Program.cs`, delete the `CommitteeBootstrapper` registration and the whole `try { … EnsureSeededAsync() … }` block (the one whose comment reads "A brand-new database gets its organization and reference data here"). A pooled platform must not invent a committee at startup — committees are provisioned deliberately.

Register the provisioner and map the group:

```csharp
builder.Services.AddScoped<TenantProvisioner>();
```

```csharp
app.MapPlatformEndpoints();
```

Then delete `src/Utsav.Infrastructure/Data/CommitteeBootstrapper.cs`.

- [ ] **Step 7: Seed the first platform admin**

Provisioning requires a platform admin, and a fresh database has none. Add a one-time startup promotion driven by configuration, which does nothing unless the setting is present:

```csharp
// Promotes exactly one account to platform operator, by email, when
// Platform:BootstrapAdminEmail is configured. Idempotent, and a no-op once
// any platform admin exists — so leaving the setting in place cannot
// silently re-grant access that was deliberately revoked.
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<UtsavDbContext>();
    var email = builder.Configuration["Platform:BootstrapAdminEmail"];
    if (!string.IsNullOrWhiteSpace(email) && !await db.Users.AnyAsync(u => u.IsPlatformAdmin))
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is not null)
        {
            user.IsPlatformAdmin = true;
            await db.SaveChangesAsync();
            app.Logger.LogInformation("Promoted {Email} to platform administrator", email);
        }
        else
        {
            app.Logger.LogWarning(
                "Platform:BootstrapAdminEmail is set to {Email} but no such account exists yet. " +
                "Sign up with that address, then restart.", email);
        }
    }
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Platform admin bootstrap failed — continuing startup.");
}
```

- [ ] **Step 8: Run the tests and commit**

```bash
dotnet test tests/Utsav.IsolationTests --filter FullyQualifiedName~ProvisioningTests
dotnet test tests/Utsav.UnitTests
dotnet test tests/Utsav.IsolationTests
git add -A
git commit -m "feat: tenant provisioning with per-festival seed templates

Replaces CommitteeBootstrapper, which assumed one committee per
database and reused a fixed organization id. Provisioning now generates
an id, runs in one transaction (a committee with no reference data or no
admin is worse than none), and refuses a duplicate slug rather than
suffixing it.

Kali Puja's calendar is seeded from its own template — Chaturdashi,
Kali Puja, Dipanwita, Bhai Phota, Bisarjan — not Durga's Mahalaya to
Dasami. All of this is lookups rows rather than code, which is why
supporting a second festival is seed data and not a domain change.

Startup no longer invents a committee: a pooled platform must not
conjure a tenant, so EnsureSeededAsync is gone."
```

---

### Task 15: Per-tenant branding

**Files:**
- Create: `src/Utsav.Infrastructure/Data/Migrations/<stamp>_AddBranding.cs`
- Create: `src/Utsav.Web/src/components/brand/TenantTheme.tsx`
- Modify: `src/Utsav.Web/src/App.tsx`
- Modify: `src/Utsav.Api/Endpoints/AdminEndpoints.cs` (branding in the rename handler)
- Test: `tests/Utsav.UnitTests/Branding/AccentColourTests.cs`

**Interfaces:**
- Consumes: `Organization.LogoUrl`, `Organization.AccentColour` (properties added in Task 5 Step 4).
- Produces: `TenantTheme` component; `PATCH /api/organizations/current` accepting `name`, `logo_url`, `accent_colour`.

- [ ] **Step 1: Write the failing test for colour validation**

`tests/Utsav.UnitTests/Branding/AccentColourTests.cs`:

```csharp
using Utsav.Api.Endpoints;
using Xunit;

namespace Utsav.UnitTests.Branding;

public class AccentColourTests
{
    [Theory]
    [InlineData("#ff0000")]
    [InlineData("#FFF")]
    [InlineData("#1a2b3c")]
    public void Valid_hex_colours_are_accepted(string value)
    {
        Assert.True(BrandingValidation.IsValidAccentColour(value));
    }

    [Theory]
    [InlineData("red")]
    [InlineData("rgb(1,2,3)")]
    [InlineData("#ff00")]
    [InlineData("#12345g")]
    // The one that matters: the value is interpolated into a CSS custom
    // property, so anything that can close the declaration and add another
    // is a stored-CSS-injection vector.
    [InlineData("#fff;} body{display:none")]
    [InlineData("url(javascript:alert(1))")]
    public void Anything_that_is_not_a_plain_hex_colour_is_rejected(string value)
    {
        Assert.False(BrandingValidation.IsValidAccentColour(value));
    }

    [Fact]
    public void Null_is_allowed_and_means_use_the_default_theme()
    {
        Assert.True(BrandingValidation.IsValidAccentColour(null));
    }
}
```

- [ ] **Step 2: Run it and confirm it fails**

```bash
cd /home/atip/sourceCode/Utsav
dotnet test tests/Utsav.UnitTests --filter FullyQualifiedName~AccentColourTests
```

Expected: build failure — `BrandingValidation` does not exist.

- [ ] **Step 3: Write the validation**

`src/Utsav.Api/Endpoints/BrandingValidation.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Utsav.Api.Endpoints;

/// <summary>
/// An accent colour is written into a CSS custom property on the page, so it
/// is untrusted input reaching a stylesheet. Validated as a strict hex literal
/// — not sanitised, not escaped — because the set of legitimate values is
/// tiny and an allowlist has no bypass to find.
/// </summary>
public static class BrandingValidation
{
    private static readonly Regex HexColour = new(
        @"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.Compiled);

    /// <summary>Null means "no brand colour set" and falls back to the default theme.</summary>
    public static bool IsValidAccentColour(string? value) =>
        value is null || HexColour.IsMatch(value);
}
```

- [ ] **Step 4: Run the test and confirm it passes**

```bash
dotnet test tests/Utsav.UnitTests --filter FullyQualifiedName~AccentColourTests
```

Expected: 10 passing.

- [ ] **Step 5: Generate the branding migration**

The two properties already exist on the entity from Task 5. Generate and apply:

```bash
dotnet ef migrations add AddBranding -p src/Utsav.Infrastructure -s src/Utsav.Api -o Data/Migrations
dotnet ef database update -p src/Utsav.Infrastructure -s src/Utsav.Api
```

- [ ] **Step 6: Accept branding on the rename endpoint**

In `src/Utsav.Api/Endpoints/AdminEndpoints.cs`, extend `RenameCommitteeAsync` to also take the two branding fields, rejecting an invalid colour:

```csharp
        if (TryString(body, "accent_colour", out var colour))
        {
            if (!BrandingValidation.IsValidAccentColour(colour))
            {
                return Results.Json(
                    new { error = "accent_colour must be a hex colour such as #c2410c" }, statusCode: 400);
            }

            await DynamicSql.ExecuteAsync(db,
                "UPDATE organizations SET accent_colour=@colour WHERE id=@org",
                [P("@colour", (object?)colour ?? DBNull.Value), Org(tenant)], ct);
        }
```

Note `organizations` is not in `TenancyGuard.OrgScopedTables` (it carries `id`, not `org_id`), so the guard will not demand an `org_id` predicate — but the `WHERE id=@org` constraint is still required so a committee can only rename itself.

- [ ] **Step 7: Write the theme component**

`src/Utsav.Web/src/components/brand/TenantTheme.tsx`:

```tsx
import { useEffect } from "react";
import { useAuth } from "@/context/AuthContext";

/**
 * Applies the active committee's brand colour by setting the CSS custom
 * properties the Tailwind theme already reads, so branding is a data change
 * rather than a per-tenant build.
 *
 * Writes via style.setProperty rather than injecting a <style> block: the
 * value is committee-supplied, and setProperty cannot escape into a new
 * declaration even if validation upstream were ever bypassed.
 */
export function TenantTheme() {
  const { org } = useAuth();

  useEffect(() => {
    const root = document.documentElement;
    const accent = org?.accent_colour;

    if (!accent) {
      root.style.removeProperty("--brand-accent");
      return;
    }

    root.style.setProperty("--brand-accent", accent);
    return () => root.style.removeProperty("--brand-accent");
  }, [org?.accent_colour]);

  useEffect(() => {
    // The installed PWA and the browser tab should name the committee, not
    // the product — a member has one committee's app open, not "Utsav".
    document.title = org?.name ? `${org.name} — Utsav` : "Utsav";
  }, [org?.name]);

  return null;
}
```

- [ ] **Step 8: Mount it and consume the variable**

In `src/Utsav.Web/src/App.tsx`, render `<TenantTheme />` inside the authenticated branch, above `<Routes>`.

In `src/Utsav.Web/src/index.css` (or wherever the Tailwind theme variables are declared), add the fallback so an unbranded committee is unchanged from today:

```css
  :root {
    /* Falls back to the existing primary, so a committee with no accent
       colour set looks exactly as the app did before branding existed. */
    --brand-accent: hsl(var(--primary));
  }
```

Then use `var(--brand-accent)` for the sidebar active state and the logo placeholder background only. **Do not** retheme every component — the UI must stay recognisable, and a committee-chosen colour applied to destructive buttons or chart series would break contrast guarantees the palette currently holds.

- [ ] **Step 9: Verify in the browser and commit**

Set an accent colour for one of the two test committees, switch between them, and confirm the sidebar accent and tab title change while nothing else shifts. Capture a before/after screenshot.

```bash
cd /home/atip/sourceCode/Utsav
npx --prefix src/Utsav.Web tsc -b
dotnet test tests/Utsav.UnitTests
git add -A
git commit -m "feat: per-tenant branding (name, logo, accent colour)

Accent colour is validated as a strict hex literal and applied with
style.setProperty on the documentElement — it is committee-supplied
input reaching a stylesheet, so it gets an allowlist rather than
sanitising, and a write path that cannot escape into a new declaration.

Scoped deliberately to the sidebar accent, logo placeholder and tab
title. Retheming every component with a committee-chosen colour would
break the contrast the current palette guarantees on destructive
actions and chart series."
```

---

### Task 16: Committee letterhead on PDF and Excel exports

**Files:**
- Modify: `src/Utsav.Api/Reports/AuditReportPdf.cs`
- Modify: `src/Utsav.Api/Reports/FeastTokenSheetPdf.cs`
- Modify: `src/Utsav.Api/Endpoints/AuditReportEndpoint.cs`
- Create: `src/Utsav.Api/Reports/ReportHeader.cs`
- Test: `tests/Utsav.UnitTests/Reports/ReportHeaderTests.cs`

**Interfaces:**
- Consumes: `Organization.Name`, `Organization.City`, `Organization.LogoUrl`.
- Produces: `ReportHeader(string CommitteeName, string? City, string? LogoUrl, string Title, string Subtitle)` and `ReportHeader.ForAsync(UtsavDbContext, ITenantContext, string title, string subtitle, CancellationToken)`.

- [ ] **Step 1: Write the failing test**

`tests/Utsav.UnitTests/Reports/ReportHeaderTests.cs`:

```csharp
using Utsav.Api.Reports;
using Xunit;

namespace Utsav.UnitTests.Reports;

public class ReportHeaderTests
{
    [Fact]
    public void Header_line_names_the_committee_not_the_product()
    {
        // These documents go to auditors and donors. "Utsav" means nothing to
        // them; the committee's own name is the whole point.
        var header = new ReportHeader("Barrackpore Kali Puja Committee", "Barrackpore", null,
            "Audit Report", "2026");

        Assert.Equal("Barrackpore Kali Puja Committee", header.CommitteeName);
        Assert.DoesNotContain("Utsav", header.Caption, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Durga", header.Caption, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Caption_includes_the_city_when_there_is_one()
    {
        var header = new ReportHeader("A Committee", "Barrackpore", null, "Audit Report", "2026");
        Assert.Contains("Barrackpore", header.Caption);
    }

    [Fact]
    public void Caption_omits_the_city_separator_when_there_is_none()
    {
        var header = new ReportHeader("A Committee", null, null, "Audit Report", "2026");
        Assert.DoesNotContain("·", header.Caption);
        Assert.DoesNotContain(",", header.Caption);
    }
}
```

- [ ] **Step 2: Run it and confirm it fails**

```bash
dotnet test tests/Utsav.UnitTests --filter FullyQualifiedName~ReportHeaderTests
```

Expected: build failure — `ReportHeader` does not exist.

- [ ] **Step 3: Write the header type**

`src/Utsav.Api/Reports/ReportHeader.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Utsav.Domain.Tenancy;
using Utsav.Infrastructure.Data;

namespace Utsav.Api.Reports;

/// <summary>
/// The letterhead every generated document carries.
///
/// This is the branding that matters most: a PDF is what a committee hands to
/// its auditors and its donors, and it must read as that committee's
/// document, not as output from a product they happen to use.
/// </summary>
public sealed record ReportHeader(
    string CommitteeName, string? City, string? LogoUrl, string Title, string Subtitle)
{
    /// <summary>The small line under the committee name. No product branding, by design.</summary>
    public string Caption => string.IsNullOrWhiteSpace(City) ? Subtitle : $"{City} · {Subtitle}";

    public static async Task<ReportHeader> ForAsync(
        UtsavDbContext db, ITenantContext tenant, string title, string subtitle, CancellationToken ct = default)
    {
        var org = await db.Organizations
            .Where(o => o.Id == tenant.TenantId)
            .Select(o => new { o.Name, o.City, o.LogoUrl })
            .SingleAsync(ct);

        return new ReportHeader(org.Name, org.City, org.LogoUrl, title, subtitle);
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

```bash
dotnet test tests/Utsav.UnitTests --filter FullyQualifiedName~ReportHeaderTests
```

Expected: 3 passing.

- [ ] **Step 5: Use it in the PDF reports**

In `src/Utsav.Api/Reports/AuditReportPdf.cs`, replace the hardcoded committee heading with the header's values. Find the existing title composition (it currently draws the committee name from the org row or a literal) and route it through `ReportHeader`:

```csharp
            page.Header().Row(row =>
            {
                row.ConstantItem(48).Height(48).Element(logo =>
                {
                    // A committee with no logo gets its initial in the accent
                    // colour rather than an empty box — a letterhead with a
                    // hole in it looks broken rather than unbranded.
                    if (header.LogoUrl is { Length: > 0 } url)
                    {
                        logo.Image(LogoBytes(url)).FitArea();
                    }
                    else
                    {
                        logo.AlignCenter().AlignMiddle()
                            .Text(header.CommitteeName[..1])
                            .FontSize(24).SemiBold();
                    }
                });

                row.RelativeItem().PaddingLeft(10).Column(column =>
                {
                    column.Item().Text(header.CommitteeName).FontSize(16).SemiBold();
                    column.Item().Text(header.Caption).FontSize(9).FontColor(Colors.Grey.Darken1);
                    column.Item().PaddingTop(4).Text(header.Title).FontSize(12).SemiBold();
                });
            });
```

Apply the same header to `FeastTokenSheetPdf.cs`. For the Excel export in `AuditReportEndpoint.cs`, write `header.CommitteeName` and `header.Caption` into the first two rows of the sheet before the column headings.

If fetching logo bytes requires a blob round trip, load it once per document via the existing `IBlobStorageService` and pass the bytes in — do not fetch inside the QuestPDF layout callback, which runs during rendering.

- [ ] **Step 6: Verify a rendered PDF and commit**

Generate an audit report for each test committee and confirm each carries its own name and city. Open both PDFs to check the Bengali font still renders (the embedded fonts are unchanged, but the header is new layout).

```bash
dotnet test tests/Utsav.UnitTests
git add -A
git commit -m "feat: committee letterhead on PDF and Excel exports

The branding that matters most — these documents go to auditors and
donors, so they must read as the committee's own, with no product name
anywhere on them. A committee with no logo gets its initial rather than
an empty box, because a letterhead with a hole in it reads as broken
rather than unbranded."
```

---

### Task 17: Remove the inherited Durga Puja hardcoding

**Files:**
- Modify: `src/Utsav.Web/index.html`
- Modify: `src/Utsav.Web/src/pages/Login.tsx:72`
- Modify: `src/Utsav.Web/src/pages/Settings.tsx:72,81`
- Modify: `src/Utsav.Api/Endpoints/AdminEndpoints.cs:158`
- Modify: `src/Utsav.Api/Endpoints/AuditReportEndpoint.cs:296`
- Modify: `src/Utsav.Api/Asura/AsuraOrchestrator.cs:229,293`
- Modify: `src/Utsav.Web/package.json`
- Test: `tests/Utsav.UnitTests/Branding/NoFestivalHardcodingTests.cs`

**Interfaces:**
- Consumes: `ReportHeader`, `FestivalTemplates`, `useAuth().org`.
- Produces: no new interfaces; a guard test preventing regression.

- [ ] **Step 1: Write the guard test**

`tests/Utsav.UnitTests/Branding/NoFestivalHardcodingTests.cs`:

```csharp
using Xunit;

namespace Utsav.UnitTests.Branding;

public class NoFestivalHardcodingTests
{
    /// <summary>
    /// Walks up from the test binary to the repository root, so this works
    /// from both `dotnet test` and an IDE runner.
    /// </summary>
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Utsav.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Utsav.sln not found above the test binary.");
    }

    [Fact]
    public void No_source_file_hardcodes_a_single_festival_in_user_facing_text()
    {
        // FestivalTemplates is the one legitimate home for festival names —
        // it is seed data keyed by festival, which is the opposite of
        // hardcoding. Everything else must derive the name from the tenant.
        var root = RepoRoot();
        var offenders = new List<string>();

        var searchable = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".cs") || p.EndsWith(".tsx") || p.EndsWith(".ts") || p.EndsWith(".html"))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !p.Contains("Migrations")
                        && !p.EndsWith("FestivalTemplates.cs"));

        foreach (var path in searchable)
        {
            var text = File.ReadAllText(path);
            if (text.Contains("Durga Puja", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetRelativePath(root, path));
            }
        }

        Assert.Empty(offenders);
    }
}
```

- [ ] **Step 2: Run it and confirm it fails with the inherited hits**

```bash
cd /home/atip/sourceCode/Utsav
dotnet test tests/Utsav.UnitTests --filter FullyQualifiedName~NoFestivalHardcodingTests
```

Expected: FAIL listing roughly 8 files. That list is this task's work queue.

- [ ] **Step 3: Fix the backend occurrences**

`AdminEndpoints.cs:158` — the rollover year-name default. Derive it from the committee's existing year names rather than a festival literal:

```csharp
        // Carries the committee's own naming forward: "Kali Puja 2026" rolls
        // into "Kali Puja 2027". Falls back to the bare year rather than
        // guessing a festival.
        var previousName = await db.PujaYears
            .Where(y => y.OrgId == tenant.TenantId)
            .OrderByDescending(y => y.Year)
            .Select(y => y.Name)
            .FirstOrDefaultAsync(ct);

        var prefix = previousName is { Length: > 0 }
            ? System.Text.RegularExpressions.Regex.Replace(previousName, @"\s*\d{4}\s*$", "").Trim()
            : "";

        var name = TryString(body, "name", out var n) && n is { Length: > 0 }
            ? n
            : (prefix is { Length: > 0 } ? $"{prefix} {year}" : year.ToString());
```

`AuditReportEndpoint.cs:296` — replace `$"Durga Puja {yearNum}"` with `yearNum.ToString()`, since the committee name now comes from the letterhead added in Task 16.

`AsuraOrchestrator.cs:229,293` — the two prompt strings. Replace "a Durga Puja committee" with "a puja committee" in both. The assistant's domain knowledge does not depend on the festival, and naming the wrong one in the system prompt would make it reason about the wrong calendar.

- [ ] **Step 4: Fix the frontend occurrences**

`index.html` — title and description:

```html
    <meta name="description" content="Puja committee finance & collection management" />
    <title>Utsav</title>
```

The per-committee title is set at runtime by `TenantTheme` from Task 15, so the static tag only needs to be festival-neutral.

`Login.tsx:72` — the subtitle. The login screen has no tenant yet, so it must be generic:

```tsx
                : "Collections, expenses and budget for your puja committee."}
```

`Settings.tsx:72,81` — the rollover name suggestion. Derive from the source year's own name:

```tsx
      // Mirrors the server's rollover naming: carry the committee's own
      // wording forward rather than assuming a festival.
      name: `${sourceYear.name.replace(/\s*\d{4}\s*$/, "").trim() || ""} ${sourceYear.year + 1}`.trim(),
```

`package.json` — `"description": "Multi-tenant puja committee finance & collection management"` (already changed in Task 2; verify).

`NavContent.tsx` — already changed in Task 12 Step 6; verify no literal remains.

- [ ] **Step 5: Run the guard test and confirm it passes**

```bash
dotnet test tests/Utsav.UnitTests --filter FullyQualifiedName~NoFestivalHardcodingTests
npx --prefix src/Utsav.Web tsc -b
```

Expected: PASS, and the frontend type-checks.

- [ ] **Step 6: Run everything and commit**

```bash
dotnet test tests/Utsav.UnitTests
dotnet test tests/Utsav.IsolationTests
git add -A
git commit -m "refactor: derive festival naming from the tenant

Removes the inherited 'Durga Puja' literals from year rollover, the
audit report, the Asura prompts, the page title and the login copy. Year
names now carry the committee's own wording forward — 'Kali Puja 2026'
rolls into 'Kali Puja 2027' — falling back to the bare year rather than
guessing a festival.

A guard test walks src/ and fails on any new 'Durga Puja' literal,
exempting FestivalTemplates, which is where festival names legitimately
live as seed data."
```

---

### Task 18: Session cookie and CSRF hardening *(cut line — skip if Day 2 is short)*

**Files:**
- Modify: `src/Utsav.Api/Endpoints/AuthEndpoints.cs`
- Modify: `src/Utsav.Api/Authentication/LegacySessionAuthenticationHandler.cs`
- Modify: `src/Utsav.Infrastructure/Data/Entities/Session.cs`
- Modify: `src/Utsav.Web/src/lib/api.ts`
- Modify: `src/Utsav.Web/src/context/AuthContext.tsx`
- Test: `tests/Utsav.IsolationTests/SessionCookieTests.cs`

**Interfaces:**
- Consumes: `SessionService`.
- Produces: `Session.ExpiresAt` (DateTime), `Session.LastSeenAt` (DateTime); cookie name `utsav_session`; CSRF header `X-Utsav-Csrf`.

**This task is the designated cut.** It has no multi-tenancy dependency, and the new app has no live users during development, so deferring it endangers nobody. If Day 2 is running short, stop after Task 17 and schedule this before any real committee signs in.

- [ ] **Step 1: Write the failing test**

`tests/Utsav.IsolationTests/SessionCookieTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Utsav.IsolationTests;

[Collection(nameof(DatabaseFixture))]
public class SessionCookieTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Sign_in_sets_an_httponly_cookie_and_returns_no_token_in_the_body()
    {
        using var client = fixture.AnonymousClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "both@test.local", password = fixture.KnownPassword,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("utsav_session=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie.ToLowerInvariant());

        // The token must not also come back in JSON — that would put it back
        // within reach of any script on the origin, defeating the cookie.
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"token\"", body);
    }

    [Fact]
    public async Task A_mutating_request_without_the_csrf_header_is_refused()
    {
        // The cookie is now sent ambiently, so a cross-site form post would
        // otherwise carry it. The header is what a cross-origin page cannot set.
        using var client = await fixture.SignedInClientAsync("both@test.local", withCsrfHeader: false);

        var response = await client.PostAsJsonAsync("/api/vendors", new { name = "CSRF probe" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_mutating_request_with_the_csrf_header_succeeds()
    {
        using var client = await fixture.SignedInClientAsync("both@test.local", withCsrfHeader: true);

        var response = await client.PostAsJsonAsync("/api/vendors", new { name = "Legitimate vendor" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_expired_session_is_refused_even_though_its_row_still_exists()
    {
        var token = await fixture.CreateExpiredSessionAsync();
        using var client = fixture.ClientWithCookie(token);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

Add the fixture helpers this test needs (`AnonymousClient`, `SignedInClientAsync`, `ClientWithCookie`, `CreateExpiredSessionAsync`, `KnownPassword`) to `DatabaseFixture`, seeding the two users with a real scrypt hash of `KnownPassword` via `ScryptPasswordHasher.Hash`.

**This task breaks every existing isolation test** unless `ClientFor` is converted at the same time. It currently sets `Authorization: Bearer`, which the handler stops reading in Step 5. Change it to set the cookies instead, so the suites written in Tasks 10, 11 and 14 keep working unchanged:

```csharp
    /// <summary>
    /// A client whose session cookie pins it to one committee. The CSRF cookie
    /// is set to a known value and echoed as a header, which is what the
    /// browser's own JavaScript does.
    /// </summary>
    public HttpClient ClientFor(string token)
    {
        var client = _factory!.CreateClient();
        const string csrf = "test-csrf-token";
        client.DefaultRequestHeaders.Add("Cookie", $"utsav_session={token}; utsav_csrf={csrf}");
        client.DefaultRequestHeaders.Add("X-Utsav-Csrf", csrf);
        return client;
    }
```

Run the full isolation suite — not just this task's tests — before committing, because this is the one task in the plan that changes an interface every other test depends on.

- [ ] **Step 2: Run it and confirm it fails**

```bash
cd /home/atip/sourceCode/Utsav
dotnet test tests/Utsav.IsolationTests --filter FullyQualifiedName~SessionCookieTests
```

Expected: all four fail — the token is still a bearer header in the body.

- [ ] **Step 3: Add expiry to the session entity**

```csharp
    /// <summary>Absolute expiry. Enforced at load, so a stolen token has a bounded life.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Last request on this session, for idle timeout.</summary>
    public DateTime LastSeenAt { get; set; }
```

In `SessionService.CreateSessionAsync`, set `ExpiresAt = DateTime.UtcNow.AddDays(30)` and `LastSeenAt = DateTime.UtcNow`. In `LoadSessionAsync`, after loading the row:

```csharp
        var now = DateTime.UtcNow;
        if (row.Session.ExpiresAt <= now || row.Session.LastSeenAt.AddDays(14) <= now)
        {
            // Delete rather than merely refuse: an expired row is dead weight
            // and leaving it invites a later bug from treating it as live.
            await db.Sessions.Where(s => s.Token == token).ExecuteDeleteAsync(ct);
            return null;
        }

        row.Session.LastSeenAt = now;
        await db.SaveChangesAsync(ct);
```

Generate the migration:

```bash
dotnet ef migrations add AddSessionExpiry -p src/Utsav.Infrastructure -s src/Utsav.Api -o Data/Migrations
dotnet ef database update -p src/Utsav.Infrastructure -s src/Utsav.Api
```

- [ ] **Step 4: Issue the cookie instead of returning the token**

In `AuthEndpoints.IssueSessionAsync`:

```csharp
    private static async Task<IResult> IssueSessionAsync(
        Guid userId, HttpContext http, SessionService sessions, CancellationToken ct)
    {
        var token = await sessions.CreateSessionAsync(userId, ct);

        // httpOnly so no script on the origin can read it, which removes the
        // XSS token-theft path entirely. Lax rather than Strict: Strict would
        // drop the cookie on the return leg of the Google and Microsoft
        // sign-in redirects and break social login.
        http.Response.Cookies.Append(SessionCookie.Name, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = !http.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase),
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(30),
        });

        // A separate, deliberately readable cookie whose value the client
        // echoes in a header. A cross-origin page can cause the session
        // cookie to be sent but cannot read this one to set the header.
        var csrf = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        http.Response.Cookies.Append(SessionCookie.CsrfName, csrf, new CookieOptions
        {
            HttpOnly = false,
            Secure = !http.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase),
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });

        var session = await sessions.LoadSessionAsync(token, ct);
        // No token in the body, deliberately.
        return Results.Ok(session);
    }
```

Add `src/Utsav.Api/Authentication/SessionCookie.cs` with `public const string Name = "utsav_session";` and `public const string CsrfName = "utsav_csrf";`.

- [ ] **Step 5: Read the cookie in the handler**

In `LegacySessionAuthenticationHandler.HandleAuthenticateAsync`, read the cookie first and keep the bearer header only for API keys:

```csharp
        // Cookie first. The Authorization header is left to the API-key
        // scheme, which is the only remaining legitimate bearer caller.
        var token = Request.Cookies[SessionCookie.Name];
        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }
```

- [ ] **Step 6: Enforce CSRF on mutating requests**

Add middleware in `Program.cs`, after `UseAuthentication`:

```csharp
// Cookies are sent ambiently, so a mutating request must also prove it came
// from our own page by echoing the readable CSRF cookie in a header. GET and
// HEAD are exempt: they must not mutate anything, and exempting them keeps
// plain navigation working.
app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method)
        || HttpMethods.IsHead(context.Request.Method)
        || HttpMethods.IsOptions(context.Request.Method)
        // API keys authenticate per request and carry no cookie, so CSRF
        // does not apply to them.
        || context.User.Identity?.AuthenticationType == ApiKeyAuthenticationDefaults.SchemeName
        || !context.Request.Path.StartsWithSegments("/api"))
    {
        await next();
        return;
    }

    var cookie = context.Request.Cookies[SessionCookie.CsrfName];
    var header = context.Request.Headers["X-Utsav-Csrf"].ToString();

    // Anonymous auth routes (login/signup) have no cookie yet.
    if (string.IsNullOrEmpty(cookie) && context.User.Identity?.IsAuthenticated != true)
    {
        await next();
        return;
    }

    if (string.IsNullOrEmpty(cookie) || !CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(cookie), System.Text.Encoding.UTF8.GetBytes(header)))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "CSRF validation failed" });
        return;
    }

    await next();
});
```

- [ ] **Step 7: Update the frontend client**

In `src/Utsav.Web/src/lib/api.ts`, delete `getToken`/`setToken`/`TOKEN_KEY` and the `Authorization` headers, and instead send credentials and the CSRF header:

```ts
/**
 * Auth is an httpOnly session cookie set by the API, so there is no token for
 * this module to hold. Mutating requests echo the readable CSRF cookie in a
 * header, which a cross-origin page cannot do.
 */
const CSRF_COOKIE = "utsav_csrf";

function csrfToken(): string {
  const match = document.cookie.match(new RegExp(`(?:^|; )${CSRF_COOKIE}=([^;]*)`));
  return match ? decodeURIComponent(match[1]) : "";
}

function headers(method: string, hasBody: boolean): HeadersInit {
  return {
    ...(hasBody ? { "Content-Type": "application/json" } : {}),
    ...(method === "GET" || method === "HEAD" ? {} : { "X-Utsav-Csrf": csrfToken() }),
  };
}
```

Pass `credentials: "include"` on every `fetch`. In `AuthContext.tsx`, remove the `token` destructuring from `authenticate` (the body no longer carries one), remove the `getToken()` check in the mount effect — call `/auth/me` unconditionally and treat an `ApiError` as "not signed in" — and drop `setToken(null)` from `signOut`.

- [ ] **Step 8: Run the tests, verify sign-in end to end, commit**

```bash
dotnet test tests/Utsav.IsolationTests
dotnet test tests/Utsav.UnitTests
npx --prefix src/Utsav.Web tsc -b
```

Then sign in through the browser, confirm the app loads, confirm a write succeeds, and confirm `localStorage` holds no token. Capture the Application-tab cookie list as evidence.

```bash
git add -A
git commit -m "feat: httpOnly session cookie, CSRF defence, session expiry

Moves the session token out of localStorage, where any script on the
origin could read it, into an httpOnly/Secure/SameSite=Lax cookie, and
stops returning it in the response body — returning it there would put
it straight back within reach and defeat the cookie.

SameSite is Lax rather than Strict deliberately: Strict drops the cookie
on the return leg of the Google and Microsoft sign-in redirects.

Because the cookie is now ambient, mutating requests must echo a
readable CSRF cookie in X-Utsav-Csrf, compared in fixed time. Sessions
also gain absolute (30d) and idle (14d) expiry, deleted on load rather
than merely refused."
```

---

### Task 19: Deployment configuration

**Files:**
- Modify: `.github/workflows/deploy.yml`
- Create: `docs/DEPLOYMENT.md`

**Interfaces:**
- Consumes: everything above.
- Produces: a deploy workflow and a documented list of required App Service settings.

- [ ] **Step 1: Point the workflow at the new projects and resource**

In `.github/workflows/deploy.yml`, update: the unit-test path to `tests/Utsav.UnitTests`, the publish project to `src/Utsav.Api`, `AZURE_WEBAPP_NAME` to the new App Service name, and the publish-profile secret name to a new one. Keep the existing `warnings as errors` build step and the frontend type-check step.

- [ ] **Step 2: Document every required setting**

`docs/DEPLOYMENT.md`:

```markdown
# Deploying Utsav

## App Service configuration

| Setting | Purpose |
|---|---|
| `ConnectionStrings__Default` | Azure SQL. The application's login must be a member of `utsav_app` only — **never** `db_owner`, or row-level security can be disabled by the app itself. |
| `ConnectionStrings__Deploy` | Migration-only login, member of `utsav_deploy`. Used by `dotnet ef database update`, never by the running app. |
| `Platform__BootstrapAdminEmail` | Promotes this account to platform operator on first start. No-op once any platform admin exists. |
| `AzureAd__TenantId`, `AzureAd__ClientId` | New Entra app registration for this hostname. |
| `Google__ClientId` | New Google OAuth client for this hostname. |
| `Cosmos__Endpoint`, `Cosmos__Database`, `Cosmos__Container` | Audit trail. Container must be partitioned on `/orgId`. |
| `Storage__ConnectionString` | Blob storage for documents and committee logos. |
| `Push__PublicKey`, `Push__PrivateKey` | New VAPID key pair. |
| `Asura__Enabled` | Leave unset. The AI assistant stays off unless deliberately enabled. |

## Redirect URIs

Both identity providers must list the new origin, or social sign-in fails
with no code-level workaround:

- Google OAuth client → Authorised JavaScript origins: `https://<host>`
- Entra app registration → Redirect URI (SPA): `https://<host>`

## First run

1. Apply migrations with the `Deploy` connection string.
2. Start the app. No committee is created — this is deliberate; a pooled
   platform must not conjure a tenant.
3. Sign up with the `Platform__BootstrapAdminEmail` address, then restart so
   the promotion applies.
4. Provision the first committee: `POST /api/platform/committees` with
   `{ name, festival, first_year, first_admin_email }`.
5. The named admin signs up with that address and lands in an active
   membership.
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "chore: deployment workflow and configuration reference

Documents the two-principal database setup the RLS backstop depends on:
the app connects as utsav_app (datareader/datawriter only) and must
never be db_owner, or it could disable the security policy it runs
under. Migrations use a separate utsav_deploy login."
```

---

## Done criteria

```bash
cd /home/atip/sourceCode/Utsav
dotnet build Utsav.sln                  # zero warnings
dotnet test tests/Utsav.UnitTests       # all pass
dotnet test tests/Utsav.IsolationTests  # all pass — a failure here is a data leak
npx --prefix src/Utsav.Web tsc -b       # clean
grep -rn 'DefaultOrgId\|00000000-0000-0000-0000-000000000001' src tests | grep -v bin/ | grep -v obj/   # silent
```

Plus, verified in the browser: a two-committee user sees the chooser, enters one, switches from the sidebar, and sees different figures and different branding per committee; an audit-report PDF carries each committee's own letterhead.

## Not in this plan

Deferred per the spec, with nothing here blocking them later: custom domain per committee (needs wildcard TLS plus a cross-domain SSO handoff), per-tenant PWA manifest and icons, the React 19 / Vite 7 / Tailwind 4 upgrade, Kalipuja's historical data import (format unknown), a platform admin console UI, self-serve signup, and any migration of the existing Durga Puja committee onto this platform.
