:on error exit
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Now DATETIMEOFFSET(3)=SYSDATETIMEOFFSET(),@Today DATE=CONVERT(date,SWITCHOFFSET(SYSDATETIMEOFFSET(),'+06:00'));
DECLARE @Hash NVARCHAR(1000)=N'$(DemoPasswordHash)';
DECLARE @Name NVARCHAR(200),@Slug NVARCHAR(70),@Email NVARCHAR(254);
DECLARE companies CURSOR LOCAL FAST_FORWARD FOR
SELECT * FROM (VALUES(N'LightHouse Consulting ltd',N'lighthouse-consulting',N'admin@lighthouse.feghr.test'),
 (N'FegTechnova',N'fegtechnova',N'admin@fegtechnova.feghr.test'),
 (N'FuturePathGlobal',N'futurepathglobal',N'admin@futurepathglobal.feghr.test')) c(Name,Slug,Email);
OPEN companies;
FETCH NEXT FROM companies INTO @Name,@Slug,@Email;
WHILE @@FETCH_STATUS=0
BEGIN
    IF NOT EXISTS(SELECT 1 FROM platform.Tenants WHERE Slug=@Slug)
        EXEC iam.usp_Company_Register @CompanyName=@Name,@BaseSlug=@Slug,@CompanyAddress=N'Dhaka, Bangladesh',
             @CompanyEmail=@Email,@AdminName=N'Demo Company Administrator',@AdminEmail=@Email,
             @PasswordHash=@Hash,@Today=@Today,@Now=@Now;
    FETCH NEXT FROM companies INTO @Name,@Slug,@Email;
END;
CLOSE companies;
DEALLOCATE companies;
DECLARE @Parent INT=(SELECT Id FROM platform.Tenants WHERE Slug=N'lighthouse-consulting');
UPDATE platform.Tenants SET ParentTenantRefId=@Parent WHERE Slug IN(N'fegtechnova',N'futurepathglobal') AND ParentTenantRefId IS NULL;
INSERT platform.TenantHosts(TenantRefId,Host,IsPrimary,CreatedAt)
SELECT Id,Slug+N'.localhost',1,@Now FROM platform.Tenants t
WHERE t.Slug IN(N'lighthouse-consulting',N'fegtechnova',N'futurepathglobal')
AND NOT EXISTS(SELECT 1 FROM platform.TenantHosts h WHERE h.TenantRefId=t.Id);
-- Demo licences have no expiry so the downloaded demonstration remains usable.
INSERT platform.Licenses(TenantRefId,PlanId,Kind,StartsAt,EndsAt,Note,Status,CreatedAt)
SELECT t.Id,p.TrialPlanId,N'Complimentary',@Now,NULL,N'FegHR local demo',N'Active',@Now
FROM platform.Tenants t CROSS JOIN platform.LicensePolicy p
WHERE t.Slug IN(N'lighthouse-consulting',N'fegtechnova',N'futurepathglobal')
AND NOT EXISTS(SELECT 1 FROM platform.Licenses l WHERE l.TenantRefId=t.Id AND l.Note=N'FegHR local demo');
GO
