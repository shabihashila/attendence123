-- Global control plane. Root company ID is the group ID; business data remains company-scoped.
IF COL_LENGTH(N'iam.UserTenants',N'IsGroupManaged') IS NULL
    ALTER TABLE iam.UserTenants ADD IsGroupManaged BIT NOT NULL CONSTRAINT DF_UserTenants_GroupManaged DEFAULT 0;
GO
IF OBJECT_ID(N'iam.GroupRoles',N'U') IS NULL
    CREATE TABLE iam.GroupRoles(Code NVARCHAR(30) PRIMARY KEY,Name NVARCHAR(100) NOT NULL);
IF OBJECT_ID(N'iam.GroupRolePermissions',N'U') IS NULL
    CREATE TABLE iam.GroupRolePermissions(RoleCode NVARCHAR(30) NOT NULL REFERENCES iam.GroupRoles(Code),
        PermissionId INT NOT NULL REFERENCES iam.Permissions(Id),PRIMARY KEY(RoleCode,PermissionId));
IF OBJECT_ID(N'iam.GroupMembers',N'U') IS NULL
BEGIN
    CREATE TABLE iam.GroupMembers(GroupId INT NOT NULL REFERENCES platform.Tenants(Id),UserId INT NOT NULL REFERENCES iam.Users(Id),
        RoleCode NVARCHAR(30) NOT NULL REFERENCES iam.GroupRoles(Code),IsActive BIT NOT NULL DEFAULT 1,
        UpdatedAt DATETIMEOFFSET(3) NOT NULL,UpdatedBy INT NOT NULL REFERENCES iam.Users(Id),RowVersion ROWVERSION,
        PRIMARY KEY(GroupId,UserId));
    CREATE INDEX IX_GroupMembers_User ON iam.GroupMembers(UserId,IsActive) INCLUDE(GroupId,RoleCode);
END;
IF OBJECT_ID(N'platform.GroupSettings',N'U') IS NULL
    CREATE TABLE platform.GroupSettings(GroupId INT PRIMARY KEY REFERENCES platform.Tenants(Id),
        SettingsJson NVARCHAR(MAX) NOT NULL CHECK(ISJSON(SettingsJson)=1),RowVersion ROWVERSION);
IF OBJECT_ID(N'platform.GlobalSettings',N'U') IS NULL
    CREATE TABLE platform.GlobalSettings(Id INT PRIMARY KEY CHECK(Id=1),SettingsJson NVARCHAR(MAX) NOT NULL CHECK(ISJSON(SettingsJson)=1),RowVersion ROWVERSION);
IF NOT EXISTS(SELECT 1 FROM platform.GlobalSettings WHERE Id=1) INSERT platform.GlobalSettings(Id,SettingsJson) VALUES(1,N'{}');
IF OBJECT_ID(N'platform.GroupBillingProfiles',N'U') IS NULL
    CREATE TABLE platform.GroupBillingProfiles(GroupId INT PRIMARY KEY REFERENCES platform.Tenants(Id),
        BillingName NVARCHAR(200) NOT NULL,Email NVARCHAR(254) NOT NULL,Address NVARCHAR(500),UpdatedAt DATETIMEOFFSET(3) NOT NULL,UpdatedBy INT NOT NULL REFERENCES iam.Users(Id),RowVersion ROWVERSION);
IF OBJECT_ID(N'platform.CompanySettingOverrides',N'U') IS NULL
    CREATE TABLE platform.CompanySettingOverrides(TenantId INT PRIMARY KEY REFERENCES platform.Tenants(Id),
        SettingsJson NVARCHAR(MAX) NOT NULL CHECK(ISJSON(SettingsJson)=1),RowVersion ROWVERSION);
IF OBJECT_ID(N'platform.GroupAudit',N'U') IS NULL
BEGIN
    CREATE TABLE platform.GroupAudit(Id BIGINT IDENTITY PRIMARY KEY,GroupId INT NOT NULL REFERENCES platform.Tenants(Id),
        ActorId INT NOT NULL REFERENCES iam.Users(Id),Action NVARCHAR(80) NOT NULL,Detail NVARCHAR(1000),OccurredAt DATETIMEOFFSET(3) NOT NULL);
    CREATE INDEX IX_GroupAudit_Group ON platform.GroupAudit(GroupId,Id DESC);
END;
INSERT iam.GroupRoles(Code,Name) SELECT Code,Name FROM (VALUES(N'group-admin',N'Master Group Admin'),(N'group-viewer',N'Group Auditor'),(N'group-billing',N'Group Billing')) v(Code,Name)
WHERE NOT EXISTS(SELECT 1 FROM iam.GroupRoles r WHERE r.Code=v.Code);
INSERT iam.GroupRolePermissions(RoleCode,PermissionId)
SELECT r.Code,p.Id FROM iam.GroupRoles r CROSS JOIN iam.Permissions p
WHERE p.IsActive=1 AND p.Code NOT LIKE N'owner.%' AND
    (r.Code=N'group-admin' OR (r.Code=N'group-viewer' AND (p.Code LIKE N'%.read' OR p.Code LIKE N'%.reports.%'))
     OR (r.Code=N'group-billing' AND p.Code=N'platform.settings.read'))
AND NOT EXISTS(SELECT 1 FROM iam.GroupRolePermissions rp WHERE rp.RoleCode=r.Code AND rp.PermissionId=p.Id);
DENY SELECT,INSERT,UPDATE,DELETE ON OBJECT::iam.GroupMembers TO feghr_runtime;
DENY SELECT,INSERT,UPDATE,DELETE ON OBJECT::iam.GroupRoles TO feghr_runtime;
DENY SELECT,INSERT,UPDATE,DELETE ON OBJECT::iam.GroupRolePermissions TO feghr_runtime;
DENY SELECT,INSERT,UPDATE,DELETE ON OBJECT::platform.GroupSettings TO feghr_runtime;
DENY SELECT,INSERT,UPDATE,DELETE ON OBJECT::platform.GlobalSettings TO feghr_runtime;
DENY SELECT,INSERT,UPDATE,DELETE ON OBJECT::platform.GroupBillingProfiles TO feghr_runtime;
DENY SELECT,INSERT,UPDATE,DELETE ON OBJECT::platform.CompanySettingOverrides TO feghr_runtime;
DENY SELECT,INSERT,UPDATE,DELETE ON OBJECT::platform.GroupAudit TO feghr_runtime;
EXEC security.usp_ApplyTenantPolicy @Schema=N'platform';
GO
DECLARE @Now DATETIMEOFFSET(3)=SYSDATETIMEOFFSET();
IF NOT EXISTS(SELECT 1 FROM iam.Permissions WHERE Code=N'group.console')
    INSERT iam.Permissions(Code,Name,Module,CreatedAt) VALUES(N'group.console',N'Open Group Console',N'group',@Now);
INSERT iam.GroupRolePermissions(RoleCode,PermissionId) SELECT r.Code,p.Id FROM iam.GroupRoles r CROSS JOIN iam.Permissions p WHERE p.Code=N'group.console'
AND NOT EXISTS(SELECT 1 FROM iam.GroupRolePermissions gp WHERE gp.RoleCode=r.Code AND gp.PermissionId=p.Id);
IF NOT EXISTS(SELECT 1 FROM iam.Menus WHERE Code=N'group-console')
    INSERT iam.Menus(ParentId,Code,Title,Icon,Route,SortOrder,CreatedAt)
    SELECT Id,N'group-console',N'Group Console',N'pi pi-sitemap',N'/app/admin/group',445,@Now FROM iam.Menus WHERE Code=N'administration';
IF NOT EXISTS(SELECT 1 FROM iam.Menus WHERE Code=N'company-settings')
    INSERT iam.Menus(ParentId,Code,Title,Icon,Route,SortOrder,CreatedAt)
    SELECT Id,N'company-settings',N'Company Settings',N'pi pi-cog',N'/app/admin/settings',446,@Now FROM iam.Menus WHERE Code=N'administration';
INSERT iam.MenuPermissions(MenuId,PermissionId,CreatedAt)
SELECT m.Id,p.Id,@Now FROM iam.Menus m JOIN iam.Permissions p ON (m.Code=N'group-console' AND p.Code=N'group.console') OR (m.Code=N'company-settings' AND p.Code=N'platform.settings.write')
WHERE NOT EXISTS(SELECT 1 FROM iam.MenuPermissions mp WHERE mp.MenuId=m.Id AND mp.PermissionId=p.Id);
GO
CREATE OR ALTER FUNCTION iam.fn_GroupRole(@UserId INT,@TenantId INT)
RETURNS TABLE AS RETURN
    SELECT gm.RoleCode,gm.GroupId FROM iam.GroupMembers gm JOIN iam.Users u ON u.Id=gm.UserId AND u.IsActive=1
    JOIN platform.Tenants root ON root.Id=gm.GroupId AND root.ParentTenantRefId IS NULL AND root.IsActive=1 AND root.Status=N'Active'
    JOIN platform.Tenants t ON t.Id=@TenantId AND (t.Id=root.Id OR t.ParentTenantRefId=root.Id)
    WHERE gm.UserId=@UserId AND gm.IsActive=1;
GO
CREATE OR ALTER PROCEDURE iam.usp_User_CanAccessTenant @UserId INT,@TenantId INT,@ExpectedPlatformAdmin BIT=0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM iam.UserTenants x JOIN iam.Users u ON u.Id=x.UserId AND u.IsActive=1
        WHERE x.UserId=@UserId AND x.TenantRefId=@TenantId AND x.IsActive=1 AND u.IsPlatformAdmin=@ExpectedPlatformAdmin
        AND (x.IsGroupManaged=0 OR EXISTS(SELECT 1 FROM iam.fn_GroupRole(@UserId,@TenantId)))) THEN 1 ELSE 0 END);
END;
GO
CREATE OR ALTER PROCEDURE iam.usp_User_IsGroupAdmin @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM iam.fn_GroupRole(@UserId,TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId'))) WHERE RoleCode=N'group-admin') THEN 1 ELSE 0 END);
END;
GO
CREATE OR ALTER PROCEDURE iam.usp_TenantUser_List
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Tid INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId'));
    IF ISNULL(@Tid,0)<=0 THROW 51045,'Tenant context is required.',1;
    SELECT tu.Id AS TenantUserId,u.Id AS UserId,u.DisplayName,u.Email,u.Phone,tu.IsActive,u.IsPlatformAdmin,
        x.IsGroupManaged,CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM iam.fn_GroupRole(u.Id,@Tid) WHERE RoleCode=N'group-admin') THEN 1 ELSE 0 END) AS IsGroupProtected
    FROM iam.TenantUsers tu JOIN iam.Users u ON u.Id=tu.UserId JOIN iam.UserTenants x ON x.TenantRefId=@Tid AND x.UserId=u.Id;
END;
GO
CREATE OR ALTER PROCEDURE iam.usp_Membership_MakeDirect @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Tid INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId')),@Actor INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.UserId'));
    IF NOT EXISTS(SELECT 1 FROM iam.fn_UserPermissions(@Actor) WHERE Code=N'iam.users.write') THROW 51101,'Company user management permission required.',1;
    IF EXISTS(SELECT 1 FROM iam.fn_GroupRole(@UserId,@Tid) WHERE RoleCode=N'group-admin') THROW 51101,'Manage Group Admin access through Group Console.',1;
    UPDATE iam.UserTenants SET IsGroupManaged=0 WHERE UserId=@UserId AND TenantRefId=@Tid;
END;
GO
CREATE OR ALTER PROCEDURE iam.usp_User_GetTenants @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Prior sql_variant=SESSION_CONTEXT(N'FegHR.TenantId'),@Tid INT,@MemberId INT;
    DECLARE companies CURSOR LOCAL FAST_FORWARD FOR
        SELECT t.Id FROM platform.Tenants t WHERE t.IsActive=1 AND t.Status=N'Active' AND EXISTS(SELECT 1 FROM iam.fn_GroupRole(@UserId,t.Id));
    BEGIN TRY
        OPEN companies;
        FETCH NEXT FROM companies INTO @Tid;
        WHILE @@FETCH_STATUS=0
        BEGIN
            EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Tid;
            BEGIN TRANSACTION;
            SET @MemberId=NULL;
            SELECT @MemberId=Id FROM iam.TenantUsers WITH(UPDLOCK,HOLDLOCK) WHERE UserId=@UserId;
            IF @MemberId IS NULL
            BEGIN
                INSERT iam.TenantUsers(UserId,CreatedAt) VALUES(@UserId,SYSDATETIMEOFFSET());
                SET @MemberId=CONVERT(int,SCOPE_IDENTITY());
                INSERT iam.UserTenants(UserId,TenantRefId,TenantUserId,IsActive,IsGroupManaged) VALUES(@UserId,@Tid,@MemberId,1,1);
            END;
            COMMIT TRANSACTION;
            FETCH NEXT FROM companies INTO @Tid;
        END;
        CLOSE companies; DEALLOCATE companies;
        EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior;
        THROW;
    END CATCH;
    SELECT t.Id AS TenantId,t.Slug,t.Name,x.TenantUserId FROM iam.UserTenants x
    JOIN platform.Tenants t ON t.Id=x.TenantRefId JOIN iam.Users u ON u.Id=x.UserId
    WHERE x.UserId=@UserId AND x.IsActive=1 AND u.IsActive=1 AND t.IsActive=1 AND t.Status=N'Active'
      AND (x.IsGroupManaged=0 OR EXISTS(SELECT 1 FROM iam.fn_GroupRole(@UserId,t.Id))) ORDER BY t.Id;
END;
GO
CREATE OR ALTER FUNCTION iam.fn_UserPermissions(@UserId INT)
RETURNS TABLE AS RETURN
    WITH Caller AS (
        SELECT u.IsPlatformAdmin,
            CASE WHEN EXISTS(SELECT 1 FROM iam.TenantUsers tu JOIN iam.UserRoles ur ON ur.TenantId=tu.TenantId AND ur.TenantUserId=tu.Id AND ur.IsActive=1
                JOIN iam.Roles r ON r.TenantId=ur.TenantId AND r.Id=ur.RoleId AND r.IsActive=1
                WHERE tu.UserId=@UserId AND tu.IsActive=1 AND r.Code=N'admin') THEN 1 ELSE 0 END AS CompanyAdmin
        FROM iam.Users u WHERE u.Id=@UserId AND u.IsActive=1
    )
    SELECT p.Code FROM iam.Permissions p CROSS JOIN Caller c
    WHERE p.IsActive=1 AND EXISTS(SELECT 1 FROM iam.TenantUsers tu WHERE tu.UserId=@UserId AND tu.IsActive=1)
    AND (p.Code NOT LIKE N'owner.%' OR c.IsPlatformAdmin=1)
    AND (p.Module<>N'group' OR c.IsPlatformAdmin=1 OR EXISTS(SELECT 1 FROM iam.fn_GroupRole(@UserId,TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId')))))
    AND (c.CompanyAdmin=1 OR EXISTS(SELECT 1 FROM iam.fn_GroupRole(@UserId,TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId'))) gr
         JOIN iam.GroupRolePermissions gp ON gp.RoleCode=gr.RoleCode WHERE gp.PermissionId=p.Id)
      OR EXISTS(SELECT 1 FROM iam.TenantUsers tu JOIN iam.UserRoles ur ON ur.TenantId=tu.TenantId AND ur.TenantUserId=tu.Id AND ur.IsActive=1
         JOIN iam.Roles r ON r.TenantId=ur.TenantId AND r.Id=ur.RoleId AND r.IsActive=1
         JOIN iam.RolePermissions rp ON rp.TenantId=r.TenantId AND rp.RoleId=r.Id AND rp.IsActive=1 WHERE tu.UserId=@UserId AND tu.IsActive=1 AND rp.PermissionId=p.Id));
GO
-- Authorize the group from the live root relationship, never from a caller-supplied group claim.
CREATE OR ALTER PROCEDURE platform.usp_Group_Context @Manage BIT=0
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @UserId INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.UserId')),@TenantId INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId'));
    IF @UserId IS NULL OR @TenantId IS NULL THROW 51100,'Sign in to a company first.',1;
    DECLARE @GroupId INT=(SELECT COALESCE(ParentTenantRefId,Id) FROM platform.Tenants WHERE Id=@TenantId);
    IF NOT EXISTS(SELECT 1 FROM iam.Users WHERE Id=@UserId AND IsActive=1 AND IsPlatformAdmin=1)
       AND NOT EXISTS(SELECT 1 FROM iam.fn_GroupRole(@UserId,@TenantId) WHERE @Manage=0 OR RoleCode=N'group-admin')
        THROW 51101,'You do not have permission to manage or inspect this group.',1;
    SELECT @GroupId AS GroupId,(SELECT Name FROM platform.Tenants WHERE Id=@GroupId) AS Name,
        CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM iam.Users WHERE Id=@UserId AND IsActive=1 AND IsPlatformAdmin=1)
            OR EXISTS(SELECT 1 FROM iam.fn_GroupRole(@UserId,@TenantId) WHERE RoleCode=N'group-admin') THEN 1 ELSE 0 END) AS CanManage,
        CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM iam.Users WHERE Id=@UserId AND IsActive=1 AND IsPlatformAdmin=1)
            OR EXISTS(SELECT 1 FROM iam.fn_GroupRole(@UserId,@TenantId) WHERE RoleCode IN(N'group-admin',N'group-billing')) THEN 1 ELSE 0 END) AS CanBill;
END;
GO
CREATE OR ALTER PROCEDURE iam.usp_Group_Members @Email NVARCHAR(254)=NULL,@RoleCode NVARCHAR(30)=NULL,@IsActive BIT=1
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @Context TABLE(GroupId INT,Name NVARCHAR(200),CanManage BIT,CanBill BIT);
    INSERT @Context EXEC platform.usp_Group_Context @Manage=1;
    DECLARE @GroupId INT=(SELECT GroupId FROM @Context),@Actor INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.UserId'));
    IF @Email IS NOT NULL
    BEGIN
        DECLARE @UserId INT=(SELECT Id FROM iam.Users WHERE NormalizedEmail=UPPER(LTRIM(RTRIM(@Email))) AND IsActive=1);
        IF @UserId IS NULL THROW 51102,'Create an active user under Administration > Users first, then use their existing email.',1;
        IF NOT EXISTS(SELECT 1 FROM iam.GroupRoles WHERE Code=@RoleCode) THROW 51103,'Choose a valid group role.',1;
        IF @Actor=@UserId AND (@IsActive=0 OR @RoleCode<>N'group-admin') THROW 51104,'Ask another Group Admin or the Owner to change your own group access.',1;
        BEGIN TRANSACTION;
        DECLARE @Lock INT;
        EXEC @Lock=sys.sp_getapplock @Resource=N'FegHR.CompanyHierarchy',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
        IF @Lock<0 THROW 51105,'Group access is busy. Try again.',1;
        UPDATE iam.GroupMembers SET RoleCode=@RoleCode,IsActive=@IsActive,UpdatedAt=SYSDATETIMEOFFSET(),UpdatedBy=@Actor WHERE GroupId=@GroupId AND UserId=@UserId;
        IF @@ROWCOUNT=0 INSERT iam.GroupMembers(GroupId,UserId,RoleCode,IsActive,UpdatedAt,UpdatedBy) VALUES(@GroupId,@UserId,@RoleCode,@IsActive,SYSDATETIMEOFFSET(),@Actor);
        INSERT platform.GroupAudit(GroupId,ActorId,Action,Detail,OccurredAt) VALUES(@GroupId,@Actor,N'Group membership changed',CONCAT(@Email,N' / ',@RoleCode,N' / active=',@IsActive),SYSDATETIMEOFFSET());
        COMMIT TRANSACTION;
    END;
    SELECT u.Id AS UserId,u.DisplayName,u.Email,m.RoleCode,m.IsActive FROM iam.GroupMembers m JOIN iam.Users u ON u.Id=m.UserId WHERE m.GroupId=@GroupId ORDER BY u.DisplayName;
END;
GO
CREATE OR ALTER PROCEDURE platform.usp_Group_Report
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Context TABLE(GroupId INT,Name NVARCHAR(200),CanManage BIT,CanBill BIT);
    INSERT @Context EXEC platform.usp_Group_Context;
    DECLARE @GroupId INT=(SELECT GroupId FROM @Context),@Prior sql_variant=SESSION_CONTEXT(N'FegHR.TenantId'),@Tid INT,
        @User INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.UserId'));
    DECLARE @BillingOnly BIT=CASE WHEN EXISTS(SELECT 1 FROM iam.fn_GroupRole(@User,TRY_CONVERT(int,@Prior)) WHERE RoleCode=N'group-billing') THEN 1 ELSE 0 END;
    DECLARE @Report TABLE(Id INT,Name NVARCHAR(200),Employees INT,Users INT,AttendanceDays INT,PendingLeave INT,LicenseKind NVARCHAR(20),LicenseEndsAt DATETIMEOFFSET(3),RowVersion BINARY(8));
    DECLARE companies CURSOR LOCAL FAST_FORWARD FOR SELECT Id FROM platform.Tenants WHERE Id=@GroupId OR ParentTenantRefId=@GroupId;
    BEGIN TRY
        OPEN companies; FETCH NEXT FROM companies INTO @Tid;
        WHILE @@FETCH_STATUS=0
        BEGIN
            EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Tid;
            INSERT @Report SELECT t.Id,t.Name,CASE WHEN @BillingOnly=0 THEN (SELECT COUNT(*) FROM org.Employees WHERE IsActive=1) END,CASE WHEN @BillingOnly=0 THEN (SELECT COUNT(*) FROM iam.TenantUsers WHERE IsActive=1) END,
                CASE WHEN @BillingOnly=0 THEN (SELECT COUNT(*) FROM attendance.DailyLogs WHERE IsActive=1) END,CASE WHEN @BillingOnly=0 THEN (SELECT COUNT(*) FROM leave.Applications WHERE IsActive=1 AND Status=N'Pending') END,e.Kind,e.EndsAt,t.RowVersion
            FROM platform.Tenants t OUTER APPLY platform.fn_EffectiveLicense(t.Id,SYSDATETIMEOFFSET()) e WHERE t.Id=@Tid AND (t.Id=@GroupId OR t.ParentTenantRefId=@GroupId);
            FETCH NEXT FROM companies INTO @Tid;
        END;
        CLOSE companies; DEALLOCATE companies;
        EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior;
    END TRY BEGIN CATCH
        EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior; THROW;
    END CATCH;
    SELECT * FROM @Report ORDER BY Id;
END;
GO
CREATE OR ALTER PROCEDURE platform.usp_Group_Audit
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Context TABLE(GroupId INT,Name NVARCHAR(200),CanManage BIT,CanBill BIT);
    INSERT @Context EXEC platform.usp_Group_Context;
    IF EXISTS(SELECT 1 FROM iam.fn_GroupRole(TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.UserId')),TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId'))) WHERE RoleCode=N'group-billing')
        THROW 51101,'The billing role cannot view group audit records.',1;
    SELECT TOP(200) a.Id,u.DisplayName AS Actor,a.Action,a.Detail,a.OccurredAt FROM platform.GroupAudit a JOIN iam.Users u ON u.Id=a.ActorId WHERE a.GroupId=(SELECT GroupId FROM @Context) ORDER BY a.Id DESC;
END;
GO
CREATE OR ALTER PROCEDURE platform.usp_Group_Settings @SettingsJson NVARCHAR(MAX)=NULL,@CompanyOverride BIT=0,@GlobalOverride BIT=0,@RowVersion BINARY(8)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Tid INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId')),@User INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.UserId'));
    DECLARE @Gid INT=(SELECT COALESCE(ParentTenantRefId,Id) FROM platform.Tenants WHERE Id=@Tid);
    IF @SettingsJson IS NOT NULL
    BEGIN
        IF ISJSON(@SettingsJson)<>1 OR LEFT(LTRIM(@SettingsJson),1)<>N'{' THROW 51106,'Settings must be a JSON object.',1;
        IF @GlobalOverride=1
        BEGIN
            IF NOT EXISTS(SELECT 1 FROM iam.Users WHERE Id=@User AND IsActive=1 AND IsPlatformAdmin=1) THROW 51101,'Only the platform Owner can change global defaults.',1;
            UPDATE platform.GlobalSettings SET SettingsJson=@SettingsJson WHERE Id=1 AND RowVersion=@RowVersion;
            IF @@ROWCOUNT=0 THROW 51107,'Settings changed. Reload and try again.',1;
        END
        ELSE IF @CompanyOverride=1
        BEGIN
            IF NOT EXISTS(SELECT 1 FROM iam.fn_UserPermissions(@User) WHERE Code=N'platform.settings.write') THROW 51101,'Company settings permission required.',1;
            IF EXISTS(SELECT 1 FROM platform.CompanySettingOverrides WHERE TenantId=@Tid)
            BEGIN
                UPDATE platform.CompanySettingOverrides SET SettingsJson=@SettingsJson WHERE TenantId=@Tid AND RowVersion=@RowVersion;
                IF @@ROWCOUNT=0 THROW 51107,'Settings changed. Reload and try again.',1;
            END ELSE INSERT platform.CompanySettingOverrides(TenantId,SettingsJson) VALUES(@Tid,@SettingsJson);
        END ELSE BEGIN
            DECLARE @Context TABLE(GroupId INT,Name NVARCHAR(200),CanManage BIT,CanBill BIT);
            INSERT @Context EXEC platform.usp_Group_Context @Manage=1;
            IF EXISTS(SELECT 1 FROM platform.GroupSettings WHERE GroupId=@Gid)
            BEGIN
                UPDATE platform.GroupSettings SET SettingsJson=@SettingsJson WHERE GroupId=@Gid AND RowVersion=@RowVersion;
                IF @@ROWCOUNT=0 THROW 51107,'Settings changed. Reload and try again.',1;
            END ELSE INSERT platform.GroupSettings(GroupId,SettingsJson) VALUES(@Gid,@SettingsJson);
        END;
        INSERT platform.GroupAudit(GroupId,ActorId,Action,Detail,OccurredAt) VALUES(@Gid,@User,N'Settings changed',CASE WHEN @CompanyOverride=1 THEN N'Company overrides' ELSE N'Group defaults' END,SYSDATETIMEOFFSET());
    END;
    SELECT (SELECT SettingsJson FROM platform.GlobalSettings WHERE Id=1) AS GlobalDefaults,
        COALESCE((SELECT SettingsJson FROM platform.GroupSettings WHERE GroupId=@Gid),N'{}') AS GroupDefaults,
        COALESCE((SELECT SettingsJson FROM platform.CompanySettingOverrides WHERE TenantId=@Tid),N'{}') AS CompanyOverrides,
        (SELECT RowVersion FROM platform.GlobalSettings WHERE Id=1) AS GlobalRowVersion,
        (SELECT RowVersion FROM platform.GroupSettings WHERE GroupId=@Gid) AS GroupRowVersion,
        (SELECT RowVersion FROM platform.CompanySettingOverrides WHERE TenantId=@Tid) AS CompanyRowVersion;
END;
GO
CREATE OR ALTER PROCEDURE platform.usp_Group_Billing @BillingName NVARCHAR(200)=NULL,@Email NVARCHAR(254)=NULL,@Address NVARCHAR(500)=NULL,@RowVersion BINARY(8)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Context TABLE(GroupId INT,Name NVARCHAR(200),CanManage BIT,CanBill BIT);
    INSERT @Context EXEC platform.usp_Group_Context;
    DECLARE @Gid INT=(SELECT GroupId FROM @Context),@Uid INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.UserId'));
    IF @BillingName IS NOT NULL
    BEGIN
        IF NOT EXISTS(SELECT 1 FROM @Context WHERE CanManage=1) AND NOT EXISTS(SELECT 1 FROM iam.fn_GroupRole(@Uid,TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId'))) WHERE RoleCode=N'group-billing')
            THROW 51101,'Group billing or administration access required.',1;
        IF NULLIF(LTRIM(RTRIM(@BillingName)),N'') IS NULL OR @Email NOT LIKE N'_%@_%._%' THROW 51108,'Enter a billing name and email.',1;
        IF EXISTS(SELECT 1 FROM platform.GroupBillingProfiles WHERE GroupId=@Gid)
        BEGIN
            UPDATE platform.GroupBillingProfiles SET BillingName=@BillingName,Email=@Email,Address=@Address,UpdatedAt=SYSDATETIMEOFFSET(),UpdatedBy=@Uid WHERE GroupId=@Gid AND RowVersion=@RowVersion;
            IF @@ROWCOUNT=0 THROW 51107,'Billing profile changed. Reload and try again.',1;
        END ELSE INSERT platform.GroupBillingProfiles(GroupId,BillingName,Email,Address,UpdatedAt,UpdatedBy) VALUES(@Gid,@BillingName,@Email,@Address,SYSDATETIMEOFFSET(),@Uid);
        INSERT platform.GroupAudit(GroupId,ActorId,Action,Detail,OccurredAt) VALUES(@Gid,@Uid,N'Billing profile changed',@BillingName,SYSDATETIMEOFFSET());
    END;
    SELECT BillingName,Email,Address,RowVersion FROM platform.GroupBillingProfiles WHERE GroupId=@Gid;
END;
GO
