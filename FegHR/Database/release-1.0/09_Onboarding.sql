-- Company onboarding. Public registration is the one flow that must create rows in several
-- module schemas atomically, so it is orchestrated here: iam.usp_Company_Register calls each
-- module's own onboarding procedure inside one transaction. Modules still never join each
-- other's tables; they are called through their procedures (ARCHITECTURE.md section 6).

-- Default shift of a new company: Sunday to Thursday 09:00-18:00, 15 minutes of grace.
CREATE OR ALTER PROCEDURE attendance.usp_Tenant_SeedDefaults @Now DATETIMEOFFSET(3),@ActorId INT=NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF ISNULL(TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId')),0)<=0 OR @Now IS NULL
        THROW 52000,'Tenant context and timestamp are required.',1;
    IF NOT EXISTS(SELECT 1 FROM attendance.Settings) INSERT attendance.Settings(CreatedAt,CreatedBy) VALUES(@Now,@ActorId);
    IF NOT EXISTS(SELECT 1 FROM attendance.WorkSchedules)
    BEGIN
        INSERT attendance.WorkSchedules(Name,Description,IsDefault,CreatedAt,CreatedBy)
        VALUES(N'General Shift',N'Sunday to Thursday, 09:00 to 18:00',1,@Now,@ActorId);
        DECLARE @ScheduleId INT=CONVERT(int,SCOPE_IDENTITY());
        INSERT attendance.WorkScheduleDays(WorkScheduleId,DayOfWeek,IsWorkingDay,CreatedAt,CreatedBy)
        SELECT @ScheduleId,d.Day,CASE WHEN d.Day IN(5,6) THEN 0 ELSE 1 END,@Now,@ActorId
        FROM (VALUES(0),(1),(2),(3),(4),(5),(6)) d(Day);
    END;
END;
GO
CREATE OR ALTER PROCEDURE leave.usp_Tenant_SeedDefaults @Now DATETIMEOFFSET(3),@ActorId INT=NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF ISNULL(TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId')),0)<=0 OR @Now IS NULL
        THROW 54001,'Tenant context and timestamp are required.',1;
    INSERT leave.LeaveTypes(Code,Name,YearlyQuota,IsPaid,CreatedAt,CreatedBy)
    SELECT x.Code,x.Name,x.Quota,x.IsPaid,@Now,@ActorId
    FROM (VALUES(N'CL',N'Casual Leave',CONVERT(decimal(5,1),10),1),(N'SL',N'Sick Leave',14,1),
                (N'AL',N'Annual Leave',15,1),(N'UL',N'Unpaid Leave',NULL,0)) x(Code,Name,Quota,IsPaid)
    WHERE NOT EXISTS(SELECT 1 FROM leave.LeaveTypes t WHERE t.Code=x.Code);
END;
GO
-- Gives every product owner an administrator membership in the bound company, so they can
-- switch into it from the top bar (the way DailyCommerce's SuperAdmin reaches every company).
CREATE OR ALTER PROCEDURE iam.usp_PlatformOwner_JoinTenant @Now DATETIMEOFFSET(3)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @TenantId INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId'));
    IF ISNULL(@TenantId,0)<=0 OR @Now IS NULL THROW 51045,'Tenant context and timestamp are required.',1;
    DECLARE @RoleId INT=(SELECT Id FROM iam.Roles WHERE Code=N'admin');
    IF @RoleId IS NULL THROW 51049,'The administrator role is missing.',1;
    INSERT iam.TenantUsers(TenantId,UserId,CreatedAt)
    SELECT @TenantId,u.Id,@Now FROM iam.Users u
    WHERE u.IsPlatformAdmin=1 AND u.IsActive=1
      AND NOT EXISTS(SELECT 1 FROM iam.TenantUsers tu WHERE tu.UserId=u.Id);
    INSERT iam.UserTenants(UserId,TenantRefId,TenantUserId,IsActive)
    SELECT tu.UserId,@TenantId,tu.Id,tu.IsActive FROM iam.TenantUsers tu JOIN iam.Users u ON u.Id=tu.UserId AND u.IsPlatformAdmin=1
    WHERE NOT EXISTS(SELECT 1 FROM iam.UserTenants x WHERE x.UserId=tu.UserId AND x.TenantRefId=@TenantId);
    INSERT iam.UserRoles(TenantUserId,RoleId,CreatedAt)
    SELECT tu.Id,@RoleId,@Now FROM iam.TenantUsers tu JOIN iam.Users u ON u.Id=tu.UserId AND u.IsPlatformAdmin=1 AND u.IsActive=1
    WHERE NOT EXISTS(SELECT 1 FROM iam.UserRoles ur WHERE ur.TenantUserId=tu.Id AND ur.RoleId=@RoleId);
END;
GO
-- Public registration: a new company, its administrator account and a trial licence, atomically.
-- The administrator is also the company's first employee, so attendance and leave work at once.
--   51030 the email or phone already has an account      51050 registration is closed
CREATE OR ALTER PROCEDURE iam.usp_Company_Register
    @CompanyName NVARCHAR(200),@BaseSlug NVARCHAR(70),@CompanyAddress NVARCHAR(500)=NULL,@CompanyPhone NVARCHAR(30)=NULL,
    @CompanyEmail NVARCHAR(254)=NULL,@TimeZoneId NVARCHAR(100)=N'Asia/Dhaka',
    @AdminName NVARCHAR(150),@AdminEmail NVARCHAR(254),@AdminPhone NVARCHAR(30)=NULL,@PasswordHash NVARCHAR(1000),
    @Today DATE,@Now DATETIMEOFFSET(3)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @AdminEmail=NULLIF(LOWER(LTRIM(RTRIM(@AdminEmail))),N'');
    SET @AdminPhone=NULLIF(LTRIM(RTRIM(@AdminPhone)),N'');
    SET @AdminName=NULLIF(LTRIM(RTRIM(@AdminName)),N'');
    IF @AdminEmail IS NULL OR @AdminEmail NOT LIKE N'_%@_%._%' OR @AdminEmail LIKE N'% %' THROW 51051,'A valid email address is required.',1;
    IF @AdminName IS NULL OR @Now IS NULL OR @Today IS NULL THROW 51051,'Your name and the timestamp are required.',1;
    IF LEN(ISNULL(@PasswordHash,N''))<>60 OR LEFT(@PasswordHash,4) NOT IN(N'$2a$',N'$2b$',N'$2y$') THROW 51051,'A BCrypt password hash is required.',1;
    IF @AdminPhone IS NOT NULL AND (LEN(@AdminPhone)<>13 OR LEFT(@AdminPhone,4)<>N'8801' OR @AdminPhone COLLATE Latin1_General_100_BIN2 LIKE N'%[^0-9]%')
        THROW 51043,'Phone must be normalized as 8801XXXXXXXXX.',1;
    IF NOT EXISTS(SELECT 1 FROM platform.LicensePolicy WHERE Id=1 AND RegistrationOpen=1)
        THROW 51050,'Registration is closed at the moment. Please contact us.',1;

    DECLARE @Prior sql_variant=SESSION_CONTEXT(N'FegHR.TenantId');
    DECLARE @TenantId INT,@Slug NVARCHAR(80),@TrialEndsAt DATETIMEOFFSET(3),@UserId INT,@MemberId INT,@RoleId INT,@BranchId INT;
    BEGIN TRY
        BEGIN TRANSACTION;
        IF EXISTS(SELECT 1 FROM iam.Users WITH(UPDLOCK,HOLDLOCK)
                  WHERE NormalizedEmail=UPPER(@AdminEmail) OR (@AdminPhone IS NOT NULL AND NormalizedPhone=@AdminPhone))
            THROW 51030,'An account with this email or phone already exists. Please sign in.',1;

        EXEC platform.usp_Tenant_Provision @Name=@CompanyName,@BaseSlug=@BaseSlug,@Address=@CompanyAddress,@Phone=@CompanyPhone,
             @Email=@CompanyEmail,@TimeZoneId=@TimeZoneId,@Origin=N'Registration',@Now=@Now,
             @TenantId=@TenantId OUTPUT,@Slug=@Slug OUTPUT,@TrialEndsAt=@TrialEndsAt OUTPUT;
        -- The session is now bound to the new company (set by usp_Tenant_Provision).

        INSERT iam.Users(Email,NormalizedEmail,Phone,NormalizedPhone,DisplayName,PasswordHash,SecurityStamp,CreatedAt)
        VALUES(@AdminEmail,UPPER(@AdminEmail),@AdminPhone,@AdminPhone,@AdminName,@PasswordHash,NEWID(),@Now);
        SET @UserId=CONVERT(int,SCOPE_IDENTITY());

        EXEC iam.usp_Tenant_SeedRoles @Now=@Now;
        SELECT @RoleId=Id FROM iam.Roles WHERE Code=N'admin';
        INSERT iam.TenantUsers(TenantId,UserId,CreatedAt,CreatedBy) VALUES(@TenantId,@UserId,@Now,@UserId);
        SET @MemberId=CONVERT(int,SCOPE_IDENTITY());
        INSERT iam.UserTenants(UserId,TenantRefId,TenantUserId,IsActive) VALUES(@UserId,@TenantId,@MemberId,1);
        INSERT iam.UserRoles(TenantUserId,RoleId,CreatedAt,CreatedBy) VALUES(@MemberId,@RoleId,@Now,@UserId);

        SELECT @BranchId=Id FROM platform.Branches WHERE Code=N'HO';
        EXEC org.usp_Employee_CreateForUser @UserId=@UserId,@FullName=@AdminName,@Phone=@AdminPhone,@Email=@AdminEmail,
             @BranchId=@BranchId,@JoiningDate=@Today,@Now=@Now;
        EXEC attendance.usp_Tenant_SeedDefaults @Now=@Now,@ActorId=@UserId;
        EXEC leave.usp_Tenant_SeedDefaults @Now=@Now,@ActorId=@UserId;
        EXEC iam.usp_PlatformOwner_JoinTenant @Now=@Now;

        COMMIT TRANSACTION;
        EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior;
        IF ERROR_NUMBER() IN(2601,2627) THROW 51030,'An account with this email or phone already exists. Please sign in.',1;
        THROW;
    END CATCH;
    SELECT @TenantId AS TenantId,@Slug AS Slug,@UserId AS UserId,@MemberId AS TenantUserId,@TrialEndsAt AS TrialEndsAt;
END;
GO
