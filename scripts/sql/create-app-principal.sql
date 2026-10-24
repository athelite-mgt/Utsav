-- Creates the least-privilege login the application connects as.
--
-- The app login is a member of utsav_app_role ONLY (datareader + datawriter).
-- It must never be db_owner or hold ALTER ANY SECURITY POLICY: row-level
-- security is only a backstop if the principal it constrains cannot switch it
-- off. Migrations and bulk import use a separate, higher-privileged login that
-- is a member of utsav_deploy_role.
--
-- Run in two steps with sqlcmd (-v supplies the password, never hardcode one):
--   1. against master:           :setvar STEP login
--   2. against the app database: :setvar STEP user
--
--   sqlcmd -S <server> -U sa -C -d master  -v APP_PASSWORD="..." STEP=login -i create-app-principal.sql
--   sqlcmd -S <server> -U sa -C -d <db>    -v APP_PASSWORD="..." STEP=user  -i create-app-principal.sql

IF '$(STEP)' = 'login'
BEGIN
    IF SUSER_ID(N'utsav_app') IS NULL
        EXEC(N'CREATE LOGIN utsav_app WITH PASSWORD = N''$(APP_PASSWORD)'', CHECK_POLICY = OFF;');
END
ELSE IF '$(STEP)' = 'user'
BEGIN
    IF USER_ID(N'utsav_app') IS NULL
        CREATE USER utsav_app FOR LOGIN utsav_app;
    ALTER ROLE utsav_app_role ADD MEMBER utsav_app;
END
