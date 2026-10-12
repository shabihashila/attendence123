-- iam: identity and access management. Users are global; a user joins companies through
-- TenantUsers (row-level secured) and the global discovery index UserTenants.
-- Global tables: Users, Permissions, Menus, MenuPermissions, UserTenants.
IF OBJECT_ID(N'iam.Users', N'U') IS NULL
BEGIN
    CREATE TABLE [iam].[Users](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
        Email NVARCHAR(254) NULL,
        NormalizedEmail NVARCHAR(254) NULL,
        Phone NVARCHAR(30) NULL,
        NormalizedPhone NVARCHAR(30) NULL,
        DisplayName NVARCHAR(150) NOT NULL,
        PasswordHash NVARCHAR(1000) NULL,
        SecurityStamp UNIQUEIDENTIFIER NOT NULL,
        LockoutEnd DATETIMEOFFSET(3) NULL,
        AccessFailedCount INT NOT NULL CONSTRAINT DF_Users_AccessFailedCount DEFAULT 0,
        -- Product owner: reaches every company through an admin membership and a tenant switch.
        IsPlatformAdmin BIT NOT NULL CONSTRAINT DF_Users_IsPlatformAdmin DEFAULT 0,
        CONSTRAINT CK_Users_Login CHECK(NormalizedEmail IS NOT NULL OR NormalizedPhone IS NOT NULL),
        CONSTRAINT CK_Users_AccessFailedCount CHECK(AccessFailedCount>=0),
        IsActive BIT NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL
    );
END;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'iam.Users') AND name=N'UQ_Users_NormalizedEmail')
    CREATE UNIQUE INDEX UQ_Users_NormalizedEmail ON iam.Users(NormalizedEmail) WHERE NormalizedEmail IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'iam.Users') AND name=N'UQ_Users_NormalizedPhone')
    CREATE UNIQUE INDEX UQ_Users_NormalizedPhone ON iam.Users(NormalizedPhone) WHERE NormalizedPhone IS NOT NULL;
GO
IF OBJECT_ID(N'iam.Permissions', N'U') IS NULL
BEGIN
    CREATE TABLE [iam].[Permissions](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Permissions PRIMARY KEY,
        Code NVARCHAR(100) NOT NULL,
        Name NVARCHAR(150) NOT NULL,
        Module NVARCHAR(40) NOT NULL,
        CONSTRAINT UQ_Permissions_Code UNIQUE(Code),
        IsActive BIT NOT NULL CONSTRAINT DF_Permissions_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL
    );
END;
GO
IF OBJECT_ID(N'iam.Menus', N'U') IS NULL
BEGIN
    CREATE TABLE [iam].[Menus](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Menus PRIMARY KEY,
        ParentId INT NULL,
        Code NVARCHAR(80) NOT NULL,
        Title NVARCHAR(150) NOT NULL,
        Icon NVARCHAR(80) NULL,
        Route NVARCHAR(200) NULL,
        SortOrder INT NOT NULL CONSTRAINT DF_Menus_SortOrder DEFAULT 0,
        FeatureCode NVARCHAR(80) NULL,
        CONSTRAINT UQ_Menus_Code UNIQUE(Code),
        CONSTRAINT FK_Menus_Menus FOREIGN KEY(ParentId) REFERENCES iam.Menus(Id),
        IsActive BIT NOT NULL CONSTRAINT DF_Menus_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL
    );
END;
GO
IF OBJECT_ID(N'iam.MenuPermissions',N'U') IS NULL
BEGIN
    CREATE TABLE [iam].MenuPermissions(
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MenuPermissions PRIMARY KEY,
        MenuId INT NOT NULL, PermissionId INT NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_MenuPermissions_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL, CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL, UpdatedBy INT NULL, RowVersion ROWVERSION NOT NULL,
        CONSTRAINT UQ_MenuPermissions_MenuId_PermissionId UNIQUE(MenuId,PermissionId),
        CONSTRAINT FK_MenuPermissions_Menus FOREIGN KEY(MenuId) REFERENCES iam.Menus(Id),
        CONSTRAINT FK_MenuPermissions_Permissions FOREIGN KEY(PermissionId) REFERENCES iam.Permissions(Id)
    );
END;
GO
IF OBJECT_ID(N'iam.TenantUsers', N'U') IS NULL
BEGIN
    CREATE TABLE [iam].[TenantUsers](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TenantUsers PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_TenantUsers_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        UserId INT NOT NULL,
        CONSTRAINT UQ_TenantUsers_TenantId_UserId UNIQUE(TenantId,UserId),
        CONSTRAINT FK_TenantUsers_Users FOREIGN KEY(UserId) REFERENCES iam.Users(Id),
        IsActive BIT NOT NULL CONSTRAINT DF_TenantUsers_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_TenantUsers_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id),
        CONSTRAINT UQ_TenantUsers_TenantId_Id UNIQUE(TenantId, Id)
    );
END;
GO
-- Global discovery index: which companies a user belongs to, read before any tenant is bound.
-- Maintained only by the membership procedures.
IF OBJECT_ID(N'iam.UserTenants',N'U') IS NULL
CREATE TABLE [iam].UserTenants(
    UserId INT NOT NULL,TenantRefId INT NOT NULL,TenantUserId INT NOT NULL,IsActive BIT NOT NULL,
    CONSTRAINT PK_UserTenants PRIMARY KEY(UserId,TenantRefId),
    CONSTRAINT UQ_UserTenants_TenantRefId_TenantUserId UNIQUE(TenantRefId,TenantUserId),
    CONSTRAINT FK_UserTenants_Users FOREIGN KEY(UserId) REFERENCES iam.Users(Id),
    CONSTRAINT FK_UserTenants_TenantUsers FOREIGN KEY(TenantRefId,TenantUserId) REFERENCES iam.TenantUsers(TenantId,Id));
GO
IF OBJECT_ID(N'iam.Roles', N'U') IS NULL
BEGIN
    CREATE TABLE [iam].[Roles](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_Roles_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        Code NVARCHAR(50) NOT NULL,
        Name NVARCHAR(100) NOT NULL,
        IsSystem BIT NOT NULL CONSTRAINT DF_Roles_IsSystem DEFAULT 0,
        CONSTRAINT UQ_Roles_TenantId_Code UNIQUE(TenantId,Code),
        IsActive BIT NOT NULL CONSTRAINT DF_Roles_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_Roles_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id),
        CONSTRAINT UQ_Roles_TenantId_Id UNIQUE(TenantId, Id)
    );
END;
GO
IF OBJECT_ID(N'iam.RolePermissions', N'U') IS NULL
BEGIN
    CREATE TABLE [iam].[RolePermissions](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RolePermissions PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_RolePermissions_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        RoleId INT NOT NULL,
        PermissionId INT NOT NULL,
        CONSTRAINT UQ_RolePermissions_TenantId_RoleId_PermissionId UNIQUE(TenantId,RoleId,PermissionId),
        CONSTRAINT FK_RolePermissions_Roles FOREIGN KEY(TenantId,RoleId) REFERENCES iam.Roles(TenantId,Id),
        CONSTRAINT FK_RolePermissions_Permissions FOREIGN KEY(PermissionId) REFERENCES iam.Permissions(Id),
        IsActive BIT NOT NULL CONSTRAINT DF_RolePermissions_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_RolePermissions_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id),
        CONSTRAINT UQ_RolePermissions_TenantId_Id UNIQUE(TenantId, Id)
    );
END;
GO
-- Default grants of the system roles are one-time facts: a grant a company removed is not
-- restored by a later deployment, while a newly introduced permission is granted once.
IF OBJECT_ID(N'iam.RoleGrantSeeds',N'U') IS NULL
CREATE TABLE [iam].RoleGrantSeeds(
    TenantId INT NOT NULL CONSTRAINT DF_RoleGrantSeeds_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
    RoleId INT NOT NULL,PermissionId INT NOT NULL,AppliedAt DATETIMEOFFSET(3) NOT NULL,
    CONSTRAINT PK_RoleGrantSeeds PRIMARY KEY(TenantId,RoleId,PermissionId),
    CONSTRAINT FK_RoleGrantSeeds_Role FOREIGN KEY(TenantId,RoleId) REFERENCES iam.Roles(TenantId,Id),
    CONSTRAINT FK_RoleGrantSeeds_Permission FOREIGN KEY(PermissionId) REFERENCES iam.Permissions(Id),
    CONSTRAINT FK_RoleGrantSeeds_Tenant FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id));
GO
IF OBJECT_ID(N'iam.UserRoles', N'U') IS NULL
BEGIN
    CREATE TABLE [iam].[UserRoles](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserRoles PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_UserRoles_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        TenantUserId INT NOT NULL,
        RoleId INT NOT NULL,
        CONSTRAINT UQ_UserRoles_TenantId_TenantUserId_RoleId UNIQUE(TenantId,TenantUserId,RoleId),
        CONSTRAINT FK_UserRoles_TenantUsers FOREIGN KEY(TenantId,TenantUserId) REFERENCES iam.TenantUsers(TenantId,Id),
        CONSTRAINT FK_UserRoles_Roles FOREIGN KEY(TenantId,RoleId) REFERENCES iam.Roles(TenantId,Id),
        IsActive BIT NOT NULL CONSTRAINT DF_UserRoles_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_UserRoles_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id),
        CONSTRAINT UQ_UserRoles_TenantId_Id UNIQUE(TenantId, Id)
    );
END;
GO
-- A member with no rows may work in every branch; rows restrict the member to those branches.
IF OBJECT_ID(N'iam.UserBranches', N'U') IS NULL
BEGIN
    CREATE TABLE [iam].[UserBranches](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserBranches PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_UserBranches_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        TenantUserId INT NOT NULL,
        BranchId INT NOT NULL,
        CONSTRAINT UQ_UserBranches_TenantId_TenantUserId_BranchId UNIQUE(TenantId,TenantUserId,BranchId),
        CONSTRAINT FK_UserBranches_TenantUsers FOREIGN KEY(TenantId,TenantUserId) REFERENCES iam.TenantUsers(TenantId,Id),
        CONSTRAINT CK_UserBranches_BranchId CHECK(BranchId>0),
        IsActive BIT NOT NULL CONSTRAINT DF_UserBranches_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_UserBranches_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id),
        CONSTRAINT UQ_UserBranches_TenantId_Id UNIQUE(TenantId, Id)
    );
END;
GO
IF OBJECT_ID(N'iam.RefreshTokens', N'U') IS NULL
BEGIN
    CREATE TABLE [iam].[RefreshTokens](
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RefreshTokens PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_RefreshTokens_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        TenantUserId INT NOT NULL,
        TokenHash BINARY(32) NOT NULL,
        ExpiresAt DATETIMEOFFSET(3) NOT NULL,
        RevokedAt DATETIMEOFFSET(3) NULL,
        ReplacedByTokenHash BINARY(32) NULL,
        CONSTRAINT UQ_RefreshTokens_TenantId_TokenHash UNIQUE(TenantId,TokenHash),
        CONSTRAINT FK_RefreshTokens_TenantUsers FOREIGN KEY(TenantId,TenantUserId) REFERENCES iam.TenantUsers(TenantId,Id),
        CONSTRAINT CK_RefreshTokens_Dates CHECK(ExpiresAt>CreatedAt),
        IsActive BIT NOT NULL CONSTRAINT DF_RefreshTokens_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_RefreshTokens_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id),
        CONSTRAINT UQ_RefreshTokens_TenantId_Id UNIQUE(TenantId, Id)
    );
END;
GO
EXEC security.usp_ApplyTenantPolicy @Schema=N'iam';
GO
-- The application verifies PasswordHash and lockout itself. Credential material leaves the
-- database only through this procedure; iam.Users has no table grant.
CREATE OR ALTER PROCEDURE iam.usp_User_GetLogin @NormalizedLogin NVARCHAR(254)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id,DisplayName,PasswordHash,SecurityStamp,LockoutEnd,AccessFailedCount,IsPlatformAdmin
    FROM iam.Users WHERE IsActive=1 AND (NormalizedEmail=@NormalizedLogin OR NormalizedPhone=@NormalizedLogin);
END;
GO
CREATE OR ALTER PROCEDURE iam.usp_User_GetById @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id,DisplayName,Email,Phone,IsActive,IsPlatformAdmin FROM iam.Users WHERE Id=@UserId;
END;
GO
CREATE OR ALTER PROCEDURE iam.usp_User_RecordLoginResult
    @UserId INT,@Succeeded BIT,@Now DATETIMEOFFSET(3),@MaxFailures INT=5,@LockoutMinutes INT=15
AS
BEGIN
    SET NOCOUNT ON;
    IF @Now IS NULL OR @MaxFailures<1 OR @LockoutMinutes<1 THROW 51042,'Invalid login result parameters.',1;
    UPDATE iam.Users SET AccessFailedCount=CASE WHEN @Succeeded=1 THEN 0 ELSE AccessFailedCount+1 END,
        LockoutEnd=CASE WHEN @Succeeded=1 THEN NULL WHEN AccessFailedCount+1>=@MaxFailures
                       THEN DATEADD(minute,@LockoutMinutes,@Now) ELSE LockoutEnd END,UpdatedAt=@Now
    WHERE Id=@UserId AND IsActive=1;
END;
GO
-- NormalizedEmail = UPPER(TRIM(email)); normalized phone is digits-only Bangladesh
-- 8801XXXXXXXXX. The host sends normalized phone values.
CREATE OR ALTER PROCEDURE iam.usp_User_Create
    @Email NVARCHAR(254)=NULL,@Phone NVARCHAR(30)=NULL,@DisplayName NVARCHAR(150),
    @PasswordHash NVARCHAR(1000),@Now DATETIMEOFFSET(3)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @Email=NULLIF(TRIM(@Email),N'');
    SET @Phone=NULLIF(TRIM(@Phone),N'');
    IF @Phone IS NOT NULL AND (LEN(@Phone)<>13 OR LEFT(@Phone,4)<>N'8801' OR @Phone COLLATE Latin1_General_100_BIN2 LIKE N'%[^0-9]%')
        THROW 51043,'Phone must be normalized as 8801XXXXXXXXX.',1;
    IF (@Email IS NULL AND @Phone IS NULL) OR @Now IS NULL OR NULLIF(TRIM(@DisplayName),N'') IS NULL OR NULLIF(@PasswordHash,N'') IS NULL
        THROW 51044,'User login, display name, hash and timestamp are required.',1;
    BEGIN TRY
        INSERT iam.Users(Email,NormalizedEmail,Phone,NormalizedPhone,DisplayName,PasswordHash,SecurityStamp,CreatedAt)
        VALUES(@Email,UPPER(@Email),@Phone,@Phone,TRIM(@DisplayName),@PasswordHash,NEWID(),@Now);
        SELECT CONVERT(int,SCOPE_IDENTITY()) AS Id;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() IN(2601,2627) THROW 51030,'A user with this email or phone already exists.',1;
        THROW;
    END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE iam.usp_User_SetPassword @UserId INT,@PasswordHash NVARCHAR(1000),@Now DATETIMEOFFSET(3)
AS
BEGIN
    SET NOCOUNT ON;
    IF NULLIF(@PasswordHash,N'') IS NULL OR @Now IS NULL THROW 51044,'Hash and timestamp are required.',1;
    UPDATE iam.Users SET PasswordHash=@PasswordHash,SecurityStamp=NEWID(),AccessFailedCount=0,LockoutEnd=NULL,UpdatedAt=@Now WHERE Id=@UserId;
END;
GO
-- Used by the self-service change-password endpoint: the caller already holds a session.
CREATE OR ALTER PROCEDURE iam.usp_User_GetPasswordHash @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT PasswordHash FROM iam.Users WHERE Id=@UserId AND IsActive=1;
END;
GO
CREATE OR ALTER PROCEDURE iam.usp_TenantUser_Add @UserId INT,@Now DATETIMEOFFSET(3)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @TenantId INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId')),@Id INT,@Started BIT=0;
    IF ISNULL(@TenantId,0)<=0 OR @Now IS NULL THROW 51045,'Tenant context and timestamp are required.',1;
    IF NOT EXISTS(SELECT 1 FROM iam.Users WHERE Id=@UserId AND IsActive=1) THROW 51046,'Active user not found.',1;
    IF @@TRANCOUNT=0 BEGIN SET @Started=1; BEGIN TRANSACTION; END;
    BEGIN TRY
        SELECT @Id=Id FROM iam.TenantUsers WITH(UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND UserId=@UserId;
        IF @Id IS NULL
        BEGIN
            INSERT iam.TenantUsers(TenantId,UserId,CreatedAt) VALUES(@TenantId,@UserId,@Now);
            SET @Id=CONVERT(int,SCOPE_IDENTITY());
        END
        ELSE UPDATE iam.TenantUsers SET IsActive=1,UpdatedAt=@Now WHERE Id=@Id;
        UPDATE iam.UserTenants SET TenantUserId=@Id,IsActive=1 WHERE UserId=@UserId AND TenantRefId=@TenantId;
        IF @@ROWCOUNT=0 INSERT iam.UserTenants(UserId,TenantRefId,TenantUserId,IsActive) VALUES(@UserId,@TenantId,@Id,1);
        IF @Started=1 COMMIT TRANSACTION;
        SELECT @Id AS TenantUserId;
    END TRY
    BEGIN CATCH
        IF @Started=1 AND XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE iam.usp_TenantUser_SetActive @TenantUserId INT,@IsActive BIT,@Now DATETIMEOFFSET(3)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @TenantId INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId')),@UserId INT,@Started BIT=0;
    IF ISNULL(@TenantId,0)<=0 OR @Now IS NULL OR @IsActive IS NULL THROW 51045,'Tenant context, active flag and timestamp are required.',1;
    IF @@TRANCOUNT=0 BEGIN SET @Started=1; BEGIN TRANSACTION; END;
    BEGIN TRY
        SELECT @UserId=UserId FROM iam.TenantUsers WITH(UPDLOCK,HOLDLOCK) WHERE Id=@TenantUserId;
        IF @UserId IS NULL THROW 51047,'Company membership not found.',1;
        UPDATE iam.TenantUsers SET IsActive=@IsActive,UpdatedAt=@Now WHERE Id=@TenantUserId;
        UPDATE iam.UserTenants SET IsActive=@IsActive WHERE UserId=@UserId AND TenantRefId=@TenantId AND TenantUserId=@TenantUserId;
        IF @@ROWCOUNT<>1 THROW 51048,'Membership discovery index is inconsistent.',1;
        IF @Started=1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @Started=1 AND XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE iam.usp_TenantUser_List
AS
BEGIN
    SET NOCOUNT ON;
    IF ISNULL(TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId')),0)<=0 THROW 51045,'Tenant context is required.',1;
    SELECT tu.Id AS TenantUserId,u.Id AS UserId,u.DisplayName,u.Email,u.Phone,tu.IsActive,u.IsPlatformAdmin
    FROM iam.TenantUsers tu JOIN iam.Users u ON u.Id=tu.UserId;
END;
GO
-- Approved authentication-only exception: the global membership index joins global tenants.
-- Suspended and closed companies are not offered.
CREATE OR ALTER PROCEDURE iam.usp_User_GetTenants @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT t.Id AS TenantId,t.Slug,t.Name,x.TenantUserId FROM iam.UserTenants x
    JOIN platform.Tenants t ON t.Id=x.TenantRefId
    JOIN iam.Users u ON u.Id=x.UserId
    WHERE x.UserId=@UserId AND x.IsActive=1 AND u.IsActive=1 AND t.IsActive=1 AND t.Status=N'Active'
    ORDER BY t.Id;
END;
GO
-- A company administrator (role code 'admin') holds every company permission by rule.
-- Permissions coded 'owner.*' belong to the product owner only.
CREATE OR ALTER FUNCTION iam.fn_UserPermissions(@UserId INT)
RETURNS TABLE AS RETURN
    WITH Caller AS (
        SELECT ISNULL(MAX(CONVERT(int,u.IsPlatformAdmin)),0) AS PlatformAdmin,
               ISNULL((SELECT MAX(1) FROM iam.TenantUsers tu
                       JOIN iam.UserRoles ur ON ur.TenantId=tu.TenantId AND ur.TenantUserId=tu.Id AND ur.IsActive=1
                       JOIN iam.Roles r ON r.TenantId=ur.TenantId AND r.Id=ur.RoleId AND r.IsActive=1
                       WHERE tu.UserId=@UserId AND tu.IsActive=1 AND r.Code=N'admin'),0) AS CompanyAdmin
        FROM iam.Users u WHERE u.Id=@UserId AND u.IsActive=1
    )
    SELECT p.Code FROM iam.Permissions p CROSS JOIN Caller c
    WHERE p.IsActive=1
      AND EXISTS(SELECT 1 FROM iam.TenantUsers tu WHERE tu.UserId=@UserId AND tu.IsActive=1)
      AND (p.Code NOT LIKE N'owner.%' OR c.PlatformAdmin=1)
      AND (c.CompanyAdmin=1 OR EXISTS(
            SELECT 1 FROM iam.TenantUsers tu
            JOIN iam.UserRoles ur ON ur.TenantId=tu.TenantId AND ur.TenantUserId=tu.Id AND ur.IsActive=1
            JOIN iam.Roles r ON r.TenantId=ur.TenantId AND r.Id=ur.RoleId AND r.IsActive=1
            JOIN iam.RolePermissions rp ON rp.TenantId=r.TenantId AND rp.RoleId=r.Id AND rp.IsActive=1
            WHERE tu.UserId=@UserId AND tu.IsActive=1 AND rp.PermissionId=p.Id));
GO
CREATE OR ALTER PROCEDURE iam.usp_User_GetPermissions @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    IF ISNULL(TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId')),0)<=0
        THROW 51020, 'Tenant context is required.', 1;
    SELECT Code FROM iam.fn_UserPermissions(@UserId) ORDER BY Code;
END;
GO
-- Iam owns permission and menu filtering. The host passes the licence's feature codes resolved
-- through Platform.Contracts, so this module never reads another module's tables.
CREATE OR ALTER PROCEDURE iam.usp_User_GetNavigation
    @UserId INT,
    @FeatureCodesJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    IF ISJSON(@FeatureCodesJson)<>1 OR @FeatureCodesJson IS NULL OR LEFT(LTRIM(@FeatureCodesJson),1)<>N'['
        THROW 51021, 'Feature codes must be a JSON array.', 1;
    IF NOT EXISTS(SELECT 1 FROM iam.TenantUsers WHERE UserId=@UserId AND IsActive=1)
        THROW 51024, 'Active company membership is required.', 1;
    DECLARE @Permissions TABLE(Code NVARCHAR(100) PRIMARY KEY);
    INSERT @Permissions(Code) SELECT Code FROM iam.fn_UserPermissions(@UserId);
    ;WITH EligibleMenus AS (
        SELECT Id,ParentId FROM iam.Menus
        WHERE IsActive=1 AND (FeatureCode IS NULL OR FeatureCode IN(SELECT CONVERT(nvarchar(80),value) FROM OPENJSON(@FeatureCodesJson)))
    ), VisibleMenus AS (
        SELECT m.Id,m.ParentId FROM iam.Menus m
        WHERE m.IsActive=1 AND m.Route IS NOT NULL
          AND (m.FeatureCode IS NULL OR EXISTS(SELECT 1 FROM OPENJSON(@FeatureCodesJson) f WHERE f.value=m.FeatureCode))
          AND (m.Code=N'dashboard'
               OR EXISTS(SELECT 1 FROM iam.MenuPermissions mp
                         JOIN iam.Permissions p ON p.Id=mp.PermissionId AND p.IsActive=1
                         JOIN @Permissions up ON up.Code=p.Code WHERE mp.MenuId=m.Id AND mp.IsActive=1))
        UNION ALL
        SELECT parent.Id,parent.ParentId FROM EligibleMenus parent
        JOIN VisibleMenus child ON parent.Id=child.ParentId
    )
    SELECT m.Id,m.ParentId,m.Code,m.Title,m.Icon,m.Route,m.SortOrder,m.FeatureCode
    FROM iam.Menus m
    WHERE EXISTS(SELECT 1 FROM VisibleMenus v WHERE v.Id=m.Id)
    ORDER BY m.SortOrder,m.Id OPTION(MAXRECURSION 100);
END;
GO
