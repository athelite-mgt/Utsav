# Utsav

Multi-tenant finance and collection management for puja committees.

Forked from the single-committee Durga Puja application; see
`docs/superpowers/specs/2026-10-23-utsav-multi-tenant-platform-design.md`
for the design and `docs/superpowers/plans/` for the implementation plan.

## Local development

Local services (SQL Server and the Azure Storage emulator) come from Docker; the
SQL Server password lives in a git-ignored `.env` at the repo root
(`MSSQL_SA_PASSWORD=...`, `UTSAV_APP_PASSWORD=...`). Load it into your shell, then:

    set -a; . ./.env; set +a
    docker compose -f docker-compose.dev.yml up -d --wait   # SQL Server 2022 + Azurite

    # 1. Create the schema (runs as sa; the migrations create the security policy):
    export ConnectionStrings__AzureSql="Server=localhost,1433;Database=utsav_dev;User Id=sa;Password=$MSSQL_SA_PASSWORD;TrustServerCertificate=True"
    dotnet ef database update -p src/Utsav.Infrastructure -s src/Utsav.Api

    # 2. One-off: create the least-privilege login the app runs as (see "Database principals").

    # 3. Run the app as that login, never as sa. Audit goes to the log and uploads
    #    go to the emulator, so no Azure account is needed:
    export ConnectionStrings__AzureSql="Server=localhost,1433;Database=utsav_dev;User Id=utsav_app;Password=$UTSAV_APP_PASSWORD;TrustServerCertificate=True"
    export Audit__Mode=Log Storage__ConnectionString="UseDevelopmentStorage=true" Storage__ContainerName=utsav-dev
    ASPNETCORE_URLS=http://localhost:5174 dotnet run --project src/Utsav.Api --no-launch-profile

    # 4. The web app (Vite on :5173, proxying /api to :5174):
    cd src/Utsav.Web && npm install && npm run dev

`Audit__Mode=Log` is an explicit opt-in. The default is Cosmos, and a deployment
missing its Cosmos configuration refuses to start rather than run without an
audit trail.

The first platform operator is whoever `Platform__BootstrapAdminEmail` names (an
account that already exists); it is inert once any operator exists.

## Database principals

Tenant isolation is enforced by Azure SQL row-level security, which is only a
real backstop if the application cannot switch it off. Two principals:

- **Migrations / deployment**: a high-privilege login (locally `sa`) that runs
  `dotnet ef database update`.
- **The running app**: `utsav_app`, a member of `utsav_app_role` only
  (datareader + datawriter). It is not `db_owner` and cannot alter the policy.

Create the app login once after migrating (password from `UTSAV_APP_PASSWORD`
in `.env`); `scripts/sql/create-app-principal.sql` documents the two steps.

Note that RLS also constrains `sa`/`dbo`: with no tenant set you see no tenant
rows, by design. Use `-I` with `sqlcmd` (QUOTED_IDENTIFIER) when poking the
database by hand.

## Tests

    dotnet test tests/Utsav.UnitTests
    dotnet test tests/Utsav.IsolationTests   # needs the SQL Server container; set MSSQL_SA_PASSWORD and UTSAV_APP_PASSWORD
    cd src/Utsav.Web && npm test             # pure-logic tests (colour contrast, ...)
