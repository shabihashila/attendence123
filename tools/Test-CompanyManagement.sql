-- Run with sqlcmd from FegHR/Database/release-1.0 and SeedTimestamp set.
-- Uses a dedicated disposable database; refuses to touch an existing database of that name.
:on error exit
USE master;
GO
IF DB_ID(N'FegHR_CompanyManagement_Test') IS NOT NULL THROW 59000,'Test database already exists; inspect it before retrying.',1;
CREATE DATABASE FegHR_CompanyManagement_Test;
GO
USE FegHR_CompanyManagement_Test;
GO
:r 00_Deploy.sql
-- Reapply the new migration to check repeat deployment.
:r 13_CompanyManagement.sql
GO
SET NOCOUNT ON;
DECLARE @Now DATETIMEOFFSET(3)=SYSDATETIMEOFFSET();
INSERT iam.Users(Email,NormalizedEmail,DisplayName,PasswordHash,SecurityStamp,IsPlatformAdmin,CreatedAt)
VALUES(N'owner@example.test',N'OWNER@EXAMPLE.TEST',N'Test owner',NULL,NEWID(),1,@Now);
DECLARE @Actor INT=CONVERT(int,SCOPE_IDENTITY());
INSERT platform.Tenants(Slug,Name,MaxChildConcerns,CreatedAt) VALUES(N'main',N'Main',1,@Now),(N'other',N'Other',NULL,@Now),(N'zero',N'Zero',0,@Now);
DECLARE @Main INT=(SELECT Id FROM platform.Tenants WHERE Slug=N'main'),@Other INT=(SELECT Id FROM platform.Tenants WHERE Slug=N'other'),@Zero INT=(SELECT Id FROM platform.Tenants WHERE Slug=N'zero');
DECLARE @Version BINARY(8)=(SELECT RowVersion FROM platform.Tenants WHERE Id=@Main);
DECLARE @OriginalVersion BINARY(8)=@Version;
BEGIN TRY
    EXEC platform.usp_Owner_Tenant_Save @TenantId=@Main,@Name=N'Forbidden',@RowVersion=@Version,@Now=@Now,@ActorId=@Actor;
    THROW 59001,'Authorization test failed.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>51020 THROW; END CATCH;
EXEC sys.sp_set_session_context @key=N'FegHR.IsPlatformAdmin',@value=1;
EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Main;
EXEC platform.usp_Owner_Tenant_Save @TenantId=@Main,@Name=N'Renamed main',@MaxChildConcerns=1,@RowVersion=@Version,@Now=@Now,@ActorId=@Actor;
IF NOT EXISTS(SELECT 1 FROM platform.Tenants WHERE Id=@Main AND Name=N'Renamed main' AND Slug=N'main') THROW 59002,'Stable ID and slug test failed.',1;
BEGIN TRY
    EXEC platform.usp_Owner_Tenant_Save @TenantId=@Main,@Name=N'Stale',@RowVersion=@OriginalVersion,@Now=@Now,@ActorId=@Actor;
    THROW 59003,'Stale edit test failed.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>51062 THROW; END CATCH;
BEGIN TRY
    EXEC platform.usp_Owner_Concern_Create @ParentTenantId=@Zero,@Name=N'Blocked',@BaseSlug=N'blocked',@Now=@Now,@ActorId=@Actor;
    THROW 59004,'Zero limit test failed.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>51066 THROW; END CATCH;
EXEC platform.usp_Owner_Concern_Create @ParentTenantId=@Main,@Name=N'Child',@BaseSlug=N'child',@Now=@Now,@ActorId=@Actor;
DECLARE @Child INT=(SELECT Id FROM platform.Tenants WHERE Slug=N'child');
IF TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId'))<>@Main THROW 59005,'Tenant context was not restored.',1;
EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Child;
IF NOT EXISTS(SELECT 1 FROM iam.TenantUsers WHERE UserId=@Actor) OR NOT EXISTS(SELECT 1 FROM attendance.Settings) OR NOT EXISTS(SELECT 1 FROM leave.LeaveTypes)
    THROW 59006,'Child onboarding failed.',1;
IF NOT EXISTS(SELECT 1 FROM platform.Licenses WHERE TenantRefId=@Child) THROW 59007,'Child licence missing.',1;
BEGIN TRY
    EXEC platform.usp_Owner_Concern_Create @ParentTenantId=@Main,@Name=N'Overflow',@BaseSlug=N'overflow',@Now=@Now,@ActorId=@Actor;
    THROW 59008,'Capacity test failed.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>51066 THROW; END CATCH;
SET @Version=(SELECT RowVersion FROM platform.Tenants WHERE Id=@Other);
BEGIN TRY
    EXEC platform.usp_Owner_Tenant_Save @TenantId=@Other,@Name=N'Other',@ParentTenantRefId=@Main,@RowVersion=@Version,@Now=@Now,@ActorId=@Actor;
    THROW 59009,'Existing company attachment bypassed capacity.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>51066 THROW; END CATCH;
SET @Version=(SELECT RowVersion FROM platform.Tenants WHERE Id=@Main);
BEGIN TRY
    EXEC platform.usp_Owner_Tenant_Save @TenantId=@Main,@Name=N'Main',@MaxChildConcerns=0,@RowVersion=@Version,@Now=@Now,@ActorId=@Actor;
    THROW 59010,'Limit below count was accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>51063 THROW; END CATCH;
BEGIN TRY
    EXEC platform.usp_Owner_Tenant_Save @TenantId=@Main,@Name=N'Main',@ParentTenantRefId=@Child,@MaxChildConcerns=1,@RowVersion=@Version,@Now=@Now,@ActorId=@Actor;
    THROW 59011,'Circular hierarchy was accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>51064 THROW; END CATCH;
BEGIN TRY
    EXEC platform.usp_Owner_Concern_Create @ParentTenantId=@Child,@Name=N'Nested',@BaseSlug=N'nested',@Now=@Now,@ActorId=@Actor;
    THROW 59012,'Nested hierarchy was accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>51065 THROW; END CATCH;
EXEC platform.usp_Owner_Tenant_Save @TenantId=@Main,@Name=N'Renamed main',@RowVersion=@Version,@Now=@Now,@ActorId=@Actor;
EXEC platform.usp_Owner_Concern_Create @ParentTenantId=@Main,@Name=N'Second child',@BaseSlug=N'second',@Now=@Now,@ActorId=@Actor;
SET @Version=(SELECT RowVersion FROM platform.Tenants WHERE Id=@Child);
EXEC platform.usp_Owner_Tenant_Save @TenantId=@Child,@Name=N'Renamed child',@ParentTenantRefId=@Other,@RowVersion=@Version,@Now=@Now,@ActorId=@Actor;
IF NOT EXISTS(SELECT 1 FROM platform.Tenants WHERE Id=@Child AND ParentTenantRefId=@Other AND Slug=N'child') THROW 59013,'Reparent test failed.',1;
UPDATE platform.Tenants SET Status=N'Suspended' WHERE Id=@Other;
SET @Version=(SELECT RowVersion FROM platform.Tenants WHERE Id=@Child);
EXEC platform.usp_Owner_Tenant_Save @TenantId=@Child,@Name=N'Renamed under suspended parent',@ParentTenantRefId=@Other,@RowVersion=@Version,@Now=@Now,@ActorId=@Actor;
SET @Version=(SELECT RowVersion FROM platform.Tenants WHERE Id=@Child);
EXEC platform.usp_Owner_Tenant_Save @TenantId=@Child,@Name=N'Standalone',@RowVersion=@Version,@Now=@Now,@ActorId=@Actor;
IF EXISTS(SELECT 1 FROM platform.Tenants WHERE Id=@Child AND ParentTenantRefId IS NOT NULL) THROW 59014,'Detach test failed.',1;
PRINT N'PASS: authorization, idempotent migration, rename, stable ID/slug, stale edits, zero/unlimited limits, capacity, attachment, nesting, context restoration, onboarding, reparent and detach.';
GO
USE master;
GO
DROP DATABASE FegHR_CompanyManagement_Test;
GO
