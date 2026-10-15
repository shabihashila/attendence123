:on error exit
USE master;
GO
IF DB_ID(N'FegHR_CompanyAccessDefaults_Test') IS NOT NULL THROW 59000,'Existing test database must be inspected; nothing was replaced.',1;
CREATE DATABASE FegHR_CompanyAccessDefaults_Test;
GO
USE FegHR_CompanyAccessDefaults_Test;
GO
:r 00_Deploy.sql
GO
SET NOCOUNT ON;
DECLARE @Now DATETIMEOFFSET(3)=SYSDATETIMEOFFSET();
INSERT platform.Tenants(Slug,Name,CreatedAt) VALUES(N'qa-main-a',N'Main A',@Now),(N'qa-main-b',N'Main B',@Now),(N'qa-single',N'Single company',@Now);
DECLARE @A INT=(SELECT Id FROM platform.Tenants WHERE Slug=N'qa-main-a'),@B INT=(SELECT Id FROM platform.Tenants WHERE Slug=N'qa-main-b'),@Single INT=(SELECT Id FROM platform.Tenants WHERE Slug=N'qa-single');
INSERT platform.Tenants(Slug,Name,ParentTenantRefId,CreatedAt) VALUES(N'qa-a1',N'A1',@A,@Now),(N'qa-a2',N'A2',@A,@Now),(N'qa-b1',N'B1',@B,@Now);
DECLARE @A1 INT=(SELECT Id FROM platform.Tenants WHERE Slug=N'qa-a1'),@A2 INT=(SELECT Id FROM platform.Tenants WHERE Slug=N'qa-a2'),@B1 INT=(SELECT Id FROM platform.Tenants WHERE Slug=N'qa-b1');
INSERT iam.Users(Email,NormalizedEmail,DisplayName,SecurityStamp,IsPlatformAdmin,CreatedAt)
SELECT v.Email,UPPER(v.Email),v.Name,NEWID(),v.Owner,@Now FROM (VALUES
(N'qa-owner@test.local',N'QA owner',1),(N'qa-admin@test.local',N'QA company admin',0),
(N'qa-child@test.local',N'QA child admin',0),(N'qa-staff@test.local',N'QA staff',0),(N'qa-single@test.local',N'QA single',0))v(Email,Name,Owner);
DECLARE @Owner INT=(SELECT Id FROM iam.Users WHERE Email=N'qa-owner@test.local'),@Admin INT=(SELECT Id FROM iam.Users WHERE Email=N'qa-admin@test.local'),
    @ChildAdmin INT=(SELECT Id FROM iam.Users WHERE Email=N'qa-child@test.local'),@Staff INT=(SELECT Id FROM iam.Users WHERE Email=N'qa-staff@test.local'),@SingleUser INT=(SELECT Id FROM iam.Users WHERE Email=N'qa-single@test.local');
DECLARE @Tid INT,@Uid INT,@RoleCode NVARCHAR(20),@Member INT;
DECLARE @Memberships TABLE(Tid INT,Uid INT,RoleCode NVARCHAR(20));
INSERT @Memberships VALUES(@A,@Owner,N'admin'),(@A,@Admin,N'admin'),(@B,@Admin,N'admin'),(@A1,@ChildAdmin,N'admin'),
    (@A,@Staff,N'employee'),(@A1,@Staff,N'employee'),(@A2,@Staff,N'employee'),(@B1,@Staff,N'employee'),(@Single,@SingleUser,N'employee');
DECLARE tenants CURSOR LOCAL FAST_FORWARD FOR SELECT Id FROM platform.Tenants;
OPEN tenants;FETCH NEXT FROM tenants INTO @Tid;
WHILE @@FETCH_STATUS=0 BEGIN
    EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Tid;
    EXEC iam.usp_Tenant_SeedRoles @Now=@Now;
    FETCH NEXT FROM tenants INTO @Tid;
END;CLOSE tenants;DEALLOCATE tenants;
DECLARE memberships CURSOR LOCAL FAST_FORWARD FOR SELECT Tid,Uid,RoleCode FROM @Memberships;
OPEN memberships;FETCH NEXT FROM memberships INTO @Tid,@Uid,@RoleCode;
DECLARE @Created TABLE(Id INT);
WHILE @@FETCH_STATUS=0 BEGIN
    EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Tid;
    DELETE @Created;INSERT @Created EXEC iam.usp_TenantUser_Add @UserId=@Uid,@Now=@Now;
    SELECT @Member=Id FROM @Created;
    INSERT iam.UserRoles(TenantUserId,RoleId,CreatedAt) SELECT @Member,Id,@Now FROM iam.Roles WHERE Code=@RoleCode;
    FETCH NEXT FROM memberships INTO @Tid,@Uid,@RoleCode;
END;CLOSE memberships;DEALLOCATE memberships;

EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@A;
EXEC sys.sp_set_session_context @key=N'FegHR.UserId',@value=@Admin;
EXECUTE AS USER=N'feghr_app';
IF (SELECT COUNT(*) FROM iam.fn_ManagedCompanies(@Admin))<>3 THROW 59001,'Main-company admin must manage only own company and its two children.',1;
IF EXISTS(SELECT 1 FROM iam.fn_ManagedCompanies(@Admin) WHERE TenantId IN(@B,@B1,@Single)) THROW 59002,'Unrelated companies leaked into the current administration scope.',1;
IF EXISTS(SELECT 1 FROM iam.fn_ManagedCompanies(@Owner)) THROW 59003,'Spoofed actor identity was accepted.',1;
REVERT;
IF EXISTS(SELECT 1 FROM iam.UserTenants WHERE UserId=@Admin AND TenantRefId IN(@A1,@A2)) THROW 59004,'Administration granted implicit business-data access.',1;

EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@B;
EXECUTE AS USER=N'feghr_app';
IF (SELECT COUNT(*) FROM iam.fn_ManagedCompanies(@Admin))<>2 OR EXISTS(SELECT 1 FROM iam.fn_ManagedCompanies(@Admin) WHERE TenantId=@A)
    THROW 59005,'Administration scope must follow the current company, even for a multi-company admin.',1;
REVERT;
EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@A1;
EXEC sys.sp_set_session_context @key=N'FegHR.UserId',@value=@ChildAdmin;
EXECUTE AS USER=N'feghr_app';
IF (SELECT COUNT(*) FROM iam.fn_ManagedCompanies(@ChildAdmin))<>1 OR NOT EXISTS(SELECT 1 FROM iam.fn_ManagedCompanies(@ChildAdmin) WHERE TenantId=@A1)
    THROW 59006,'Child admin reached a parent or sibling company.',1;
REVERT;
EXEC sys.sp_set_session_context @key=N'FegHR.UserId',@value=@Owner;
EXECUTE AS USER=N'feghr_app';
IF (SELECT COUNT(*) FROM iam.fn_ManagedCompanies(@Owner))<>6 THROW 59007,'Owner must manage all active companies, including unrelated and single companies.',1;
REVERT;
EXEC sys.sp_set_session_context @key=N'FegHR.UserId',@value=@Staff;
EXECUTE AS USER=N'feghr_app';
IF EXISTS(SELECT 1 FROM iam.fn_ManagedCompanies(@Staff)) THROW 59008,'An employee can manage company access.',1;
REVERT;

DECLARE @Result TABLE(TenantId INT NULL);
EXECUTE AS USER=N'feghr_app';
INSERT @Result EXEC iam.usp_User_DefaultCompany @UserId=@Staff;
IF NOT EXISTS(SELECT 1 FROM @Result WHERE TenantId=@A1) THROW 59009,'Initial default should choose an accessible child of the first company.',1;
DELETE @Result;INSERT @Result EXEC iam.usp_User_DefaultCompany @UserId=@Staff,@TenantId=@B1,@Save=1;
IF NOT EXISTS(SELECT 1 FROM @Result WHERE TenantId=@B1) THROW 59010,'User cannot choose an accessible company in a different group.',1;
REVERT;
EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@A;
EXEC sys.sp_set_session_context @key=N'FegHR.UserId',@value=@Admin;
EXECUTE AS USER=N'feghr_app';
BEGIN TRY
    EXEC iam.usp_User_DefaultCompany @UserId=@Staff,@TenantId=@B1,@Save=1;
    THROW 59999,'Company admin changed a default outside the current group.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>51181 THROW; END CATCH;
DELETE @Result;INSERT @Result EXEC iam.usp_User_DefaultCompany @UserId=@Staff;
IF NOT EXISTS(SELECT 1 FROM @Result WHERE TenantId=@B1) THROW 59011,'Rejected default update changed the existing preference.',1;
DELETE @Result;INSERT @Result EXEC iam.usp_User_DefaultCompany @UserId=@Staff,@TenantId=@A2,@Save=1;
IF NOT EXISTS(SELECT 1 FROM @Result WHERE TenantId=@A2) THROW 59012,'Parent admin cannot set an accessible child as default.',1;
REVERT;

EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@A2;
SELECT @Member=Id FROM iam.TenantUsers WHERE UserId=@Staff;
EXEC iam.usp_TenantUser_SetActive @TenantUserId=@Member,@IsActive=0,@Now=@Now;
DELETE @Result;INSERT @Result EXEC iam.usp_User_DefaultCompany @UserId=@Staff;
IF NOT EXISTS(SELECT 1 FROM @Result WHERE TenantId=@A1) THROW 59013,'Revoked default did not fall back to an accessible sibling.',1;
EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@A1;
SELECT @Member=Id FROM iam.TenantUsers WHERE UserId=@Staff;
EXEC iam.usp_TenantUser_SetActive @TenantUserId=@Member,@IsActive=0,@Now=@Now;
DELETE @Result;INSERT @Result EXEC iam.usp_User_DefaultCompany @UserId=@Staff;
IF NOT EXISTS(SELECT 1 FROM @Result WHERE TenantId=@A) THROW 59014,'Default did not fall back to the accessible main company.',1;
EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@A;
SELECT @Member=Id FROM iam.TenantUsers WHERE UserId=@Staff;
EXEC iam.usp_TenantUser_SetActive @TenantUserId=@Member,@IsActive=0,@Now=@Now;
DELETE @Result;INSERT @Result EXEC iam.usp_User_DefaultCompany @UserId=@Staff;
IF NOT EXISTS(SELECT 1 FROM @Result WHERE TenantId=@B1) THROW 59015,'Default did not fall back to another accessible company.',1;
DELETE @Result;INSERT @Result EXEC iam.usp_User_DefaultCompany @UserId=@SingleUser;
IF NOT EXISTS(SELECT 1 FROM @Result WHERE TenantId=@Single) THROW 59016,'Single-company user has no correct default.',1;
EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@B1;
SELECT @Member=Id FROM iam.TenantUsers WHERE UserId=@Staff;
EXEC iam.usp_TenantUser_SetActive @TenantUserId=@Member,@IsActive=0,@Now=@Now;
DELETE @Result;INSERT @Result EXEC iam.usp_User_DefaultCompany @UserId=@Staff;
IF NOT EXISTS(SELECT 1 FROM @Result WHERE TenantId IS NULL) THROW 59017,'A user without company access retained an inaccessible default.',1;
IF EXISTS(SELECT 1 FROM iam.UserCompanyPreferences WHERE UserId=@Staff) THROW 59018,'Stale default preference was not cleared.',1;
PRINT N'PASS: owner scope, parent/child administration, unrelated-company isolation, actor spoofing, no implicit memberships, saved defaults, restricted default assignment, revocation fallback and standalone defaults.';
GO
USE master;
GO
DROP DATABASE FegHR_CompanyAccessDefaults_Test;
GO
