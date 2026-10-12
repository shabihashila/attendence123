-- Marks one company as the owner company (the product operator's own). Run by deploy-db.ps1
-- only when -OwnerCompanySlug is given. Any previous owner company loses the flag.
:on error exit
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Slug NVARCHAR(80)=LOWER(LTRIM(RTRIM(N'$(OwnerCompanySlug)')));
DECLARE @Now DATETIMEOFFSET(3)=CONVERT(datetimeoffset(3),N'$(SeedTimestamp)');
IF NOT EXISTS(SELECT 1 FROM platform.Tenants WHERE Slug=@Slug) THROW 51062,'The owner company slug does not exist.',1;
BEGIN TRANSACTION;
UPDATE platform.Tenants SET IsOwnerCompany=0,UpdatedAt=@Now WHERE IsOwnerCompany=1 AND Slug<>@Slug;
UPDATE platform.Tenants SET IsOwnerCompany=1,Status=N'Active',IsActive=1,UpdatedAt=@Now WHERE Slug=@Slug AND IsOwnerCompany=0;
COMMIT TRANSACTION;
PRINT N'Owner company: ' + @Slug;
GO
