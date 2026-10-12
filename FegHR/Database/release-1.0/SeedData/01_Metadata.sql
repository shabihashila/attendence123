-- Product metadata: features, plans, licence policy, permission catalog, navigation and the
-- default grants of the system roles. Idempotent; runs on every deployment.
DECLARE @Now DATETIMEOFFSET(3)=CONVERT(datetimeoffset(3),N'$(SeedTimestamp)');

INSERT platform.Features(Code,Name,CreatedAt)
SELECT x.Code,x.Name,@Now FROM (VALUES
    (N'organization',N'Organization and employees'),
    (N'attendance',N'Attendance with geo-fencing and photo'),
    (N'leave',N'Leave management')) x(Code,Name)
WHERE NOT EXISTS(SELECT 1 FROM platform.Features f WHERE f.Code=x.Code);

-- Plan contents and prices are the product owner's decision; these are starting values.
INSERT platform.Plans(Code,Name,MonthlyPrice,FeatureCodesJson,MaxEmployees,CreatedAt)
SELECT x.Code,x.Name,0,x.Features,x.MaxEmployees,@Now FROM (VALUES
    (N'starter',N'Starter',N'["organization","attendance"]',25),
    (N'business',N'Business',N'["organization","attendance","leave"]',NULL)) x(Code,Name,Features,MaxEmployees)
WHERE NOT EXISTS(SELECT 1 FROM platform.Plans p WHERE p.Code=x.Code);

IF NOT EXISTS(SELECT 1 FROM platform.LicensePolicy WHERE Id=1)
    INSERT platform.LicensePolicy(Id,TrialDays,TrialPlanId,UpdatedAt)
    SELECT 1,60,Id,@Now FROM platform.Plans WHERE Code=N'business';

INSERT iam.Permissions(Code,Name,Module,CreatedAt)
SELECT x.Code,x.Name,x.Module,@Now FROM (VALUES
    (N'platform.settings.read',N'View company settings and licence',N'platform'),
    (N'platform.settings.write',N'Edit company settings and branding',N'platform'),
    (N'platform.branches.read',N'View branches',N'platform'),
    (N'platform.branches.write',N'Manage branches',N'platform'),
    (N'iam.users.read',N'View users',N'iam'),
    (N'iam.users.write',N'Manage users',N'iam'),
    (N'iam.roles.read',N'View roles',N'iam'),
    (N'iam.roles.write',N'Manage roles and permissions',N'iam'),
    (N'iam.permissions.read',N'View the permission catalog',N'iam'),
    (N'org.employees.read',N'View employees',N'org'),
    (N'org.employees.write',N'Manage employees',N'org'),
    (N'org.setup.read',N'View departments and designations',N'org'),
    (N'org.setup.write',N'Manage departments and designations',N'org'),
    (N'attendance.punch.self',N'Record own attendance',N'attendance'),
    (N'attendance.logs.read',N'View attendance of all employees',N'attendance'),
    (N'attendance.logs.write',N'Enter and correct attendance',N'attendance'),
    (N'attendance.review',N'Review flagged punches',N'attendance'),
    (N'attendance.setup.read',N'View work locations, schedules and holidays',N'attendance'),
    (N'attendance.setup.write',N'Manage work locations, schedules, holidays and rules',N'attendance'),
    (N'attendance.reports.read',N'View attendance reports',N'attendance'),
    (N'leave.apply.self',N'Apply for own leave',N'leave'),
    (N'leave.applications.read',N'View all leave applications',N'leave'),
    (N'leave.approve.l1',N'Approve leave as line manager',N'leave'),
    (N'leave.approve.l2',N'Approve leave as HR (final)',N'leave'),
    (N'leave.adjust',N'Adjust leave balances',N'leave'),
    (N'leave.setup.write',N'Manage leave types',N'leave'),
    (N'leave.reports.read',N'View leave statements',N'leave'),
    (N'owner.companies.manage',N'Manage companies',N'owner'),
    (N'owner.licenses.manage',N'Manage licences and the trial policy',N'owner'),
    (N'owner.branding.manage',N'Manage product branding',N'owner')) x(Code,Name,Module)
WHERE NOT EXISTS(SELECT 1 FROM iam.Permissions p WHERE p.Code=x.Code);

-- Navigation. Groups have no route; a leaf is shown when the user holds one of its permissions
-- and the licence includes its feature.
DECLARE @Menus TABLE(Code NVARCHAR(80),ParentCode NVARCHAR(80),Title NVARCHAR(150),Icon NVARCHAR(80),Route NVARCHAR(200),SortOrder INT,FeatureCode NVARCHAR(80));
INSERT @Menus VALUES
    (N'dashboard',NULL,N'Dashboard',N'pi pi-fw pi-home',N'/app/dashboard',10,NULL),
    (N'attendance',NULL,N'Attendance',N'pi pi-fw pi-clock',NULL,100,N'attendance'),
    (N'attendance-my',N'attendance',N'My Attendance',N'pi pi-fw pi-map-marker',N'/app/attendance/my',110,N'attendance'),
    (N'attendance-daily',N'attendance',N'Daily Attendance',N'pi pi-fw pi-calendar',N'/app/attendance/daily',120,N'attendance'),
    (N'attendance-monthly',N'attendance',N'Monthly Summary',N'pi pi-fw pi-chart-bar',N'/app/attendance/monthly',130,N'attendance'),
    (N'attendance-review',N'attendance',N'Punch Review',N'pi pi-fw pi-flag',N'/app/attendance/review',140,N'attendance'),
    (N'attendance-locations',N'attendance',N'Work Locations',N'pi pi-fw pi-map',N'/app/attendance/locations',150,N'attendance'),
    (N'attendance-schedules',N'attendance',N'Work Schedules',N'pi pi-fw pi-calendar-clock',N'/app/attendance/schedules',160,N'attendance'),
    (N'attendance-holidays',N'attendance',N'Holidays',N'pi pi-fw pi-sun',N'/app/attendance/holidays',170,N'attendance'),
    (N'attendance-settings',N'attendance',N'Attendance Rules',N'pi pi-fw pi-sliders-h',N'/app/attendance/settings',180,N'attendance'),
    (N'leave',NULL,N'Leave',N'pi pi-fw pi-calendar-minus',NULL,200,N'leave'),
    (N'leave-my',N'leave',N'My Leave',N'pi pi-fw pi-send',N'/app/leave/my',210,N'leave'),
    (N'leave-applications',N'leave',N'Leave Applications',N'pi pi-fw pi-inbox',N'/app/leave/applications',220,N'leave'),
    (N'leave-statement',N'leave',N'Leave Statement',N'pi pi-fw pi-list',N'/app/leave/statement',230,N'leave'),
    (N'leave-adjustments',N'leave',N'Balance Adjustments',N'pi pi-fw pi-plus-circle',N'/app/leave/adjustments',240,N'leave'),
    (N'leave-types',N'leave',N'Leave Types',N'pi pi-fw pi-tags',N'/app/leave/types',250,N'leave'),
    (N'organization',NULL,N'Organization',N'pi pi-fw pi-sitemap',NULL,300,NULL),
    (N'org-employees',N'organization',N'Employees',N'pi pi-fw pi-users',N'/app/organization/employees',310,NULL),
    (N'org-departments',N'organization',N'Departments',N'pi pi-fw pi-building',N'/app/organization/departments',320,NULL),
    (N'org-designations',N'organization',N'Designations',N'pi pi-fw pi-id-card',N'/app/organization/designations',330,NULL),
    (N'org-branches',N'organization',N'Branches',N'pi pi-fw pi-map-marker',N'/app/organization/branches',340,NULL),
    (N'administration',NULL,N'Administration',N'pi pi-fw pi-cog',NULL,400,NULL),
    (N'admin-users',N'administration',N'Users',N'pi pi-fw pi-user',N'/app/admin/users',410,NULL),
    (N'admin-roles',N'administration',N'Roles',N'pi pi-fw pi-lock',N'/app/admin/roles',420,NULL),
    (N'admin-branding',N'administration',N'Company Branding',N'pi pi-fw pi-palette',N'/app/admin/branding',430,NULL),
    (N'admin-licence',N'administration',N'Licence',N'pi pi-fw pi-verified',N'/app/admin/licence',440,NULL),
    (N'owner',NULL,N'Product Owner',N'pi pi-fw pi-crown',NULL,500,NULL),
    (N'owner-companies',N'owner',N'Companies',N'pi pi-fw pi-briefcase',N'/app/owner/companies',510,NULL),
    (N'owner-policy',N'owner',N'Licence Policy',N'pi pi-fw pi-key',N'/app/owner/licence-policy',520,NULL),
    (N'owner-branding',N'owner',N'Product Branding',N'pi pi-fw pi-image',N'/app/owner/branding',530,NULL);
INSERT iam.Menus(ParentId,Code,Title,Icon,Route,SortOrder,FeatureCode,CreatedAt)
SELECT NULL,m.Code,m.Title,m.Icon,m.Route,m.SortOrder,m.FeatureCode,@Now FROM @Menus m
WHERE m.ParentCode IS NULL AND NOT EXISTS(SELECT 1 FROM iam.Menus x WHERE x.Code=m.Code);
INSERT iam.Menus(ParentId,Code,Title,Icon,Route,SortOrder,FeatureCode,CreatedAt)
SELECT p.Id,m.Code,m.Title,m.Icon,m.Route,m.SortOrder,m.FeatureCode,@Now FROM @Menus m JOIN iam.Menus p ON p.Code=m.ParentCode
WHERE NOT EXISTS(SELECT 1 FROM iam.Menus x WHERE x.Code=m.Code);

INSERT iam.MenuPermissions(MenuId,PermissionId,CreatedAt)
SELECT m.Id,p.Id,@Now FROM (VALUES
    (N'attendance-my',N'attendance.punch.self'),
    (N'attendance-daily',N'attendance.logs.read'),
    (N'attendance-monthly',N'attendance.reports.read'),
    (N'attendance-review',N'attendance.review'),
    (N'attendance-locations',N'attendance.setup.read'),
    (N'attendance-schedules',N'attendance.setup.read'),
    (N'attendance-holidays',N'attendance.setup.read'),
    (N'attendance-settings',N'attendance.setup.write'),
    (N'leave-my',N'leave.apply.self'),
    (N'leave-applications',N'leave.applications.read'),
    (N'leave-applications',N'leave.approve.l1'),
    (N'leave-applications',N'leave.approve.l2'),
    (N'leave-statement',N'leave.reports.read'),
    (N'leave-adjustments',N'leave.adjust'),
    (N'leave-types',N'leave.setup.write'),
    (N'org-employees',N'org.employees.read'),
    (N'org-departments',N'org.setup.read'),
    (N'org-designations',N'org.setup.read'),
    (N'org-branches',N'platform.branches.read'),
    (N'admin-users',N'iam.users.read'),
    (N'admin-roles',N'iam.roles.read'),
    (N'admin-branding',N'platform.settings.write'),
    (N'admin-licence',N'platform.settings.read'),
    (N'owner-companies',N'owner.companies.manage'),
    (N'owner-policy',N'owner.licenses.manage'),
    (N'owner-branding',N'owner.branding.manage')) x(MenuCode,PermissionCode)
JOIN iam.Menus m ON m.Code=x.MenuCode JOIN iam.Permissions p ON p.Code=x.PermissionCode
WHERE NOT EXISTS(SELECT 1 FROM iam.MenuPermissions mp WHERE mp.MenuId=m.Id AND mp.PermissionId=p.Id);
GO
-- System roles of the bound company and their default grants. A grant is applied once per
-- company, role and permission (iam.RoleGrantSeeds), so a grant the company removed stays removed
-- and a permission introduced by a later release is granted on the next run.
CREATE OR ALTER PROCEDURE iam.usp_Tenant_SeedRoles
    @Now DATETIMEOFFSET(3)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @TenantId INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId'));
    IF ISNULL(@TenantId,0)<=0 THROW 51022, 'Tenant context is required.', 1;
    IF @Now IS NULL THROW 51023, 'Application timestamp is required.', 1;
    DECLARE @Started BIT=0;
    IF @@TRANCOUNT=0 BEGIN SET @Started=1; BEGIN TRANSACTION; END;
    BEGIN TRY
        INSERT iam.Roles(TenantId,Code,Name,IsSystem,CreatedAt)
        SELECT @TenantId,x.Code,x.Name,1,@Now FROM (VALUES
            (N'admin',N'Company Admin'),(N'hr',N'HR Manager'),(N'manager',N'Line Manager'),(N'employee',N'Employee')) x(Code,Name)
        WHERE NOT EXISTS(SELECT 1 FROM iam.Roles r WITH(UPDLOCK,HOLDLOCK) WHERE r.TenantId=@TenantId AND r.Code=x.Code);

        DECLARE @Grants TABLE(RoleId INT,PermissionId INT);
        INSERT iam.RoleGrantSeeds(TenantId,RoleId,PermissionId,AppliedAt)
        OUTPUT inserted.RoleId,inserted.PermissionId INTO @Grants(RoleId,PermissionId)
        SELECT @TenantId,r.Id,p.Id,@Now FROM iam.Roles r CROSS JOIN iam.Permissions p
        WHERE r.TenantId=@TenantId AND r.IsSystem=1 AND p.IsActive=1 AND p.Module<>N'owner'
          AND NOT EXISTS(SELECT 1 FROM iam.RoleGrantSeeds g WITH(UPDLOCK,HOLDLOCK) WHERE g.TenantId=r.TenantId AND g.RoleId=r.Id AND g.PermissionId=p.Id)
          AND (r.Code=N'admin'
               OR r.Code=N'hr' AND (p.Module IN(N'org',N'attendance',N'leave')
                                    OR p.Code IN(N'platform.branches.read',N'platform.branches.write',N'platform.settings.read',N'iam.users.read'))
               OR r.Code=N'manager' AND p.Code IN(N'attendance.punch.self',N'attendance.logs.read',N'attendance.reports.read',
                                                  N'leave.apply.self',N'leave.applications.read',N'leave.approve.l1',N'leave.reports.read',
                                                  N'org.employees.read')
               OR r.Code=N'employee' AND p.Code IN(N'attendance.punch.self',N'leave.apply.self'));
        INSERT iam.RolePermissions(TenantId,RoleId,PermissionId,CreatedAt)
        SELECT @TenantId,g.RoleId,g.PermissionId,@Now FROM @Grants g
        WHERE NOT EXISTS(SELECT 1 FROM iam.RolePermissions rp WHERE rp.TenantId=@TenantId AND rp.RoleId=g.RoleId AND rp.PermissionId=g.PermissionId);
        IF @Started=1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @Started=1 AND XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
-- Role seeding is an onboarding and deployment operation, not a runtime endpoint.
DENY EXECUTE ON OBJECT::iam.usp_Tenant_SeedRoles TO feghr_runtime;
GO
-- Bring existing companies up to date: new default grants, default setup rows, product owners.
DECLARE @Now DATETIMEOFFSET(3)=CONVERT(datetimeoffset(3),N'$(SeedTimestamp)');
DECLARE @Prior sql_variant=SESSION_CONTEXT(N'FegHR.TenantId'),@Tenant INT;
DECLARE tenants CURSOR LOCAL FAST_FORWARD FOR SELECT Id FROM platform.Tenants WHERE IsActive=1;
BEGIN TRY
    OPEN tenants; FETCH NEXT FROM tenants INTO @Tenant;
    WHILE @@FETCH_STATUS=0
    BEGIN
        EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Tenant;
        EXEC iam.usp_Tenant_SeedRoles @Now=@Now;
        EXEC attendance.usp_Tenant_SeedDefaults @Now=@Now;
        EXEC leave.usp_Tenant_SeedDefaults @Now=@Now;
        EXEC iam.usp_PlatformOwner_JoinTenant @Now=@Now;
        FETCH NEXT FROM tenants INTO @Tenant;
    END;
    CLOSE tenants; DEALLOCATE tenants;
    EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior;
END TRY
BEGIN CATCH
    IF CURSOR_STATUS('local','tenants')>=0 CLOSE tenants;
    IF CURSOR_STATUS('local','tenants')>=-1 DEALLOCATE tenants;
    EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior;
    THROW;
END CATCH;
GO
