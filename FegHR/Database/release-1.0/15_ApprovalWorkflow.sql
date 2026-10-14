-- Existing applications retain Manager/HR. Configured new applications snapshot their route.
IF COL_LENGTH(N'leave.Applications',N'UsesApprovalWorkflow') IS NULL
    ALTER TABLE leave.Applications ADD UsesApprovalWorkflow BIT NOT NULL CONSTRAINT DF_Applications_Workflow DEFAULT 0,
        ApproverUserId INT NULL REFERENCES iam.Users(Id);
GO
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_Applications_CurrentStep')
    ALTER TABLE leave.Applications DROP CONSTRAINT CK_Applications_CurrentStep;
ALTER TABLE leave.Applications ADD CONSTRAINT CK_Applications_CurrentStep CHECK(CurrentStep IN(N'Manager',N'HR',N'L1',N'L2',N'L3',N'L4',N'L5',N'Final',N'Done'));
GO
IF OBJECT_ID(N'leave.ApprovalPolicies',N'U') IS NULL
    CREATE TABLE leave.ApprovalPolicies(TenantId INT PRIMARY KEY REFERENCES platform.Tenants(Id),
        SupervisorLevels INT NOT NULL CHECK(SupervisorLevels BETWEEN 0 AND 5),FinalApproverUserId INT NULL REFERENCES iam.Users(Id),
        IsEnabled BIT NOT NULL,RowVersion ROWVERSION,CONSTRAINT CK_ApprovalPolicy_Final CHECK(IsEnabled=0 OR FinalApproverUserId IS NOT NULL));
IF OBJECT_ID(N'leave.ApprovalSteps',N'U') IS NULL
BEGIN
    CREATE TABLE leave.ApprovalSteps(TenantId INT NOT NULL,ApplicationId INT NOT NULL,Stage INT NOT NULL CHECK(Stage BETWEEN 1 AND 6),
        ApproverUserId INT NOT NULL REFERENCES iam.Users(Id),Status NVARCHAR(20) NOT NULL DEFAULT N'Pending' CONSTRAINT CK_ApprovalSteps_Status CHECK(Status IN(N'Pending',N'Approved',N'Rejected',N'Skipped',N'Cancelled')),
        DecidedAt DATETIMEOFFSET(3) NULL,Remarks NVARCHAR(500),PRIMARY KEY(TenantId,ApplicationId,Stage),
        FOREIGN KEY(TenantId,ApplicationId) REFERENCES leave.Applications(TenantId,Id));
    CREATE INDEX IX_ApprovalSteps_Approver ON leave.ApprovalSteps(TenantId,ApproverUserId,Status) INCLUDE(ApplicationId,Stage);
END;
EXEC security.usp_ApplyTenantPolicy @Schema=N'leave';
DENY INSERT,UPDATE,DELETE ON OBJECT::leave.ApprovalSteps TO feghr_runtime;
DENY INSERT,UPDATE,DELETE ON OBJECT::leave.ApprovalPolicies TO feghr_runtime;
GO
DECLARE @Now DATETIMEOFFSET(3)=SYSDATETIMEOFFSET();
INSERT iam.Permissions(Code,Name,Module,CreatedAt)
SELECT v.Code,v.Name,N'leave',@Now FROM (VALUES(N'leave.approve.l3',N'L3 Supervisor approval'),(N'leave.approve.l4',N'L4 Supervisor approval'),(N'leave.approve.l5',N'L5 Supervisor approval'),(N'leave.approve.final',N'Final Approver')) v(Code,Name)
WHERE NOT EXISTS(SELECT 1 FROM iam.Permissions p WHERE p.Code=v.Code);
INSERT iam.GroupRolePermissions(RoleCode,PermissionId) SELECT N'group-admin',Id FROM iam.Permissions p WHERE p.Code LIKE N'leave.approve.%'
AND NOT EXISTS(SELECT 1 FROM iam.GroupRolePermissions gp WHERE gp.RoleCode=N'group-admin' AND gp.PermissionId=p.Id);
GO
CREATE OR ALTER PROCEDURE leave.usp_ApprovalPolicy @Levels INT=NULL,@FinalApproverUserId INT=NULL,@IsEnabled BIT=0,@RowVersion BINARY(8)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Tid INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId')),@User INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.UserId'));
    IF @Levels IS NOT NULL
    BEGIN
        IF NOT EXISTS(SELECT 1 FROM iam.fn_UserPermissions(@User) WHERE Code=N'leave.setup.write') THROW 54012,'Leave setup permission required.',1;
        IF @Levels NOT BETWEEN 0 AND 5 THROW 54001,'Choose zero to five supervisor levels.',1;
        IF @IsEnabled=1 AND NOT EXISTS(SELECT 1 FROM iam.TenantUsers tu JOIN iam.Users u ON u.Id=tu.UserId AND u.IsActive=1 WHERE tu.UserId=@FinalApproverUserId AND tu.IsActive=1)
            THROW 54001,'Choose an active company member as Final Approver.',1;
        IF EXISTS(SELECT 1 FROM leave.ApprovalPolicies)
        BEGIN
            UPDATE leave.ApprovalPolicies SET SupervisorLevels=@Levels,FinalApproverUserId=@FinalApproverUserId,IsEnabled=@IsEnabled WHERE RowVersion=@RowVersion;
            IF @@ROWCOUNT=0 THROW 54020,'Approval settings changed. Reload and try again.',1;
        END ELSE INSERT leave.ApprovalPolicies(TenantId,SupervisorLevels,FinalApproverUserId,IsEnabled) VALUES(@Tid,@Levels,@FinalApproverUserId,@IsEnabled);
    END;
    SELECT SupervisorLevels,FinalApproverUserId,IsEnabled,RowVersion FROM leave.ApprovalPolicies;
END;
GO
CREATE OR ALTER PROCEDURE leave.usp_ApprovalRoute_Snapshot @ApplicationId INT,@EmployeeId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Levels INT,@Final INT,@Tid INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId'));
    SELECT @Levels=SupervisorLevels,@Final=FinalApproverUserId FROM leave.ApprovalPolicies WHERE IsEnabled=1;
    IF @Levels IS NULL RETURN;
    DECLARE @Employee INT=@EmployeeId,@Level INT=1,@User INT,@ApplicantUser INT=(SELECT UserId FROM org.Employees WHERE Id=@EmployeeId);
    DECLARE @Seen TABLE(Id INT PRIMARY KEY);
    INSERT @Seen VALUES(@EmployeeId);
    WHILE @Level<=@Levels
    BEGIN
        SET @Employee=(SELECT ReportsToId FROM org.Employees WHERE Id=@Employee AND IsActive=1);
        IF @Employee IS NULL OR EXISTS(SELECT 1 FROM @Seen WHERE Id=@Employee) THROW 54021,'The configured supervisor chain is missing or circular. Ask HR to update Reports to.',1;
        INSERT @Seen VALUES(@Employee);
        SET @User=(SELECT UserId FROM org.Employees WHERE Id=@Employee AND IsActive=1);
        IF @User IS NULL OR NOT EXISTS(SELECT 1 FROM iam.TenantUsers tu JOIN iam.Users u ON u.Id=tu.UserId AND u.IsActive=1 WHERE tu.UserId=@User AND tu.IsActive=1)
            THROW 54021,'Every configured supervisor must have an active linked company login.',1;
        IF NOT EXISTS(SELECT 1 FROM iam.fn_UserPermissions(@User) WHERE Code=CONCAT(N'leave.approve.l',@Level))
            THROW 54021,'A configured supervisor lacks the approval permission for their level. Ask HR to update Roles.',1;
        INSERT leave.ApprovalSteps(TenantId,ApplicationId,Stage,ApproverUserId) VALUES(@Tid,@ApplicationId,@Level,@User);
        SET @Level+=1;
    END;
    IF @Final=@ApplicantUser OR NOT EXISTS(SELECT 1 FROM iam.TenantUsers tu JOIN iam.Users u ON u.Id=tu.UserId AND u.IsActive=1 WHERE tu.UserId=@Final AND tu.IsActive=1)
        THROW 54021,'The Final Approver must be an active company member other than the applicant.',1;
    IF NOT EXISTS(SELECT 1 FROM iam.fn_UserPermissions(@Final) WHERE Code=N'leave.approve.final') THROW 54021,'The Final Approver lacks leave.approve.final permission.',1;
    INSERT leave.ApprovalSteps(TenantId,ApplicationId,Stage,ApproverUserId) VALUES(@Tid,@ApplicationId,6,@Final);
    UPDATE leave.Applications SET UsesApprovalWorkflow=1,CurrentStep=CASE WHEN @Levels=0 THEN N'Final' ELSE N'L1' END,
        ApproverUserId=(SELECT TOP(1) ApproverUserId FROM leave.ApprovalSteps WHERE ApplicationId=@ApplicationId ORDER BY Stage) WHERE Id=@ApplicationId;
END;
GO
CREATE OR ALTER PROCEDURE leave.usp_Workflow_Decide @ApplicationId INT,@Approve BIT,@Remarks NVARCHAR(500)=NULL,@Now DATETIMEOFFSET(3),@ActorId INT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @Step NVARCHAR(20),@Employee INT,@Type INT,@Days DECIMAL(5,1),@Year INT,@Stage INT,@Permission NVARCHAR(100);
        SELECT @Step=CurrentStep,@Employee=EmployeeId,@Type=LeaveTypeId,@Days=Days,@Year=YEAR(StartDate)
        FROM leave.Applications WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ApplicationId AND Status=N'Pending' AND UsesApprovalWorkflow=1 AND IsActive=1 AND ApproverUserId=@ActorId;
        IF @Step IS NULL THROW 54012,'You are not the assigned approver at this step, or it has already been decided.',1;
        IF EXISTS(SELECT 1 FROM org.Employees WHERE Id=@Employee AND UserId=@ActorId) THROW 54012,'You cannot decide your own leave.',1;
        SET @Stage=CASE WHEN @Step=N'Final' THEN 6 ELSE TRY_CONVERT(int,SUBSTRING(@Step,2,1)) END;
        SET @Permission=CASE WHEN @Stage=6 THEN N'leave.approve.final' ELSE CONCAT(N'leave.approve.l',@Stage) END;
        IF NOT EXISTS(SELECT 1 FROM iam.fn_UserPermissions(@ActorId) WHERE Code=@Permission) THROW 54012,'Your role does not permit approval at this level.',1;
        IF @Approve=1 AND @Stage=6 AND EXISTS(SELECT 1 FROM leave.LeaveTypes WHERE Id=@Type AND YearlyQuota IS NOT NULL)
            AND EXISTS(SELECT 1 FROM leave.fn_Balance(@Employee,@Year) WHERE LeaveTypeId=@Type AND ISNULL(Remaining,0)+@Days<@Days)
            THROW 54003,'The employee no longer has enough balance for this leave.',1;
        UPDATE leave.ApprovalSteps SET Status=CASE WHEN @Approve=1 THEN N'Approved' ELSE N'Rejected' END,DecidedAt=@Now,Remarks=@Remarks
        WHERE ApplicationId=@ApplicationId AND Stage=@Stage AND Status=N'Pending' AND ApproverUserId=@ActorId;
        IF @@ROWCOUNT<>1 THROW 54012,'Approval route is inconsistent. Ask an administrator.',1;
        IF @Approve=0 UPDATE leave.ApprovalSteps SET Status=N'Skipped' WHERE ApplicationId=@ApplicationId AND Stage>@Stage AND Status=N'Pending';
        DECLARE @Next INT=(SELECT MIN(Stage) FROM leave.ApprovalSteps WHERE ApplicationId=@ApplicationId AND Stage>@Stage AND Status=N'Pending');
        UPDATE leave.Applications SET Status=CASE WHEN @Approve=0 THEN N'Rejected' WHEN @Next IS NULL THEN N'Approved' ELSE N'Pending' END,
            CurrentStep=CASE WHEN @Approve=0 OR @Next IS NULL THEN N'Done' WHEN @Next=6 THEN N'Final' ELSE CONCAT(N'L',@Next) END,
            ApproverUserId=CASE WHEN @Approve=0 OR @Next IS NULL THEN NULL ELSE (SELECT ApproverUserId FROM leave.ApprovalSteps WHERE ApplicationId=@ApplicationId AND Stage=@Next) END,
            DecidedBy=CASE WHEN @Approve=0 OR @Next IS NULL THEN @ActorId ELSE NULL END,DecidedAt=CASE WHEN @Approve=0 OR @Next IS NULL THEN @Now ELSE NULL END,
            DecisionRemarks=CASE WHEN @Approve=0 OR @Next IS NULL THEN @Remarks ELSE NULL END,UpdatedAt=@Now,UpdatedBy=@ActorId WHERE Id=@ApplicationId;
        COMMIT TRANSACTION;
    END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK TRANSACTION; THROW; END CATCH;
    SELECT Id,Status,CurrentStep FROM leave.Applications WHERE Id=@ApplicationId;
END;
GO
DENY EXECUTE ON OBJECT::leave.usp_ApprovalRoute_Snapshot TO feghr_runtime;
GO
CREATE OR ALTER PROCEDURE leave.usp_ApprovalHistory @ApplicationId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @User INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.UserId'));
    IF NOT EXISTS(SELECT 1 FROM leave.Applications a JOIN org.Employees e ON e.Id=a.EmployeeId WHERE a.Id=@ApplicationId
        AND (e.UserId=@User OR a.ApproverUserId=@User OR EXISTS(SELECT 1 FROM leave.ApprovalSteps WHERE ApplicationId=@ApplicationId AND ApproverUserId=@User) OR EXISTS(SELECT 1 FROM iam.fn_UserPermissions(@User) WHERE Code=N'leave.applications.read')))
        THROW 54012,'You cannot view this approval history.',1;
    SELECT s.Stage,u.DisplayName AS Approver,s.Status,s.DecidedAt,s.Remarks FROM leave.ApprovalSteps s JOIN iam.Users u ON u.Id=s.ApproverUserId WHERE s.ApplicationId=@ApplicationId ORDER BY s.Stage;
END;
GO
CREATE OR ALTER PROCEDURE leave.usp_Application_Submit
    @EmployeeId INT,@LeaveTypeId INT,@DayType NVARCHAR(20),@StartDate DATE,@EndDate DATE,
    @Reason NVARCHAR(1000)=NULL,@AddressDuringLeave NVARCHAR(500)=NULL,@AttachmentKey NVARCHAR(400)=NULL,
    @StartAtHr BIT=0,@Today DATE,@Now DATETIMEOFFSET(3),@ActorId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @TenantId INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId'));
    IF ISNULL(@TenantId,0)<=0 OR @Now IS NULL OR @Today IS NULL OR ISNULL(@EmployeeId,0)<=0
        THROW 54001,'Tenant context, employee and timestamps are required.',1;
    IF @StartDate IS NULL OR @EndDate IS NULL OR @EndDate<@StartDate THROW 54001,'The end date cannot be before the start date.',1;
    IF @DayType NOT IN(N'Full',N'HalfFirst',N'HalfSecond') THROW 54001,'Day type must be Full, HalfFirst or HalfSecond.',1;
    IF @DayType<>N'Full' AND @StartDate<>@EndDate THROW 54001,'A half-day leave must be a single date.',1;
    IF DATEDIFF(day,@StartDate,@EndDate)>365 THROW 54001,'A leave application cannot span more than a year.',1;
    DECLARE @Quota DECIMAL(5,1),@TypeName NVARCHAR(100),@Days DECIMAL(5,1),@Remaining DECIMAL(9,1),@Lock INT,@Id INT,
            @Resource NVARCHAR(255)=CONCAT(N'Leave.Employee.',@TenantId,N'.',@EmployeeId);
    SET @Days=CASE WHEN @DayType=N'Full' THEN DATEDIFF(day,@StartDate,@EndDate)+1 ELSE 0.5 END;
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC @Lock=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
        IF @Lock<0 THROW 54004,'Leave is busy; try again.',1;
        SELECT @Quota=YearlyQuota,@TypeName=Name FROM leave.LeaveTypes WHERE Id=@LeaveTypeId AND IsActive=1;
        IF @TypeName IS NULL THROW 54001,'The leave type is not available.',1;
        IF EXISTS(SELECT 1 FROM leave.Applications WHERE EmployeeId=@EmployeeId AND IsActive=1 AND Status IN(N'Pending',N'Approved')
                  AND StartDate<=@EndDate AND EndDate>=@StartDate
                  AND NOT (DayType<>N'Full' AND @DayType<>N'Full' AND DayType<>@DayType))
            THROW 54002,'These dates overlap another leave application.',1;
        IF @Quota IS NOT NULL
        BEGIN
            SELECT @Remaining=Remaining FROM leave.fn_Balance(@EmployeeId,YEAR(@StartDate)) WHERE LeaveTypeId=@LeaveTypeId;
            IF ISNULL(@Remaining,0)<@Days
            BEGIN
                DECLARE @Msg NVARCHAR(300)=CONCAT(N'Not enough ',@TypeName,N' balance: ',CONVERT(nvarchar(20),ISNULL(@Remaining,0)),
                                                   N' day(s) left, ',CONVERT(nvarchar(20),@Days),N' requested.');
                THROW 54003,@Msg,1;
            END;
        END;
        INSERT leave.Applications(EmployeeId,LeaveTypeId,DayType,StartDate,EndDate,Days,AppliedOn,Reason,AddressDuringLeave,AttachmentKey,
                                  CurrentStep,CreatedAt,CreatedBy)
        VALUES(@EmployeeId,@LeaveTypeId,@DayType,@StartDate,@EndDate,@Days,@Today,NULLIF(LTRIM(RTRIM(@Reason)),N''),
               NULLIF(LTRIM(RTRIM(@AddressDuringLeave)),N''),NULLIF(@AttachmentKey,N''),
               CASE WHEN @StartAtHr=1 THEN N'HR' ELSE N'Manager' END,@Now,@ActorId);
        SET @Id=CONVERT(int,SCOPE_IDENTITY());
        EXEC leave.usp_ApprovalRoute_Snapshot @ApplicationId=@Id,@EmployeeId=@EmployeeId;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
    SELECT @Id AS Id,@Days AS Days;
END;
GO
CREATE OR ALTER PROCEDURE leave.usp_Application_Decide
    @ApplicationId INT,@Approve BIT,@CanLevel1 BIT,@CanLevel2 BIT,@ActorEmployeeId INT=NULL,
    @Remarks NVARCHAR(500)=NULL,@Now DATETIMEOFFSET(3),@ActorId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF ISNULL(TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId')),0)<=0 OR @Now IS NULL OR @Approve IS NULL
        THROW 54001,'Tenant context, decision and timestamp are required.',1;
    SET @Remarks=NULLIF(LTRIM(RTRIM(@Remarks)),N'');
    DECLARE @Status NVARCHAR(20),@Step NVARCHAR(20),@EmployeeId INT,@LeaveTypeId INT,@Days DECIMAL(5,1),@StartDate DATE,
            @Quota DECIMAL(5,1),@Remaining DECIMAL(9,1);
    BEGIN TRY
        BEGIN TRANSACTION;
        SELECT @Status=Status,@Step=CurrentStep,@EmployeeId=EmployeeId,@LeaveTypeId=LeaveTypeId,@Days=Days,@StartDate=StartDate
        FROM leave.Applications WITH(UPDLOCK,HOLDLOCK) WHERE Id=@ApplicationId AND IsActive=1;
        IF EXISTS(SELECT 1 FROM leave.Applications WHERE Id=@ApplicationId AND UsesApprovalWorkflow=1) THROW 54012,'Use the configured approval workflow for this application.',1;
        IF @Status IS NULL THROW 54010,'The leave application was not found.',1;
        IF @Status<>N'Pending' THROW 54011,'This leave application has already been decided.',1;
        IF ISNULL(@CanLevel2,0)=0 AND NOT (ISNULL(@CanLevel1,0)=1 AND @Step=N'Manager')
            THROW 54012,'You cannot decide this application at its current step.',1;
        IF ISNULL(@CanLevel2,0)=0 AND @ActorEmployeeId IS NOT NULL AND @ActorEmployeeId=@EmployeeId
            THROW 54012,'You cannot approve or reject your own leave application.',1;
        IF @Approve=0
            UPDATE leave.Applications SET Status=N'Rejected',CurrentStep=N'Done',DecidedBy=@ActorId,DecidedAt=@Now,
                   DecisionRemarks=@Remarks,UpdatedAt=@Now,UpdatedBy=@ActorId WHERE Id=@ApplicationId;
        ELSE IF ISNULL(@CanLevel2,0)=1
        BEGIN
            -- Final approval: the balance is checked again without this application's own pending days.
            SELECT @Quota=YearlyQuota FROM leave.LeaveTypes WHERE Id=@LeaveTypeId;
            IF @Quota IS NOT NULL
            BEGIN
                SELECT @Remaining=Remaining+@Days FROM leave.fn_Balance(@EmployeeId,YEAR(@StartDate)) WHERE LeaveTypeId=@LeaveTypeId;
                IF ISNULL(@Remaining,0)<@Days THROW 54003,'The employee no longer has enough balance for this leave.',1;
            END;
            UPDATE leave.Applications SET Status=N'Approved',CurrentStep=N'Done',DecidedBy=@ActorId,DecidedAt=@Now,
                   DecisionRemarks=@Remarks,UpdatedAt=@Now,UpdatedBy=@ActorId WHERE Id=@ApplicationId;
        END
        ELSE
            UPDATE leave.Applications SET CurrentStep=N'HR',Level1By=@ActorId,Level1At=@Now,Level1Remarks=@Remarks,
                   UpdatedAt=@Now,UpdatedBy=@ActorId WHERE Id=@ApplicationId;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
    SELECT Id,Status,CurrentStep FROM leave.Applications WHERE Id=@ApplicationId;
END;
GO
CREATE OR ALTER PROCEDURE iam.usp_Tenant_SeedRoles
    @Now DATETIMEOFFSET(3)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @TenantId INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId'));
    IF ISNULL(@TenantId,0)<=0 THROW 51022, 'Tenant context is required.', 1;
    IF @Now IS NULL THROW 51023, 'Application timestamp is required.', 1;
    DECLARE @Started BIT=0;
    IF @@TRANCOUNT=0 BEGIN SET @Started=1; BEGIN TRANSACTION; END;
    BEGIN TRY
        INSERT iam.Roles(TenantId,Code,Name,IsSystem,CreatedAt)
        SELECT @TenantId,x.Code,x.Name,1,@Now FROM (VALUES
            (N'admin',N'Company Admin'),(N'hr',N'HR Manager'),(N'manager',N'Line Manager'),(N'employee',N'Employee'),(N'viewer',N'Viewer')) x(Code,Name)
        WHERE NOT EXISTS(SELECT 1 FROM iam.Roles r WITH(UPDLOCK,HOLDLOCK) WHERE r.TenantId=@TenantId AND r.Code=x.Code);

        DECLARE @Grants TABLE(RoleId INT,PermissionId INT);
        INSERT iam.RoleGrantSeeds(TenantId,RoleId,PermissionId,AppliedAt)
        OUTPUT inserted.RoleId,inserted.PermissionId INTO @Grants(RoleId,PermissionId)
        SELECT @TenantId,r.Id,p.Id,@Now FROM iam.Roles r CROSS JOIN iam.Permissions p
        WHERE r.TenantId=@TenantId AND r.IsSystem=1 AND p.IsActive=1 AND p.Module<>N'owner'
          AND NOT EXISTS(SELECT 1 FROM iam.RoleGrantSeeds g WITH(UPDLOCK,HOLDLOCK) WHERE g.TenantId=r.TenantId AND g.RoleId=r.Id AND g.PermissionId=p.Id)
          AND (r.Code=N'admin'
               OR r.Code=N'hr' AND (p.Module IN(N'org',N'attendance',N'leave')
                                    OR p.Code IN(N'platform.branches.read',N'platform.branches.write',N'platform.settings.read',N'iam.users.read'))
               OR r.Code=N'manager' AND p.Code IN(N'attendance.punch.self',N'attendance.logs.read',N'attendance.reports.read',
                                                  N'leave.apply.self',N'leave.applications.read',N'leave.approve.l1',N'leave.reports.read',
                                                  N'org.employees.read')
               OR r.Code=N'viewer' AND p.Code LIKE N'%.read'
               OR r.Code=N'employee' AND p.Code IN(N'attendance.punch.self',N'leave.apply.self'));
        INSERT iam.RolePermissions(TenantId,RoleId,PermissionId,CreatedAt)
        SELECT @TenantId,g.RoleId,g.PermissionId,@Now FROM @Grants g
        WHERE NOT EXISTS(SELECT 1 FROM iam.RolePermissions rp WHERE rp.TenantId=@TenantId AND rp.RoleId=g.RoleId AND rp.PermissionId=g.PermissionId);
        IF @Started=1 COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @Started=1 AND XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO
DECLARE @Now DATETIMEOFFSET(3)=CONVERT(datetimeoffset(3),N'$(SeedTimestamp)');
DECLARE @Prior sql_variant=SESSION_CONTEXT(N'FegHR.TenantId'),@Tenant INT;
DECLARE tenants CURSOR LOCAL FAST_FORWARD FOR SELECT Id FROM platform.Tenants WHERE IsActive=1;
BEGIN TRY
    OPEN tenants; FETCH NEXT FROM tenants INTO @Tenant;
    WHILE @@FETCH_STATUS=0
    BEGIN
        EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Tenant;
        EXEC iam.usp_Tenant_SeedRoles @Now=@Now;
        EXEC attendance.usp_Tenant_SeedDefaults @Now=@Now;
        EXEC leave.usp_Tenant_SeedDefaults @Now=@Now;
        EXEC iam.usp_PlatformOwner_JoinTenant @Now=@Now;
        FETCH NEXT FROM tenants INTO @Tenant;
    END;
    CLOSE tenants; DEALLOCATE tenants;
    EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior;
END TRY
BEGIN CATCH
    IF CURSOR_STATUS('local','tenants')>=0 CLOSE tenants;
    IF CURSOR_STATUS('local','tenants')>=-1 DEALLOCATE tenants;
    EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Prior;
    THROW;
END CATCH;
GO
