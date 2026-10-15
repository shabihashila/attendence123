-- Company administration is scoped to the current company and its direct children.
-- Platform owners can assign access across all active companies. Preferences store
-- one authorized working company; its parent identifies the default main company.
IF OBJECT_ID(N'iam.UserCompanyPreferences',N'U') IS NULL
    CREATE TABLE iam.UserCompanyPreferences(
        UserId INT NOT NULL PRIMARY KEY REFERENCES iam.Users(Id),
        DefaultTenantRefId INT NOT NULL REFERENCES platform.Tenants(Id),
        UpdatedAt DATETIMEOFFSET(3) NOT NULL);
GO
DENY SELECT,INSERT,UPDATE,DELETE ON OBJECT::iam.UserCompanyPreferences TO feghr_runtime;
GO
CREATE OR ALTER FUNCTION iam.fn_ManagedCompanies(@ActorId INT)
RETURNS TABLE AS RETURN
    WITH Caller AS (
        SELECT u.IsPlatformAdmin,
            TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId')) AS CurrentTenantId,
            CASE WHEN EXISTS(SELECT 1 FROM iam.TenantUsers tu
                JOIN iam.UserRoles ur ON ur.TenantId=tu.TenantId AND ur.TenantUserId=tu.Id AND ur.IsActive=1
                JOIN iam.Roles r ON r.TenantId=ur.TenantId AND r.Id=ur.RoleId AND r.IsActive=1 AND r.Code=N'admin'
                WHERE tu.UserId=@ActorId AND tu.IsActive=1) THEN 1 ELSE 0 END AS CompanyAdmin,
            (SELECT MIN(GroupId) FROM iam.fn_GroupRole(@ActorId,TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId')))
                WHERE RoleCode=N'group-admin') AS AdminGroupId
        FROM iam.Users u WHERE u.Id=@ActorId AND u.IsActive=1
            AND @ActorId=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.UserId'))
    )
    SELECT t.Id AS TenantId,t.Name,t.ParentTenantRefId,p.Name AS ParentName,
        CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM platform.Tenants child WHERE child.ParentTenantRefId=t.Id) THEN 1 ELSE 0 END) AS HasChildConcerns,
        CONVERT(bit,CASE WHEN c.IsPlatformAdmin=1 OR c.CompanyAdmin=1 OR c.AdminGroupId IS NOT NULL THEN 1 ELSE 0 END) AS FullAdministration
    FROM platform.Tenants t LEFT JOIN platform.Tenants p ON p.Id=t.ParentTenantRefId CROSS JOIN Caller c
    WHERE t.IsActive=1 AND t.Status=N'Active' AND (
        c.IsPlatformAdmin=1
        OR (c.CompanyAdmin=1 AND (t.Id=c.CurrentTenantId OR t.ParentTenantRefId=c.CurrentTenantId))
        OR (c.AdminGroupId IS NOT NULL AND (t.Id=c.AdminGroupId OR t.ParentTenantRefId=c.AdminGroupId))
        OR (t.Id=c.CurrentTenantId AND EXISTS(SELECT 1 FROM iam.fn_UserPermissions(@ActorId) WHERE Code=N'iam.users.write')));
GO
CREATE OR ALTER PROCEDURE iam.usp_User_ManagedCompanies @ActorId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM iam.fn_ManagedCompanies(@ActorId) ORDER BY COALESCE(ParentTenantRefId,TenantId),TenantId;
END;
GO
CREATE OR ALTER PROCEDURE iam.usp_User_DefaultCompany @UserId INT,@TenantId INT=NULL,@Save BIT=0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Actor INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.UserId'));
    DECLARE @Allowed TABLE(TenantId INT PRIMARY KEY,ParentId INT NULL);
    INSERT @Allowed SELECT t.Id,t.ParentTenantRefId FROM iam.UserTenants x
        JOIN platform.Tenants t ON t.Id=x.TenantRefId JOIN iam.Users u ON u.Id=x.UserId
        WHERE x.UserId=@UserId AND x.IsActive=1 AND u.IsActive=1 AND t.IsActive=1 AND t.Status=N'Active'
            AND (x.IsGroupManaged=0 OR EXISTS(SELECT 1 FROM iam.fn_GroupRole(@UserId,t.Id)));
    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @Prior INT,@Root INT,@Resolved INT;
        SELECT @Prior=DefaultTenantRefId FROM iam.UserCompanyPreferences WITH(UPDLOCK,HOLDLOCK) WHERE UserId=@UserId;
        IF @Save=1
        BEGIN
            IF NOT EXISTS(SELECT 1 FROM @Allowed WHERE TenantId=@TenantId)
                THROW 51180,'Choose a company the user can currently access.',1;
            SET @Resolved=@TenantId;
        END
        ELSE
        BEGIN
            IF EXISTS(SELECT 1 FROM @Allowed WHERE TenantId=@Prior) SET @Resolved=@Prior;
            ELSE BEGIN
                SELECT @Root=COALESCE(ParentTenantRefId,Id) FROM platform.Tenants WHERE Id=@Prior;
                SELECT TOP(1) @Resolved=TenantId FROM @Allowed
                    ORDER BY CASE WHEN COALESCE(ParentId,TenantId)=@Root THEN 0 ELSE 1 END,
                        COALESCE(ParentId,TenantId),CASE WHEN ParentId IS NOT NULL THEN 0 ELSE 1 END,TenantId;
            END;
        END;
        -- When the chosen main company has accessible concerns, choose a child by default.
        IF EXISTS(SELECT 1 FROM @Allowed WHERE TenantId=@Resolved AND ParentId IS NULL)
            SELECT TOP(1) @Resolved=TenantId FROM @Allowed WHERE ParentId=@Resolved ORDER BY TenantId;
        IF @Save=1 AND (ISNULL(@Actor,0)<=0 OR (@Actor<>@UserId AND NOT EXISTS(
            SELECT 1 FROM iam.fn_ManagedCompanies(@Actor) WHERE TenantId=@Resolved)))
            THROW 51181,'You cannot set a default outside your company administration scope.',1;
        IF @Resolved IS NULL DELETE FROM iam.UserCompanyPreferences WHERE UserId=@UserId;
        ELSE IF @Prior IS NULL INSERT iam.UserCompanyPreferences(UserId,DefaultTenantRefId,UpdatedAt) VALUES(@UserId,@Resolved,SYSDATETIMEOFFSET());
        ELSE IF @Prior<>@Resolved UPDATE iam.UserCompanyPreferences SET DefaultTenantRefId=@Resolved,UpdatedAt=SYSDATETIMEOFFSET() WHERE UserId=@UserId;
        COMMIT TRANSACTION;
        SELECT @Resolved AS TenantId;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
-- A parent administrator may convert a non-protected group membership to direct
-- access. Verify that source authority explicitly and restore the target scope.
CREATE OR ALTER PROCEDURE iam.usp_Membership_MakeDirect @UserId INT,@AdministrationTenantId INT=NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Tid INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId')),
        @Actor INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.UserId')),
        @Prior sql_variant=SESSION_CONTEXT(N'FegHR.TenantId'),@Allowed BIT=0;
    IF EXISTS(SELECT 1 FROM iam.fn_UserPermissions(@Actor) WHERE Code=N'iam.users.write') SET @Allowed=1;
    IF @Allowed=0 AND @AdministrationTenantId IS NOT NULL
    BEGIN
        BEGIN TRY
            EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@AdministrationTenantId;
            IF EXISTS(SELECT 1 FROM iam.fn_ManagedCompanies(@Actor) WHERE TenantId=@Tid AND FullAdministration=1) SET @Allowed=1;
            EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior;
        END TRY
        BEGIN CATCH
            EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior;
            THROW;
        END CATCH;
    END;
    IF @Allowed=0 THROW 51101,'Company user management permission required.',1;
    IF EXISTS(SELECT 1 FROM iam.fn_GroupRole(@UserId,@Tid) WHERE RoleCode=N'group-admin')
        THROW 51101,'Manage Group Admin access through Group Console.',1;
    UPDATE iam.UserTenants SET IsGroupManaged=0 WHERE UserId=@UserId AND TenantRefId=@Tid;
END;
GO
