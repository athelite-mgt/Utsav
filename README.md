# Utsav

Multi-tenant finance and collection management for puja committees.

Forked from the single-committee Durga Puja application; see
`docs/superpowers/specs/2026-10-23-utsav-multi-tenant-platform-design.md`
for the design and `docs/superpowers/plans/` for the implementation plan.

## Local development

The local SQL Server password lives in a git-ignored `.env` at the repo root
(`MSSQL_SA_PASSWORD=...`). Load it into your shell, then:

    set -a; . ./.env; set +a
    export ConnectionStrings__AzureSql="Server=localhost,1433;Database=utsav_dev;User Id=sa;Password=$MSSQL_SA_PASSWORD;TrustServerCertificate=True"
    docker compose -f docker-compose.dev.yml up -d --wait   # SQL Server 2022
    dotnet ef database update -p src/Utsav.Infrastructure -s src/Utsav.Api
    # Run the app as the least-privilege login, never sa (see "Database principals"):
    export ConnectionStrings__AzureSql="Server=localhost,1433;Database=utsav_dev;User Id=utsav_app;Password=$UTSAV_APP_PASSWORD;TrustServerCertificate=True"
    ASPNETCORE_URLS=http://localhost:5174 dotnet run --project src/Utsav.Api
    cd src/Utsav.Web && npm install && npm run dev           # Vite on :5173, proxies /api to :5174

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
    dotnet test tests/Utsav.IsolationTests   # needs the SQL Server container
