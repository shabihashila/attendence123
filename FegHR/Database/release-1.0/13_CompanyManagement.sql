-- Owner-managed main companies and child concerns. Names never change IDs or slugs.
IF COL_LENGTH(N'platform.Tenants',N'MaxChildConcerns') IS NULL
    ALTER TABLE platform.Tenants ADD MaxChildConcerns INT NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_Tenants_MaxChildConcerns')
    ALTER TABLE platform.Tenants ADD CONSTRAINT CK_Tenants_MaxChildConcerns CHECK(MaxChildConcerns IS NULL OR MaxChildConcerns>=0);
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'platform.Tenants') AND name=N'IX_Tenants_Parent')
    CREATE INDEX IX_Tenants_Parent ON platform.Tenants(ParentTenantRefId);
GO
CREATE OR ALTER PROCEDURE platform.usp_Owner_Tenant_List
    @Now DATETIMEOFFSET(3),@Search NVARCHAR(100)=NULL,@Skip INT=0,@Take INT=50
AS
BEGIN
    SET NOCOUNT ON;
    IF ISNULL(TRY_CONVERT(bit,SESSION_CONTEXT(N'FegHR.IsPlatformAdmin')),0)<>1
        THROW 51020,'Product owner context is required.',1;
    SET @Search=NULLIF(LTRIM(RTRIM(@Search)),N'');
    SELECT t.Id,t.Slug,t.Name,t.Phone,t.Email,t.Status,t.Origin,t.CreatedAt,t.RowVersion,t.IsOwnerCompany,
           t.ParentTenantRefId,parent.Name AS ParentName,t.MaxChildConcerns,
           (SELECT COUNT(*) FROM platform.Tenants child WHERE child.ParentTenantRefId=t.Id) AS ChildCount,
           CONVERT(bit,CASE WHEN e.Id IS NULL AND t.IsOwnerCompany=0 THEN 0 ELSE 1 END) AS IsLicensed,
           CASE WHEN t.IsOwnerCompany=1 THEN N'Owner' ELSE e.Kind END AS LicenseKind,
           CASE WHEN t.IsOwnerCompany=1 THEN N'Owner company' ELSE p.Name END AS PlanName,
           CASE WHEN t.IsOwnerCompany=1 THEN NULL ELSE e.EndsAt END AS LicenseEndsAt,COUNT(*) OVER() AS TotalCount
    FROM platform.Tenants t
    LEFT JOIN platform.Tenants parent ON parent.Id=t.ParentTenantRefId
    OUTER APPLY platform.fn_EffectiveLicense(t.Id,@Now) e
    LEFT JOIN platform.Plans p ON p.Id=e.PlanId
    WHERE @Search IS NULL OR t.Name LIKE N'%'+@Search+N'%' OR t.Slug LIKE N'%'+@Search+N'%' OR t.Email LIKE N'%'+@Search+N'%'
    ORDER BY COALESCE(parent.IsOwnerCompany,t.IsOwnerCompany) DESC,COALESCE(t.ParentTenantRefId,t.Id) DESC,CASE WHEN t.ParentTenantRefId IS NULL THEN 0 ELSE 1 END,t.Id
    OFFSET CASE WHEN @Skip<0 THEN 0 ELSE @Skip END ROWS FETCH NEXT CASE WHEN @Take BETWEEN 1 AND 200 THEN @Take ELSE 50 END ROWS ONLY;
END;
GO
CREATE OR ALTER PROCEDURE platform.usp_Owner_Tenant_Save
    @TenantId INT,@Name NVARCHAR(200),@ParentTenantRefId INT=NULL,@MaxChildConcerns INT=NULL,
    @RowVersion BINARY(8),@Now DATETIMEOFFSET(3),@ActorId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF ISNULL(TRY_CONVERT(bit,SESSION_CONTEXT(N'FegHR.IsPlatformAdmin')),0)<>1
        THROW 51020,'Product owner context is required.',1;
    SET @Name=NULLIF(LTRIM(RTRIM(@Name)),N'');
    IF @Name IS NULL OR @Now IS NULL OR @MaxChildConcerns<0 THROW 51060,'Enter a company name and a non-negative child concern limit.',1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @LockResult INT;
        EXEC @LockResult=sys.sp_getapplock @Resource=N'FegHR.CompanyHierarchy',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
        IF @LockResult<0 THROW 51061,'Company hierarchy is busy. Try again.',1;
        IF NOT EXISTS(SELECT 1 FROM platform.Tenants WITH(UPDLOCK,HOLDLOCK) WHERE Id=@TenantId)
            THROW 51022,'Company not found.',1;
        IF NOT EXISTS(SELECT 1 FROM platform.Tenants WHERE Id=@TenantId AND RowVersion=@RowVersion)
            THROW 51062,'Someone else changed this company. Reload and try again.',1;
        DECLARE @Children INT=(SELECT COUNT(*) FROM platform.Tenants WHERE ParentTenantRefId=@TenantId);
        IF @MaxChildConcerns IS NOT NULL AND @MaxChildConcerns<@Children
            THROW 51063,'The limit cannot be lower than the existing child concern count.',1;
        IF @ParentTenantRefId IS NOT NULL
        BEGIN
            IF @ParentTenantRefId=@TenantId OR @Children>0 OR EXISTS(SELECT 1 FROM platform.Tenants WHERE Id=@TenantId AND IsOwnerCompany=1)
                THROW 51064,'A company with child concerns, or the owner company, cannot become a child concern.',1;
            IF NOT EXISTS(SELECT 1 FROM platform.Tenants WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ParentTenantRefId AND ParentTenantRefId IS NULL
                AND ((IsActive=1 AND Status=N'Active') OR EXISTS(SELECT 1 FROM platform.Tenants currentCompany WHERE currentCompany.Id=@TenantId AND currentCompany.ParentTenantRefId=@ParentTenantRefId)))
                THROW 51065,'Choose an active main company.',1;
            IF EXISTS(SELECT 1 FROM platform.Tenants parent WHERE parent.Id=@ParentTenantRefId AND parent.MaxChildConcerns IS NOT NULL
                AND (SELECT COUNT(*) FROM platform.Tenants child WHERE child.ParentTenantRefId=parent.Id AND child.Id<>@TenantId)>=parent.MaxChildConcerns)
                THROW 51066,'This main company has reached its child concern limit.',1;
        END;
        UPDATE platform.Tenants SET Name=@Name,ParentTenantRefId=@ParentTenantRefId,MaxChildConcerns=@MaxChildConcerns,
            UpdatedAt=@Now,UpdatedBy=@ActorId WHERE Id=@TenantId;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE platform.usp_Owner_MainCompanies_List
AS
BEGIN
    SET NOCOUNT ON;
    IF ISNULL(TRY_CONVERT(bit,SESSION_CONTEXT(N'FegHR.IsPlatformAdmin')),0)<>1
        THROW 51020,'Product owner context is required.',1;
    SELECT Id,Name FROM platform.Tenants WHERE ParentTenantRefId IS NULL AND IsActive=1 AND Status=N'Active' ORDER BY Name,Id;
END;
GO
-- Create a separate tenant with default roles/settings and Owner access; staff accounts
-- are added through Users after opening the concern. Public registration cannot set a parent.
CREATE OR ALTER PROCEDURE platform.usp_Owner_Concern_Create
    @ParentTenantId INT,@Name NVARCHAR(200),@BaseSlug NVARCHAR(70),@Now DATETIMEOFFSET(3),@ActorId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF ISNULL(TRY_CONVERT(bit,SESSION_CONTEXT(N'FegHR.IsPlatformAdmin')),0)<>1
        THROW 51020,'Product owner context is required.',1;
    DECLARE @Prior sql_variant=SESSION_CONTEXT(N'FegHR.TenantId');
    DECLARE @TenantId INT,@Slug NVARCHAR(80),@TrialEndsAt DATETIMEOFFSET(3),@LockResult INT;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC @LockResult=sys.sp_getapplock @Resource=N'FegHR.CompanyHierarchy',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=10000;
        IF @LockResult<0 THROW 51061,'Company hierarchy is busy. Try again.',1;
        IF NOT EXISTS(SELECT 1 FROM platform.Tenants WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ParentTenantId AND ParentTenantRefId IS NULL AND IsActive=1 AND Status=N'Active')
            THROW 51065,'Choose an active main company.',1;
        IF EXISTS(SELECT 1 FROM platform.Tenants parent WHERE parent.Id=@ParentTenantId AND parent.MaxChildConcerns IS NOT NULL
            AND (SELECT COUNT(*) FROM platform.Tenants child WHERE child.ParentTenantRefId=parent.Id)>=parent.MaxChildConcerns)
            THROW 51066,'This main company has reached its child concern limit.',1;
        EXEC platform.usp_Tenant_Provision @Name=@Name,@BaseSlug=@BaseSlug,@Origin=N'Operator',@Now=@Now,
            @TenantId=@TenantId OUTPUT,@Slug=@Slug OUTPUT,@TrialEndsAt=@TrialEndsAt OUTPUT;
        UPDATE platform.Tenants SET ParentTenantRefId=@ParentTenantId,CreatedBy=@ActorId WHERE Id=@TenantId;
        EXEC iam.usp_Tenant_SeedRoles @Now=@Now;
        EXEC attendance.usp_Tenant_SeedDefaults @Now=@Now,@ActorId=@ActorId;
        EXEC leave.usp_Tenant_SeedDefaults @Now=@Now,@ActorId=@ActorId;
        EXEC iam.usp_PlatformOwner_JoinTenant @Now=@Now;
        COMMIT TRANSACTION;
        EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior;
        THROW;
    END CATCH;
    SELECT @TenantId AS Id,@Slug AS Slug,@TrialEndsAt AS TrialEndsAt;
END;
GO
