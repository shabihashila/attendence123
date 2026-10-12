:on error exit
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME()<>N'db_feg_hr' THROW 59010,'Run this seed only in db_feg_hr.',1;
DECLARE @User INT,@Name NVARCHAR(150),@Email NVARCHAR(254);
SELECT @User=Id,@Name=DisplayName,@Email=Email FROM iam.Users
WHERE NormalizedEmail=N'ADMINISTRATOR@FEGHR.COM' AND IsPlatformAdmin=1 AND IsActive=1;
IF @User IS NULL THROW 59011,'Seed the active FegHR owner before linking an employee.',1;
DECLARE @Now DATETIMEOFFSET(3)=SYSDATETIMEOFFSET();
DECLARE @Today DATE=CONVERT(date,SWITCHOFFSET(@Now,'+06:00'));
DECLARE @Tenant INT,@Branch INT,@Prior sql_variant=SESSION_CONTEXT(N'FegHR.TenantId');
DECLARE companies CURSOR LOCAL FAST_FORWARD FOR
SELECT t.Id FROM platform.Tenants t JOIN iam.UserTenants u ON u.TenantRefId=t.Id
WHERE u.UserId=@User AND u.IsActive=1 AND t.Slug IN(N'lighthouse-consulting',N'fegtechnova',N'futurepathglobal');
BEGIN TRY
    BEGIN TRANSACTION;
    OPEN companies;
    FETCH NEXT FROM companies INTO @Tenant;
    WHILE @@FETCH_STATUS=0
    BEGIN
        EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Tenant;
        SET @Branch=(SELECT TOP(1) Id FROM platform.Branches WHERE Code=N'HO' AND IsActive=1 ORDER BY Id);
        EXEC org.usp_Employee_CreateForUser @UserId=@User,@FullName=@Name,@Email=@Email,
             @BranchId=@Branch,@JoiningDate=@Today,@Now=@Now;
        FETCH NEXT FROM companies INTO @Tenant;
    END;
    CLOSE companies;
    DEALLOCATE companies;
    COMMIT TRANSACTION;
    EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior;
    THROW;
END CATCH;
GO
