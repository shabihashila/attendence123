-- The application connects as feghr_app, a member of feghr_runtime: DML on the module
-- schemas and EXECUTE on their procedures, no DDL, and no direct access to the control-plane
-- tables. Row-level security then limits every statement to the bound company.
IF DATABASE_PRINCIPAL_ID(N'feghr_runtime') IS NULL
    CREATE ROLE feghr_runtime AUTHORIZATION dbo;
IF DATABASE_PRINCIPAL_ID(N'feghr_app') IS NULL
    CREATE USER feghr_app WITHOUT LOGIN;
IF NOT EXISTS(SELECT 1 FROM sys.database_role_members
              WHERE role_principal_id=DATABASE_PRINCIPAL_ID(N'feghr_runtime')
              AND member_principal_id=DATABASE_PRINCIPAL_ID(N'feghr_app'))
    ALTER ROLE feghr_runtime ADD MEMBER feghr_app;
GO
-- Schema grants cover tables and procedures that later releases add.
GRANT SELECT,INSERT,UPDATE,DELETE,EXECUTE ON SCHEMA::platform TO feghr_runtime;
GRANT SELECT,INSERT,UPDATE,DELETE,EXECUTE ON SCHEMA::iam TO feghr_runtime;
GRANT SELECT,INSERT,UPDATE,DELETE,EXECUTE ON SCHEMA::org TO feghr_runtime;
GRANT SELECT,INSERT,UPDATE,DELETE,EXECUTE ON SCHEMA::attendance TO feghr_runtime;
GRANT SELECT,INSERT,UPDATE,DELETE,EXECUTE ON SCHEMA::leave TO feghr_runtime;
GRANT SELECT,EXECUTE ON SCHEMA::reporting TO feghr_runtime;
GRANT INSERT,SELECT ON OBJECT::logs.Logs TO feghr_runtime;

-- Control plane: reached through owned procedures only.
DENY SELECT,INSERT,UPDATE,DELETE ON OBJECT::iam.Users TO feghr_runtime;
DENY SELECT,INSERT,UPDATE,DELETE ON OBJECT::iam.UserTenants TO feghr_runtime;
DENY INSERT,UPDATE,DELETE ON OBJECT::iam.TenantUsers TO feghr_runtime;
DENY INSERT,UPDATE,DELETE ON OBJECT::iam.Permissions TO feghr_runtime;
DENY INSERT,UPDATE,DELETE ON OBJECT::iam.Menus TO feghr_runtime;
DENY INSERT,UPDATE,DELETE ON OBJECT::iam.MenuPermissions TO feghr_runtime;
DENY INSERT,UPDATE,DELETE ON OBJECT::iam.RoleGrantSeeds TO feghr_runtime;
DENY SELECT,INSERT,UPDATE,DELETE ON OBJECT::platform.Tenants TO feghr_runtime;
DENY SELECT,INSERT,UPDATE,DELETE ON OBJECT::platform.TenantHosts TO feghr_runtime;
DENY SELECT,INSERT,UPDATE,DELETE ON OBJECT::platform.Licenses TO feghr_runtime;
DENY INSERT,UPDATE,DELETE ON OBJECT::platform.LicensePolicy TO feghr_runtime;
DENY INSERT,UPDATE,DELETE ON OBJECT::platform.Plans TO feghr_runtime;
DENY INSERT,UPDATE,DELETE ON OBJECT::platform.Features TO feghr_runtime;
DENY INSERT,DELETE ON OBJECT::platform.PlatformBranding TO feghr_runtime;

-- Facts that only their procedures may write.
DENY INSERT,DELETE ON OBJECT::attendance.Punches TO feghr_runtime;
DENY INSERT,UPDATE,DELETE ON OBJECT::leave.Applications TO feghr_runtime;

-- Onboarding steps run inside iam.usp_Company_Register (ownership chaining), never on their own.
DENY EXECUTE ON OBJECT::platform.usp_Tenant_Provision TO feghr_runtime;
DENY EXECUTE ON OBJECT::org.usp_Employee_CreateForUser TO feghr_runtime;
DENY EXECUTE ON OBJECT::iam.usp_PlatformOwner_JoinTenant TO feghr_runtime;
DENY EXECUTE ON OBJECT::attendance.usp_Tenant_SeedDefaults TO feghr_runtime;
DENY EXECUTE ON OBJECT::leave.usp_Tenant_SeedDefaults TO feghr_runtime;

-- No DDL anywhere, and the security helpers are deployment-only.
DENY ALTER ON SCHEMA::platform TO feghr_runtime;
DENY ALTER ON SCHEMA::iam TO feghr_runtime;
DENY ALTER ON SCHEMA::org TO feghr_runtime;
DENY ALTER ON SCHEMA::attendance TO feghr_runtime;
DENY ALTER ON SCHEMA::leave TO feghr_runtime;
DENY ALTER ON SCHEMA::reporting TO feghr_runtime;
DENY ALTER ON SCHEMA::logs TO feghr_runtime;
DENY ALTER,EXECUTE ON SCHEMA::security TO feghr_runtime;
GO
