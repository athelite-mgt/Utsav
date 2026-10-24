# Utsav

Multi-tenant finance and collection management for puja committees.

Forked from the single-committee Durga Puja application; see
`docs/superpowers/specs/2026-10-23-utsav-multi-tenant-platform-design.md`
for the design and `docs/superpowers/plans/` for the implementation plan.

## Local development

    export MSSQL_SA_PASSWORD='<choose a strong password>'
    docker compose -f docker-compose.dev.yml up -d   # SQL Server 2022
    dotnet ef database update -p src/Utsav.Infrastructure -s src/Utsav.Api
    dotnet run --project src/Utsav.Api
    cd src/Utsav.Web && npm install && npm run dev

## Tests

    dotnet test tests/Utsav.UnitTests
    dotnet test tests/Utsav.IsolationTests   # needs the SQL Server container
