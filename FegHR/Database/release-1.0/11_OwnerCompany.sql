-- The owner company: the product operator's own company (DailyCommerce's Company.IsOwner).
-- It needs no licence, has every feature and cannot be suspended. At most one company carries
-- the flag; deploy-db.ps1 -OwnerCompanySlug sets it (SeedData/92_OwnerCompany.sql).
IF COL_LENGTH(N'platform.Tenants',N'IsOwnerCompany') IS NULL
    ALTER TABLE platform.Tenants ADD IsOwnerCompany BIT NOT NULL CONSTRAINT DF_Tenants_IsOwnerCompany DEFAULT 0;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'platform.Tenants') AND name=N'UQ_Tenants_OwnerCompany')
    CREATE UNIQUE INDEX UQ_Tenants_OwnerCompany ON platform.Tenants(IsOwnerCompany) WHERE IsOwnerCompany=1;
GO
CREATE OR ALTER PROCEDURE platform.usp_License_GetStatus
    @Now DATETIMEOFFSET(3)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @TenantId INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId'));
    IF ISNULL(@TenantId,0)<=0 OR @Now IS NULL THROW 51010,'Tenant context and timestamp are required.',1;
    SELECT t.Id AS TenantId,t.Status AS TenantStatus,
           CONVERT(bit,CASE WHEN (e.Id IS NOT NULL OR t.IsOwnerCompany=1) AND t.Status=N'Active' AND t.IsActive=1 THEN 1 ELSE 0 END) AS IsLicensed,
           CASE WHEN t.IsOwnerCompany=1 THEN N'Owner' ELSE COALESCE(e.Kind,x.Kind) END AS Kind,
           CASE WHEN t.IsOwnerCompany=1 THEN NULL ELSE p.Code END AS PlanCode,
           CASE WHEN t.IsOwnerCompany=1 THEN N'Owner company' ELSE p.Name END AS PlanName,
           CASE WHEN t.IsOwnerCompany=1 THEN NULL ELSE COALESCE(e.StartsAt,x.StartsAt) END AS StartsAt,
           CASE WHEN t.IsOwnerCompany=1 THEN NULL ELSE COALESCE(e.EndsAt,x.EndsAt) END AS EndsAt
    FROM platform.Tenants t
    OUTER APPLY platform.fn_EffectiveLicense(t.Id,@Now) e
    OUTER APPLY (SELECT TOP(1) l.PlanId,l.Kind,l.StartsAt,l.EndsAt FROM platform.Licenses l
                 WHERE e.Id IS NULL AND l.TenantRefId=t.Id AND l.Status=N'Active' ORDER BY l.EndsAt DESC,l.Id DESC) x
    LEFT JOIN platform.Plans p ON p.Id=COALESCE(e.PlanId,x.PlanId)
    WHERE t.Id=@TenantId;
END;
GO
CREATE OR ALTER PROCEDURE platform.usp_Tenant_GetFeatureCodes
    @Now DATETIMEOFFSET(3)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @TenantId INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId'));
    IF ISNULL(@TenantId,0)<=0 OR @Now IS NULL THROW 51010,'Tenant context and timestamp are required.',1;
    IF EXISTS(SELECT 1 FROM platform.Tenants WHERE Id=@TenantId AND IsOwnerCompany=1)
    BEGIN
        SELECT Code FROM platform.Features WHERE IsActive=1 ORDER BY Code;
        RETURN;
    END;
    SELECT DISTINCT f.Code
    FROM platform.fn_EffectiveLicense(@TenantId,@Now) e
    JOIN platform.Plans p ON p.Id=e.PlanId AND p.IsActive=1
    CROSS APPLY OPENJSON(p.FeatureCodesJson) j
    JOIN platform.Features f ON f.Code=j.value AND f.IsActive=1
    ORDER BY f.Code;
END;
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
           CONVERT(bit,CASE WHEN e.Id IS NULL AND t.IsOwnerCompany=0 THEN 0 ELSE 1 END) AS IsLicensed,
           CASE WHEN t.IsOwnerCompany=1 THEN N'Owner' ELSE e.Kind END AS LicenseKind,
           CASE WHEN t.IsOwnerCompany=1 THEN N'Owner company' ELSE p.Name END AS PlanName,
           CASE WHEN t.IsOwnerCompany=1 THEN NULL ELSE e.EndsAt END AS LicenseEndsAt,
           COUNT(*) OVER() AS TotalCount
    FROM platform.Tenants t
    OUTER APPLY platform.fn_EffectiveLicense(t.Id,@Now) e
    LEFT JOIN platform.Plans p ON p.Id=e.PlanId
    WHERE @Search IS NULL OR t.Name LIKE N'%'+@Search+N'%' OR t.Slug LIKE N'%'+@Search+N'%' OR t.Email LIKE N'%'+@Search+N'%'
    ORDER BY t.IsOwnerCompany DESC,t.Id DESC
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
    IF @Status<>N'Active' AND EXISTS(SELECT 1 FROM platform.Tenants WHERE Id=@TenantId AND IsOwnerCompany=1)
        THROW 51029,'The owner company cannot be suspended or closed.',1;
    UPDATE platform.Tenants SET Status=@Status,UpdatedAt=@Now,UpdatedBy=@ActorId WHERE Id=@TenantId;
    IF @@ROWCOUNT=0 THROW 51022,'Company not found.',1;
END;
GO
