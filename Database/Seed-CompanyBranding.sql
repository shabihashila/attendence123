:on error exit
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Tenant INT,@Name NVARCHAR(200),@Slug NVARCHAR(80);
DECLARE tenants CURSOR LOCAL FAST_FORWARD FOR SELECT Id,Name,Slug FROM platform.Tenants WHERE Slug IN(N'lighthouse-consulting',N'fegtechnova',N'futurepathglobal');
OPEN tenants;
FETCH NEXT FROM tenants INTO @Tenant,@Name,@Slug;
WHILE @@FETCH_STATUS=0
BEGIN
    EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Tenant;
    UPDATE platform.TenantSettings SET BrandingJson=(SELECT 1 AS version,@Name AS displayName,N'#1863DC' AS primaryColour,N'#0F9F8F' AS accentColour FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
    WHERE BrandingJson=N'{}' OR JSON_VALUE(BrandingJson,'$.logoUrl')=N'/feg-technova-logo.png';
    FETCH NEXT FROM tenants INTO @Tenant,@Name,@Slug;
END;
CLOSE tenants;
DEALLOCATE tenants;
EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=NULL;
IF NOT EXISTS(SELECT 1 FROM logs.Logs WHERE Message=N'FegHR demo installation ready')
    INSERT logs.Logs(Message,Level,TimeStamp) VALUES(N'FegHR demo installation ready',N'Information',GETDATE());
GO
