-- platform: companies (tenants), branches, settings, plans, licences and product branding.
-- Tenant tables carry TenantId and get a row-level security policy. Control-plane tables
-- (Tenants, TenantHosts, Features, Plans, LicensePolicy, Licenses, PlatformBranding) are global:
-- they are reached through the procedures below, which check the caller's context.
IF OBJECT_ID(N'platform.Tenants', N'U') IS NULL
BEGIN
    CREATE TABLE [platform].[Tenants](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Tenants PRIMARY KEY,
        Slug NVARCHAR(80) NOT NULL,
        Name NVARCHAR(200) NOT NULL,
        Address NVARCHAR(500) NULL,
        Phone NVARCHAR(30) NULL,
        Email NVARCHAR(254) NULL,
        Status NVARCHAR(20) NOT NULL CONSTRAINT DF_Tenants_Status DEFAULT N'Active',
        -- 'Registration' for public sign-up, 'Operator' when the product owner created it.
        Origin NVARCHAR(20) NOT NULL CONSTRAINT DF_Tenants_Origin DEFAULT N'Registration',
        CONSTRAINT UQ_Tenants_Slug UNIQUE(Slug),
        CONSTRAINT CK_Tenants_Status CHECK(Status IN (N'Active',N'Suspended',N'Closed')),
        CONSTRAINT CK_Tenants_Origin CHECK(Origin IN (N'Registration',N'Operator')),
        IsActive BIT NOT NULL CONSTRAINT DF_Tenants_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL
    );
END;
GO
IF OBJECT_ID(N'platform.TenantHosts', N'U') IS NULL
BEGIN
    CREATE TABLE [platform].[TenantHosts](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TenantHosts PRIMARY KEY,
        TenantRefId INT NOT NULL,
        Host NVARCHAR(253) NOT NULL,
        IsPrimary BIT NOT NULL CONSTRAINT DF_TenantHosts_IsPrimary DEFAULT 0,
        CONSTRAINT FK_TenantHosts_Tenants FOREIGN KEY(TenantRefId) REFERENCES platform.Tenants(Id),
        CONSTRAINT UQ_TenantHosts_Host UNIQUE(Host),
        IsActive BIT NOT NULL CONSTRAINT DF_TenantHosts_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL
    );
END;
GO
IF OBJECT_ID(N'platform.Features', N'U') IS NULL
BEGIN
    CREATE TABLE [platform].[Features](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Features PRIMARY KEY,
        Code NVARCHAR(80) NOT NULL,
        Name NVARCHAR(150) NOT NULL,
        CONSTRAINT UQ_Features_Code UNIQUE(Code),
        IsActive BIT NOT NULL CONSTRAINT DF_Features_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL
    );
END;
GO
IF OBJECT_ID(N'platform.Plans', N'U') IS NULL
BEGIN
    CREATE TABLE [platform].[Plans](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Plans PRIMARY KEY,
        Code NVARCHAR(60) NOT NULL,
        Name NVARCHAR(150) NOT NULL,
        MonthlyPrice DECIMAL(18,2) NOT NULL CONSTRAINT DF_Plans_MonthlyPrice DEFAULT 0,
        FeatureCodesJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_Plans_FeatureCodesJson DEFAULT N'[]',
        MaxEmployees INT NULL,   -- NULL = unlimited
        CONSTRAINT UQ_Plans_Code UNIQUE(Code),
        CONSTRAINT CK_Plans_MonthlyPrice CHECK(MonthlyPrice>=0),
        CONSTRAINT CK_Plans_MaxEmployees CHECK(MaxEmployees IS NULL OR MaxEmployees>0),
        CONSTRAINT CK_Plans_FeatureCodesJson CHECK(ISJSON(FeatureCodesJson)=1),
        IsActive BIT NOT NULL CONSTRAINT DF_Plans_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL
    );
END;
GO
-- One row: the licence a public registration receives. The product owner edits it.
IF OBJECT_ID(N'platform.LicensePolicy', N'U') IS NULL
BEGIN
    CREATE TABLE [platform].[LicensePolicy](
        Id INT NOT NULL CONSTRAINT PK_LicensePolicy PRIMARY KEY,
        TrialDays INT NOT NULL,
        TrialPlanId INT NOT NULL,
        RegistrationOpen BIT NOT NULL CONSTRAINT DF_LicensePolicy_RegistrationOpen DEFAULT 1,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT CK_LicensePolicy_Single CHECK(Id=1),
        CONSTRAINT CK_LicensePolicy_TrialDays CHECK(TrialDays BETWEEN 1 AND 3650),
        CONSTRAINT FK_LicensePolicy_Plans FOREIGN KEY(TrialPlanId) REFERENCES platform.Plans(Id)
    );
END;
GO
-- A company's right to use the product for a period. Rows are never edited into a different
-- period: an extension is a new row, a withdrawal sets Status = Revoked.
IF OBJECT_ID(N'platform.Licenses', N'U') IS NULL
BEGIN
    CREATE TABLE [platform].[Licenses](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Licenses PRIMARY KEY,
        TenantRefId INT NOT NULL,
        PlanId INT NOT NULL,
        Kind NVARCHAR(20) NOT NULL,
        StartsAt DATETIMEOFFSET(3) NOT NULL,
        EndsAt DATETIMEOFFSET(3) NULL,   -- NULL = no expiry
        LicenseKey NVARCHAR(100) NULL,
        Note NVARCHAR(500) NULL,
        Status NVARCHAR(20) NOT NULL CONSTRAINT DF_Licenses_Status DEFAULT N'Active',
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_Licenses_Tenants FOREIGN KEY(TenantRefId) REFERENCES platform.Tenants(Id),
        CONSTRAINT FK_Licenses_Plans FOREIGN KEY(PlanId) REFERENCES platform.Plans(Id),
        CONSTRAINT CK_Licenses_Kind CHECK(Kind IN(N'Trial',N'Paid',N'Complimentary')),
        CONSTRAINT CK_Licenses_Status CHECK(Status IN(N'Active',N'Revoked')),
        CONSTRAINT CK_Licenses_Dates CHECK(EndsAt IS NULL OR EndsAt>StartsAt)
    );
    CREATE INDEX IX_Licenses_TenantRefId_Status ON platform.Licenses(TenantRefId,Status,StartsAt) INCLUDE(EndsAt,PlanId,Kind);
END;
GO
-- One row: how the product presents itself on the landing, registration and sign-in pages.
-- NameStyleJson holds the brand name as styled runs (data, never HTML). Images live in the row,
-- so the public pages need no storage service.
IF OBJECT_ID(N'platform.PlatformBranding', N'U') IS NULL
BEGIN
    CREATE TABLE [platform].[PlatformBranding](
        Id INT NOT NULL CONSTRAINT PK_PlatformBranding PRIMARY KEY,
        BrandName NVARCHAR(60) NOT NULL,
        NameStyleJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_PlatformBranding_NameStyleJson DEFAULT N'[]',
        Tagline NVARCHAR(200) NULL,
        PrimaryColour CHAR(7) NULL,
        AccentColour CHAR(7) NULL,
        SupportEmail NVARCHAR(254) NULL,
        SupportPhone NVARCHAR(30) NULL,
        FooterText NVARCHAR(300) NULL,
        LogoBytes VARBINARY(MAX) NULL,
        LogoContentType NVARCHAR(30) NULL,
        FaviconBytes VARBINARY(MAX) NULL,
        FaviconContentType NVARCHAR(30) NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT CK_PlatformBranding_Single CHECK(Id=1),
        CONSTRAINT CK_PlatformBranding_Name CHECK(LEN(LTRIM(RTRIM(BrandName)))>0),
        CONSTRAINT CK_PlatformBranding_NameStyleJson CHECK(ISJSON(NameStyleJson)=1),
        CONSTRAINT CK_PlatformBranding_Logo CHECK((LogoBytes IS NULL AND LogoContentType IS NULL)
            OR (LogoBytes IS NOT NULL AND DATALENGTH(LogoBytes) BETWEEN 1 AND 1048576
                AND LogoContentType IN(N'image/png',N'image/jpeg',N'image/webp',N'image/svg+xml'))),
        CONSTRAINT CK_PlatformBranding_Favicon CHECK((FaviconBytes IS NULL AND FaviconContentType IS NULL)
            OR (FaviconBytes IS NOT NULL AND DATALENGTH(FaviconBytes) BETWEEN 1 AND 262144
                AND FaviconContentType IN(N'image/png',N'image/x-icon',N'image/vnd.microsoft.icon',N'image/webp')))
    );
END;
GO
IF NOT EXISTS(SELECT 1 FROM platform.PlatformBranding WHERE Id=1)
    INSERT platform.PlatformBranding(Id,BrandName,Tagline,PrimaryColour,AccentColour)
    VALUES(1,N'FegHR',N'People, attendance and leave in one place.','#1863DC','#0F9F8F');
GO
IF OBJECT_ID(N'platform.Branches', N'U') IS NULL
BEGIN
    CREATE TABLE [platform].[Branches](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Branches PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_Branches_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        Code NVARCHAR(40) NOT NULL,
        Name NVARCHAR(150) NOT NULL,
        Address NVARCHAR(500) NULL,
        Phone NVARCHAR(30) NULL,
        CONSTRAINT UQ_Branches_TenantId_Code UNIQUE(TenantId,Code),
        IsActive BIT NOT NULL CONSTRAINT DF_Branches_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_Branches_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id),
        CONSTRAINT UQ_Branches_TenantId_Id UNIQUE(TenantId, Id)
    );
END;
GO
IF OBJECT_ID(N'platform.TenantSettings', N'U') IS NULL
BEGIN
    CREATE TABLE [platform].[TenantSettings](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TenantSettings PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_TenantSettings_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        CurrencyCode CHAR(3) NOT NULL CONSTRAINT DF_TenantSettings_CurrencyCode DEFAULT 'BDT',
        TimeZoneId NVARCHAR(100) NOT NULL CONSTRAINT DF_TenantSettings_TimeZoneId DEFAULT N'Asia/Dhaka',
        BrandingJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_TenantSettings_BrandingJson DEFAULT N'{}',
        CONSTRAINT UQ_TenantSettings_TenantId UNIQUE(TenantId),
        CONSTRAINT CK_TenantSettings_BrandingJson CHECK(ISJSON(BrandingJson)=1),
        IsActive BIT NOT NULL CONSTRAINT DF_TenantSettings_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_TenantSettings_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id),
        CONSTRAINT UQ_TenantSettings_TenantId_Id UNIQUE(TenantId, Id)
    );
END;
GO
EXEC security.usp_ApplyTenantPolicy @Schema=N'platform';
GO
CREATE OR ALTER PROCEDURE platform.usp_Tenant_ResolveByHost
    @Host NVARCHAR(253)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Normalized NVARCHAR(253)=LOWER(LTRIM(RTRIM(@Host)));
    SELECT t.Id AS TenantId,t.Slug,t.Name
    FROM platform.Tenants t
    WHERE t.IsActive=1 AND t.Status=N'Active'
      AND (t.Slug=@Normalized OR EXISTS(
          SELECT 1 FROM platform.TenantHosts h WHERE h.TenantRefId=t.Id AND h.Host=@Normalized AND h.IsActive=1));
END;
GO
CREATE OR ALTER PROCEDURE platform.usp_Tenant_GetCurrent
AS
BEGIN
    SET NOCOUNT ON;
    SELECT t.Id,t.Slug,t.Name,s.CurrencyCode,s.TimeZoneId FROM platform.Tenants t
    JOIN platform.TenantSettings s ON s.TenantId=t.Id
    WHERE t.Id=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId')) AND t.IsActive=1 AND t.Status=N'Active' AND s.IsActive=1;
END;
GO
-- The licence in force for a tenant at @Now: an active row covering @Now. A paid licence wins
-- over a complimentary one, which wins over the trial; within a kind the longest runs.
CREATE OR ALTER FUNCTION platform.fn_EffectiveLicense(@TenantId INT,@Now DATETIMEOFFSET(3))
RETURNS TABLE AS RETURN
    SELECT TOP(1) l.Id,l.PlanId,l.Kind,l.StartsAt,l.EndsAt,l.LicenseKey
    FROM platform.Licenses l
    WHERE l.TenantRefId=@TenantId AND l.Status=N'Active' AND l.StartsAt<=@Now AND (l.EndsAt IS NULL OR l.EndsAt>@Now)
    ORDER BY CASE l.Kind WHEN N'Paid' THEN 0 WHEN N'Complimentary' THEN 1 ELSE 2 END,
             CASE WHEN l.EndsAt IS NULL THEN 0 ELSE 1 END, l.EndsAt DESC, l.Id DESC;
GO
-- Licence state of the bound company. Returns one row even when nothing is in force, with the
-- most recent licence so the UI can say when it expired.
CREATE OR ALTER PROCEDURE platform.usp_License_GetStatus
    @Now DATETIMEOFFSET(3)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @TenantId INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId'));
    IF ISNULL(@TenantId,0)<=0 OR @Now IS NULL THROW 51010,'Tenant context and timestamp are required.',1;
    SELECT t.Id AS TenantId,t.Status AS TenantStatus,
           CONVERT(bit,CASE WHEN e.Id IS NOT NULL AND t.Status=N'Active' AND t.IsActive=1 THEN 1 ELSE 0 END) AS IsLicensed,
           COALESCE(e.Kind,x.Kind) AS Kind,p.Code AS PlanCode,p.Name AS PlanName,
           COALESCE(e.StartsAt,x.StartsAt) AS StartsAt,COALESCE(e.EndsAt,x.EndsAt) AS EndsAt
    FROM platform.Tenants t
    OUTER APPLY platform.fn_EffectiveLicense(t.Id,@Now) e
    OUTER APPLY (SELECT TOP(1) l.PlanId,l.Kind,l.StartsAt,l.EndsAt FROM platform.Licenses l
                 WHERE e.Id IS NULL AND l.TenantRefId=t.Id AND l.Status=N'Active' ORDER BY l.EndsAt DESC,l.Id DESC) x
    LEFT JOIN platform.Plans p ON p.Id=COALESCE(e.PlanId,x.PlanId)
    WHERE t.Id=@TenantId;
END;
GO
-- Feature codes of the licence in force; nothing when the company is unlicensed.
CREATE OR ALTER PROCEDURE platform.usp_Tenant_GetFeatureCodes
    @Now DATETIMEOFFSET(3)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @TenantId INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId'));
    IF ISNULL(@TenantId,0)<=0 OR @Now IS NULL THROW 51010,'Tenant context and timestamp are required.',1;
    SELECT DISTINCT f.Code
    FROM platform.fn_EffectiveLicense(@TenantId,@Now) e
    JOIN platform.Plans p ON p.Id=e.PlanId AND p.IsActive=1
    CROSS APPLY OPENJSON(p.FeatureCodesJson) j
    JOIN platform.Features f ON f.Code=j.value AND f.IsActive=1
    ORDER BY f.Code;
END;
GO
-- Creates a company with its settings, head-office branch and trial licence, then binds the
-- session to it so the caller can add the company's first rows. The caller owns the transaction
-- and restores the prior session tenant. Not callable by the application role directly.
CREATE OR ALTER PROCEDURE platform.usp_Tenant_Provision
    @Name NVARCHAR(200),@BaseSlug NVARCHAR(70),@Address NVARCHAR(500)=NULL,@Phone NVARCHAR(30)=NULL,
    @Email NVARCHAR(254)=NULL,@TimeZoneId NVARCHAR(100)=N'Asia/Dhaka',@Origin NVARCHAR(20)=N'Registration',
    @Now DATETIMEOFFSET(3),@TenantId INT OUTPUT,@Slug NVARCHAR(80) OUTPUT,@TrialEndsAt DATETIMEOFFSET(3) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @Name=NULLIF(LTRIM(RTRIM(@Name)),N'');
    SET @BaseSlug=LOWER(NULLIF(LTRIM(RTRIM(@BaseSlug)),N''));
    IF @Name IS NULL OR @BaseSlug IS NULL OR @Now IS NULL THROW 51011,'Company name, slug and timestamp are required.',1;
    IF @BaseSlug COLLATE Latin1_General_100_BIN2 LIKE N'%[^a-z0-9-]%' OR LEFT(@BaseSlug,1)=N'-'
        THROW 51012,'Slug may contain only a-z, 0-9 and hyphens.',1;
    IF @@TRANCOUNT=0 THROW 51013,'Company provisioning must run inside the caller''s transaction.',1;
    DECLARE @TrialDays INT,@PlanId INT,@Suffix INT=1;
    SELECT @TrialDays=TrialDays,@PlanId=TrialPlanId FROM platform.LicensePolicy WHERE Id=1;
    IF @PlanId IS NULL THROW 51014,'The licence policy is not configured.',1;
    SET @Slug=@BaseSlug;
    WHILE EXISTS(SELECT 1 FROM platform.Tenants WITH(UPDLOCK,HOLDLOCK) WHERE Slug=@Slug)
    BEGIN
        SET @Suffix+=1;
        IF @Suffix>500 THROW 51015,'Could not allocate a company address. Try another name.',1;
        SET @Slug=CONCAT(@BaseSlug,N'-',@Suffix);
    END;
    INSERT platform.Tenants(Slug,Name,Address,Phone,Email,Origin,CreatedAt)
    VALUES(@Slug,@Name,NULLIF(LTRIM(RTRIM(@Address)),N''),NULLIF(LTRIM(RTRIM(@Phone)),N''),NULLIF(LTRIM(RTRIM(@Email)),N''),@Origin,@Now);
    SET @TenantId=CONVERT(int,SCOPE_IDENTITY());
    EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@TenantId;
    INSERT platform.TenantSettings(TimeZoneId,CreatedAt) VALUES(ISNULL(NULLIF(@TimeZoneId,N''),N'Asia/Dhaka'),@Now);
    INSERT platform.Branches(Code,Name,Address,Phone,CreatedAt) VALUES(N'HO',N'Head Office',NULLIF(LTRIM(RTRIM(@Address)),N''),NULLIF(LTRIM(RTRIM(@Phone)),N''),@Now);
    SET @TrialEndsAt=DATEADD(day,@TrialDays,@Now);
    INSERT platform.Licenses(TenantRefId,PlanId,Kind,StartsAt,EndsAt,Note,CreatedAt)
    VALUES(@TenantId,@PlanId,N'Trial',@Now,@TrialEndsAt,N'Registration trial',@Now);
END;
GO
-- ---------------------------------------------------------------------------------------------
-- Product owner console. Every procedure requires the platform-administrator session flag, which
-- the API sets only from a verified token of a user with iam.Users.IsPlatformAdmin = 1.
-- ---------------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE platform.usp_Owner_Tenant_List
    @Now DATETIMEOFFSET(3),@Search NVARCHAR(100)=NULL,@Skip INT=0,@Take INT=50
AS
BEGIN
    SET NOCOUNT ON;
    IF ISNULL(TRY_CONVERT(bit,SESSION_CONTEXT(N'FegHR.IsPlatformAdmin')),0)<>1
        THROW 51020,'Product owner context is required.',1;
    SET @Search=NULLIF(LTRIM(RTRIM(@Search)),N'');
    SELECT t.Id,t.Slug,t.Name,t.Phone,t.Email,t.Status,t.Origin,t.CreatedAt,t.RowVersion,
           CONVERT(bit,CASE WHEN e.Id IS NULL THEN 0 ELSE 1 END) AS IsLicensed,
           e.Kind AS LicenseKind,p.Name AS PlanName,e.EndsAt AS LicenseEndsAt,
           COUNT(*) OVER() AS TotalCount
    FROM platform.Tenants t
    OUTER APPLY platform.fn_EffectiveLicense(t.Id,@Now) e
    LEFT JOIN platform.Plans p ON p.Id=e.PlanId
    WHERE @Search IS NULL OR t.Name LIKE N'%'+@Search+N'%' OR t.Slug LIKE N'%'+@Search+N'%' OR t.Email LIKE N'%'+@Search+N'%'
    ORDER BY t.Id DESC
    OFFSET CASE WHEN @Skip<0 THEN 0 ELSE @Skip END ROWS FETCH NEXT CASE WHEN @Take BETWEEN 1 AND 200 THEN @Take ELSE 50 END ROWS ONLY;
END;
GO
CREATE OR ALTER PROCEDURE platform.usp_Owner_Tenant_SetStatus
    @TenantId INT,@Status NVARCHAR(20),@Now DATETIMEOFFSET(3),@ActorId INT
AS
BEGIN
    SET NOCOUNT ON;
    IF ISNULL(TRY_CONVERT(bit,SESSION_CONTEXT(N'FegHR.IsPlatformAdmin')),0)<>1
        THROW 51020,'Product owner context is required.',1;
    IF @Status NOT IN(N'Active',N'Suspended',N'Closed') OR @Now IS NULL THROW 51021,'Unknown company status.',1;
    UPDATE platform.Tenants SET Status=@Status,UpdatedAt=@Now,UpdatedBy=@ActorId WHERE Id=@TenantId;
    IF @@ROWCOUNT=0 THROW 51022,'Company not found.',1;
END;
GO
CREATE OR ALTER PROCEDURE platform.usp_Owner_License_List
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;
    IF ISNULL(TRY_CONVERT(bit,SESSION_CONTEXT(N'FegHR.IsPlatformAdmin')),0)<>1
        THROW 51020,'Product owner context is required.',1;
    SELECT l.Id,l.TenantRefId AS TenantId,p.Code AS PlanCode,p.Name AS PlanName,l.Kind,l.StartsAt,l.EndsAt,
           l.LicenseKey,l.Note,l.Status,l.CreatedAt
    FROM platform.Licenses l JOIN platform.Plans p ON p.Id=l.PlanId
    WHERE l.TenantRefId=@TenantId ORDER BY l.Id DESC;
END;
GO
CREATE OR ALTER PROCEDURE platform.usp_Owner_License_Issue
    @TenantId INT,@PlanCode NVARCHAR(60),@Kind NVARCHAR(20),@StartsAt DATETIMEOFFSET(3),@EndsAt DATETIMEOFFSET(3)=NULL,
    @LicenseKey NVARCHAR(100)=NULL,@Note NVARCHAR(500)=NULL,@Now DATETIMEOFFSET(3),@ActorId INT
AS
BEGIN
    SET NOCOUNT ON;
    IF ISNULL(TRY_CONVERT(bit,SESSION_CONTEXT(N'FegHR.IsPlatformAdmin')),0)<>1
        THROW 51020,'Product owner context is required.',1;
    DECLARE @PlanId INT=(SELECT Id FROM platform.Plans WHERE Code=@PlanCode AND IsActive=1);
    IF @PlanId IS NULL THROW 51023,'Unknown or inactive plan.',1;
    IF @Kind NOT IN(N'Trial',N'Paid',N'Complimentary') THROW 51024,'Unknown licence kind.',1;
    IF @StartsAt IS NULL OR @Now IS NULL OR (@EndsAt IS NOT NULL AND @EndsAt<=@StartsAt)
        THROW 51025,'The licence period is not valid.',1;
    IF NOT EXISTS(SELECT 1 FROM platform.Tenants WHERE Id=@TenantId) THROW 51022,'Company not found.',1;
    INSERT platform.Licenses(TenantRefId,PlanId,Kind,StartsAt,EndsAt,LicenseKey,Note,CreatedAt,CreatedBy)
    VALUES(@TenantId,@PlanId,@Kind,@StartsAt,@EndsAt,NULLIF(LTRIM(RTRIM(@LicenseKey)),N''),NULLIF(LTRIM(RTRIM(@Note)),N''),@Now,@ActorId);
    SELECT CONVERT(int,SCOPE_IDENTITY()) AS Id;
END;
GO
CREATE OR ALTER PROCEDURE platform.usp_Owner_License_Revoke
    @LicenseId INT,@Now DATETIMEOFFSET(3),@ActorId INT
AS
BEGIN
    SET NOCOUNT ON;
    IF ISNULL(TRY_CONVERT(bit,SESSION_CONTEXT(N'FegHR.IsPlatformAdmin')),0)<>1
        THROW 51020,'Product owner context is required.',1;
    UPDATE platform.Licenses SET Status=N'Revoked',UpdatedAt=@Now,UpdatedBy=@ActorId WHERE Id=@LicenseId AND Status=N'Active';
    IF @@ROWCOUNT=0 THROW 51026,'Licence not found or already revoked.',1;
END;
GO
CREATE OR ALTER PROCEDURE platform.usp_Owner_Policy_Get
AS
BEGIN
    SET NOCOUNT ON;
    SELECT lp.TrialDays,p.Code AS TrialPlanCode,lp.RegistrationOpen,lp.RowVersion
    FROM platform.LicensePolicy lp JOIN platform.Plans p ON p.Id=lp.TrialPlanId WHERE lp.Id=1;
END;
GO
CREATE OR ALTER PROCEDURE platform.usp_Owner_Policy_Set
    @TrialDays INT,@TrialPlanCode NVARCHAR(60),@RegistrationOpen BIT,@RowVersion BINARY(8),@Now DATETIMEOFFSET(3),@ActorId INT
AS
BEGIN
    SET NOCOUNT ON;
    IF ISNULL(TRY_CONVERT(bit,SESSION_CONTEXT(N'FegHR.IsPlatformAdmin')),0)<>1
        THROW 51020,'Product owner context is required.',1;
    DECLARE @PlanId INT=(SELECT Id FROM platform.Plans WHERE Code=@TrialPlanCode AND IsActive=1);
    IF @PlanId IS NULL THROW 51023,'Unknown or inactive plan.',1;
    IF @TrialDays IS NULL OR @TrialDays NOT BETWEEN 1 AND 3650 THROW 51027,'Trial length must be between 1 and 3650 days.',1;
    UPDATE platform.LicensePolicy SET TrialDays=@TrialDays,TrialPlanId=@PlanId,RegistrationOpen=ISNULL(@RegistrationOpen,1),
           UpdatedAt=@Now,UpdatedBy=@ActorId
    WHERE Id=1 AND RowVersion=@RowVersion;
    IF @@ROWCOUNT=0 THROW 51028,'The licence policy was changed by someone else. Reload and try again.',1;
END;
GO
-- Public: what the registration page may show before a company exists.
CREATE OR ALTER PROCEDURE platform.usp_Registration_GetOffer
AS
BEGIN
    SET NOCOUNT ON;
    SELECT lp.RegistrationOpen,lp.TrialDays,p.Name AS PlanName
    FROM platform.LicensePolicy lp JOIN platform.Plans p ON p.Id=lp.TrialPlanId WHERE lp.Id=1;
END;
GO
