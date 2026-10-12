-- Creates or promotes the product owner (platform administrator) and gives them an
-- administrator membership in every company. Run by deploy-db.ps1 only when -PlatformOwnerEmail
-- is given; the BCrypt cost-12 hash arrives through a temporarily bound SQLCMD environment
-- variable, never in arguments or files. An existing user keeps their password.
:on error exit
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Email NVARCHAR(254)=LOWER(LTRIM(RTRIM(N'$(PlatformOwnerEmail)')));
DECLARE @Name NVARCHAR(150)=N'$(PlatformOwnerName)';
DECLARE @Hash NVARCHAR(1000)=N'$(PlatformOwnerPasswordHash)';
DECLARE @Now DATETIMEOFFSET(3)=CONVERT(datetimeoffset(3),N'$(SeedTimestamp)');
DECLARE @UserId INT,@Tenant INT;
DECLARE @Prior sql_variant=SESSION_CONTEXT(N'FegHR.TenantId');
IF @Email NOT LIKE N'_%@_%._%' THROW 51060,'A valid product owner email is required.',1;
BEGIN TRY
    BEGIN TRANSACTION;
    SELECT @UserId=Id FROM iam.Users WITH(UPDLOCK,HOLDLOCK) WHERE NormalizedEmail=UPPER(@Email);
    IF @UserId IS NULL
    BEGIN
        IF LEFT(@Hash,7) NOT IN(N'$2a$12$',N'$2b$12$',N'$2y$12$') OR LEN(@Hash)<>60
            THROW 51061,'A BCrypt cost-12 hash is required to create the product owner.',1;
        INSERT iam.Users(Email,NormalizedEmail,DisplayName,PasswordHash,SecurityStamp,CreatedAt)
        VALUES(@Email,UPPER(@Email),@Name,@Hash,NEWID(),@Now);
        SET @UserId=CONVERT(int,SCOPE_IDENTITY());
    END;
    UPDATE iam.Users SET IsPlatformAdmin=1,IsActive=1 WHERE Id=@UserId;
    DECLARE tenants CURSOR LOCAL FAST_FORWARD FOR SELECT Id FROM platform.Tenants WHERE IsActive=1;
    OPEN tenants; FETCH NEXT FROM tenants INTO @Tenant;
    WHILE @@FETCH_STATUS=0
    BEGIN
        EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Tenant;
        EXEC iam.usp_PlatformOwner_JoinTenant @Now=@Now;
        FETCH NEXT FROM tenants INTO @Tenant;
    END;
    CLOSE tenants; DEALLOCATE tenants;
    EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior;
    THROW;
END CATCH;
PRINT N'Product owner ready: ' + @Email + N'. Existing passwords were preserved.';
GO
