CREATE OR ALTER PROCEDURE platform.usp_Group_Concern_Create @Name NVARCHAR(200),@BaseSlug NVARCHAR(70),@Now DATETIMEOFFSET(3)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Context TABLE(GroupId INT,Name NVARCHAR(200),CanManage BIT,CanBill BIT);
    INSERT @Context EXEC platform.usp_Group_Context @Manage=1;
    DECLARE @GroupId INT=(SELECT GroupId FROM @Context),@User INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.UserId')),@Prior sql_variant=SESSION_CONTEXT(N'FegHR.IsPlatformAdmin');
    -- Only this owned procedure can enter the existing Owner provisioning routine, for the
    -- authorized group ID. Restore the flag on success and error; never elevate a JWT.
    BEGIN TRY
        EXEC sys.sp_set_session_context @key=N'FegHR.IsPlatformAdmin',@value=1;
        EXEC platform.usp_Owner_Concern_Create @ParentTenantId=@GroupId,@Name=@Name,@BaseSlug=@BaseSlug,@Now=@Now,@ActorId=@User;
        EXEC sys.sp_set_session_context @key=N'FegHR.IsPlatformAdmin',@value=@Prior;
        INSERT platform.GroupAudit(GroupId,ActorId,Action,Detail,OccurredAt) VALUES(@GroupId,@User,N'Child concern created',@Name,@Now);
    END TRY BEGIN CATCH
        EXEC sys.sp_set_session_context @key=N'FegHR.IsPlatformAdmin',@value=@Prior; THROW;
    END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE leave.usp_ApprovalHistory @ApplicationId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @User INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.UserId'));
    IF NOT EXISTS(SELECT 1 FROM leave.Applications a JOIN org.Employees e ON e.Id=a.EmployeeId WHERE a.Id=@ApplicationId
        AND (e.UserId=@User OR a.ApproverUserId=@User OR EXISTS(SELECT 1 FROM leave.ApprovalSteps WHERE ApplicationId=@ApplicationId AND ApproverUserId=@User) OR EXISTS(SELECT 1 FROM iam.fn_UserPermissions(@User) WHERE Code=N'leave.applications.read')))
        THROW 54012,'You cannot view this approval history.',1;
    SELECT s.Stage,u.DisplayName AS Approver,s.Status,s.DecidedAt,s.Remarks FROM leave.ApprovalSteps s JOIN iam.Users u ON u.Id=s.ApproverUserId WHERE s.ApplicationId=@ApplicationId
    UNION ALL
    SELECT r.Stage,CONCAT(oldUser.DisplayName,N' -> ',newUser.DisplayName),N'Reassigned',r.OccurredAt,r.Reason FROM leave.ApprovalReassignments r JOIN iam.Users oldUser ON oldUser.Id=r.OldUserId JOIN iam.Users newUser ON newUser.Id=r.NewUserId WHERE r.ApplicationId=@ApplicationId
    ORDER BY Stage,DecidedAt;
END;
GO
CREATE OR ALTER PROCEDURE platform.usp_Group_Company_Rename @TenantId INT,@Name NVARCHAR(200),@RowVersion BINARY(8)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Context TABLE(GroupId INT,Name NVARCHAR(200),CanManage BIT,CanBill BIT);
    INSERT @Context EXEC platform.usp_Group_Context @Manage=1;
    DECLARE @Gid INT=(SELECT GroupId FROM @Context),@User INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.UserId'));
    IF NULLIF(LTRIM(RTRIM(@Name)),N'') IS NULL THROW 51108,'Enter a company name.',1;
    UPDATE platform.Tenants SET Name=LTRIM(RTRIM(@Name)),UpdatedAt=SYSDATETIMEOFFSET(),UpdatedBy=@User
    WHERE Id=@TenantId AND (Id=@Gid OR ParentTenantRefId=@Gid) AND RowVersion=@RowVersion;
    IF @@ROWCOUNT=0 THROW 51107,'Company unavailable or changed. Reload before saving.',1;
END;
GO
CREATE OR ALTER TRIGGER platform.tr_CompanyGroupAudit ON platform.Tenants AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Actor INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.UserId'));
    IF NOT EXISTS(SELECT 1 FROM iam.Users WHERE Id=@Actor) RETURN;
    INSERT platform.GroupAudit(GroupId,ActorId,Action,Detail,OccurredAt)
    SELECT DISTINCT g.Id,@Actor,N'Company identity or hierarchy changed',
        CONCAT(N'Company ',i.Id,N': ',d.Name,N' -> ',i.Name,N'; parent ',COALESCE(CONVERT(nvarchar(20),d.ParentTenantRefId),N'none'),N' -> ',COALESCE(CONVERT(nvarchar(20),i.ParentTenantRefId),N'none')),SYSDATETIMEOFFSET()
    FROM inserted i JOIN deleted d ON d.Id=i.Id CROSS APPLY(VALUES(COALESCE(d.ParentTenantRefId,d.Id)),(COALESCE(i.ParentTenantRefId,i.Id))) g(Id)
    WHERE i.Name<>d.Name OR ISNULL(i.ParentTenantRefId,0)<>ISNULL(d.ParentTenantRefId,0);
END;
GO
IF OBJECT_ID(N'leave.ApprovalReassignments',N'U') IS NULL
    CREATE TABLE leave.ApprovalReassignments(Id BIGINT IDENTITY PRIMARY KEY,TenantId INT NOT NULL,ApplicationId INT NOT NULL,Stage INT NOT NULL,
        OldUserId INT NOT NULL REFERENCES iam.Users(Id),NewUserId INT NOT NULL REFERENCES iam.Users(Id),ActorId INT NOT NULL REFERENCES iam.Users(Id),
        Reason NVARCHAR(500) NOT NULL,OccurredAt DATETIMEOFFSET(3) NOT NULL,
        FOREIGN KEY(TenantId,ApplicationId,Stage) REFERENCES leave.ApprovalSteps(TenantId,ApplicationId,Stage));
EXEC security.usp_ApplyTenantPolicy @Schema=N'leave';
DENY INSERT,UPDATE,DELETE ON OBJECT::leave.ApprovalReassignments TO feghr_runtime;
GO
CREATE OR ALTER PROCEDURE leave.usp_Approval_Reassign @ApplicationId INT,@NewUserId INT,@Reason NVARCHAR(500),@ExpectedApproverId INT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @User INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.UserId')),@Tid INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId'));
    IF NOT EXISTS(SELECT 1 FROM iam.fn_UserPermissions(@User) WHERE Code=N'leave.setup.write') THROW 54012,'Leave setup permission required.',1;
    IF NULLIF(LTRIM(RTRIM(@Reason)),N'') IS NULL THROW 54001,'A reassignment reason is required.',1;
    IF NOT EXISTS(SELECT 1 FROM iam.TenantUsers tu JOIN iam.Users u ON u.Id=tu.UserId AND u.IsActive=1 WHERE tu.UserId=@NewUserId AND tu.IsActive=1) THROW 54001,'Choose an active company member.',1;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Step NVARCHAR(20),@OldUser INT,@Stage INT;
        SELECT @Step=CurrentStep,@OldUser=ApproverUserId FROM leave.Applications WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ApplicationId AND Status=N'Pending' AND UsesApprovalWorkflow=1 AND IsActive=1;
        IF @OldUser IS NULL OR @OldUser<>@ExpectedApproverId THROW 54020,'The approval step changed. Reload before reassigning.',1;
        IF EXISTS(SELECT 1 FROM leave.Applications a JOIN org.Employees e ON e.Id=a.EmployeeId WHERE a.Id=@ApplicationId AND e.UserId=@NewUserId) THROW 54012,'The applicant cannot be their own approver.',1;
        SET @Stage=CASE WHEN @Step=N'Final' THEN 6 ELSE TRY_CONVERT(int,SUBSTRING(@Step,2,1)) END;
        INSERT leave.ApprovalReassignments(TenantId,ApplicationId,Stage,OldUserId,NewUserId,ActorId,Reason,OccurredAt) VALUES(@Tid,@ApplicationId,@Stage,@OldUser,@NewUserId,@User,@Reason,SYSDATETIMEOFFSET());
        UPDATE leave.ApprovalSteps SET ApproverUserId=@NewUserId WHERE ApplicationId=@ApplicationId AND Stage=@Stage AND Status=N'Pending';
        UPDATE leave.Applications SET ApproverUserId=@NewUserId,UpdatedAt=SYSDATETIMEOFFSET(),UpdatedBy=@User WHERE Id=@ApplicationId;
        COMMIT TRANSACTION;
    END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK TRANSACTION; THROW; END CATCH;
END;
GO
CREATE OR ALTER PROCEDURE leave.usp_Application_Cancel @ApplicationId INT,@EmployeeId INT,@Now DATETIMEOFFSET(3),@ActorId INT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE leave.Applications SET Status=N'Cancelled',CurrentStep=N'Done',ApproverUserId=NULL,DecidedBy=@ActorId,DecidedAt=@Now,UpdatedAt=@Now,UpdatedBy=@ActorId
        WHERE Id=@ApplicationId AND EmployeeId=@EmployeeId AND Status=N'Pending' AND IsActive=1;
        IF @@ROWCOUNT=0 THROW 54011,'Only your own pending application can be cancelled.',1;
        UPDATE leave.ApprovalSteps SET Status=N'Cancelled' WHERE ApplicationId=@ApplicationId AND Status=N'Pending';
        COMMIT TRANSACTION;
    END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK TRANSACTION; THROW; END CATCH;
END;
GO
