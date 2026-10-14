IF OBJECT_ID(N'platform.RequestAudit',N'U') IS NULL
BEGIN
    CREATE TABLE platform.RequestAudit(Id BIGINT IDENTITY PRIMARY KEY,TenantId INT NOT NULL REFERENCES platform.Tenants(Id),
        ActorId INT NOT NULL REFERENCES iam.Users(Id),Method NVARCHAR(10) NOT NULL,Path NVARCHAR(500) NOT NULL,
        ResponseStatus INT NOT NULL,TraceId NVARCHAR(100) NOT NULL,OccurredAt DATETIMEOFFSET(3) NOT NULL);
    CREATE INDEX IX_RequestAudit_Tenant ON platform.RequestAudit(TenantId,Id DESC) INCLUDE(ActorId,Method,Path,ResponseStatus,OccurredAt);
END;
EXEC security.usp_ApplyTenantPolicy @Schema=N'platform';
DENY INSERT,UPDATE,DELETE ON OBJECT::platform.RequestAudit TO feghr_runtime;
GO
CREATE OR ALTER PROCEDURE platform.usp_RequestAudit_Add @Method NVARCHAR(10),@Path NVARCHAR(500),@ResponseStatus INT,@TraceId NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT platform.RequestAudit(TenantId,ActorId,Method,Path,ResponseStatus,TraceId,OccurredAt)
    VALUES(TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId')),TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.UserId')),@Method,@Path,@ResponseStatus,@TraceId,SYSDATETIMEOFFSET());
END;
GO
CREATE OR ALTER PROCEDURE platform.usp_Group_Audit
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Context TABLE(GroupId INT,Name NVARCHAR(200),CanManage BIT,CanBill BIT);
    INSERT @Context EXEC platform.usp_Group_Context;
    IF EXISTS(SELECT 1 FROM iam.fn_GroupRole(TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.UserId')),TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId'))) WHERE RoleCode=N'group-billing')
        THROW 51101,'The billing role cannot view group audit records.',1;
    DECLARE @GroupId INT=(SELECT GroupId FROM @Context),@Prior sql_variant=SESSION_CONTEXT(N'FegHR.TenantId'),@Tid INT;
    DECLARE @Audit TABLE(Id BIGINT,Actor NVARCHAR(150),Action NVARCHAR(80),Detail NVARCHAR(1000),OccurredAt DATETIMEOFFSET(3));
    INSERT @Audit SELECT TOP(200) a.Id,u.DisplayName,a.Action,a.Detail,a.OccurredAt FROM platform.GroupAudit a JOIN iam.Users u ON u.Id=a.ActorId WHERE a.GroupId=@GroupId ORDER BY a.Id DESC;
    DECLARE companies CURSOR LOCAL FAST_FORWARD FOR SELECT Id FROM platform.Tenants WHERE Id=@GroupId OR ParentTenantRefId=@GroupId;
    BEGIN TRY
        OPEN companies; FETCH NEXT FROM companies INTO @Tid;
        WHILE @@FETCH_STATUS=0
        BEGIN
            EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Tid;
            INSERT @Audit SELECT TOP(200) -a.Id,u.DisplayName,CONCAT(a.Method,N' ',LEFT(a.Path,70)),CONCAT(t.Name,N'; HTTP ',a.ResponseStatus,N'; trace ',a.TraceId),a.OccurredAt
            FROM platform.RequestAudit a JOIN iam.Users u ON u.Id=a.ActorId JOIN platform.Tenants t ON t.Id=a.TenantId WHERE t.Id=@GroupId OR t.ParentTenantRefId=@GroupId ORDER BY a.Id DESC;
            FETCH NEXT FROM companies INTO @Tid;
        END;
        CLOSE companies; DEALLOCATE companies;
        EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior;
    END TRY BEGIN CATCH EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior; THROW; END CATCH;
    SELECT TOP(200) * FROM @Audit ORDER BY OccurredAt DESC,Id DESC;
END;
GO
