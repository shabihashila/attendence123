-- Adds hierarchy labels to authorized memberships; preserves existing access rules.
CREATE OR ALTER PROCEDURE iam.usp_User_GetTenants @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @Prior sql_variant=SESSION_CONTEXT(N'FegHR.TenantId'),@Tid INT,@MemberId INT;
    DECLARE companies CURSOR LOCAL FAST_FORWARD FOR
        SELECT t.Id FROM platform.Tenants t WHERE t.IsActive=1 AND t.Status=N'Active' AND EXISTS(SELECT 1 FROM iam.fn_GroupRole(@UserId,t.Id));
    BEGIN TRY
        OPEN companies;
        FETCH NEXT FROM companies INTO @Tid;
        WHILE @@FETCH_STATUS=0
        BEGIN
            EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Tid;
            BEGIN TRANSACTION;
            SET @MemberId=NULL;
            SELECT @MemberId=Id FROM iam.TenantUsers WITH(UPDLOCK,HOLDLOCK) WHERE UserId=@UserId;
            IF @MemberId IS NULL
            BEGIN
                INSERT iam.TenantUsers(UserId,CreatedAt) VALUES(@UserId,SYSDATETIMEOFFSET());
                SET @MemberId=CONVERT(int,SCOPE_IDENTITY());
                INSERT iam.UserTenants(UserId,TenantRefId,TenantUserId,IsActive,IsGroupManaged) VALUES(@UserId,@Tid,@MemberId,1,1);
            END;
            COMMIT TRANSACTION;
            FETCH NEXT FROM companies INTO @Tid;
        END;
        CLOSE companies; DEALLOCATE companies;
        EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior;
        THROW;
    END CATCH;
    SELECT t.Id AS TenantId,t.Slug,t.Name,x.TenantUserId,t.ParentTenantRefId,p.Name AS ParentName,
        CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM platform.Tenants child WHERE child.ParentTenantRefId=t.Id) THEN 1 ELSE 0 END) AS HasChildConcerns FROM iam.UserTenants x
    JOIN platform.Tenants t ON t.Id=x.TenantRefId LEFT JOIN platform.Tenants p ON p.Id=t.ParentTenantRefId JOIN iam.Users u ON u.Id=x.UserId
    WHERE x.UserId=@UserId AND x.IsActive=1 AND u.IsActive=1 AND t.IsActive=1 AND t.Status=N'Active'
      AND (x.IsGroupManaged=0 OR EXISTS(SELECT 1 FROM iam.fn_GroupRole(@UserId,t.Id))) ORDER BY t.Id;
END;
GO
