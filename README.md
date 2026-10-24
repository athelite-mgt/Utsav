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
    ASPNETCORE_URLS=http://localhost:5174 dotnet run --project src/Utsav.Api
    cd src/Utsav.Web && npm install && npm run dev           # Vite on :5173, proxies /api to :5174

## Tests

    dotnet test tests/Utsav.UnitTests
    dotnet test tests/Utsav.IsolationTests   # needs the SQL Server container
