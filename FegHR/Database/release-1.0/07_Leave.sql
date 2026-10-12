-- leave: leave types, applications with two approval steps (line manager, then HR) and yearly
-- balance adjustments. EmployeeId refers to org.Employees by id.
IF OBJECT_ID(N'leave.LeaveTypes', N'U') IS NULL
BEGIN
    CREATE TABLE [leave].[LeaveTypes](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LeaveTypes PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_LeaveTypes_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        Code NVARCHAR(20) NOT NULL,
        Name NVARCHAR(100) NOT NULL,
        YearlyQuota DECIMAL(5,1) NULL,   -- NULL = no limit
        IsPaid BIT NOT NULL CONSTRAINT DF_LeaveTypes_IsPaid DEFAULT 1,
        CONSTRAINT UQ_LeaveTypes_TenantId_Code UNIQUE(TenantId,Code),
        CONSTRAINT CK_LeaveTypes_YearlyQuota CHECK(YearlyQuota IS NULL OR YearlyQuota>=0),
        IsActive BIT NOT NULL CONSTRAINT DF_LeaveTypes_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_LeaveTypes_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id),
        CONSTRAINT UQ_LeaveTypes_TenantId_Id UNIQUE(TenantId, Id)
    );
END;
GO
-- Status Pending moves through CurrentStep Manager -> HR and ends Approved; it can end Rejected
-- at either step or be Cancelled by the applicant while pending. Level1* records the manager
-- step, Decided* the final approval or the rejection.
IF OBJECT_ID(N'leave.Applications', N'U') IS NULL
BEGIN
    CREATE TABLE [leave].[Applications](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Applications PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_Applications_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        EmployeeId INT NOT NULL,
        LeaveTypeId INT NOT NULL,
        DayType NVARCHAR(20) NOT NULL CONSTRAINT DF_Applications_DayType DEFAULT N'Full',
        StartDate DATE NOT NULL,
        EndDate DATE NOT NULL,
        Days DECIMAL(5,1) NOT NULL,
        AppliedOn DATE NOT NULL,
        Reason NVARCHAR(1000) NULL,
        AddressDuringLeave NVARCHAR(500) NULL,
        AttachmentKey NVARCHAR(400) NULL,
        Status NVARCHAR(20) NOT NULL CONSTRAINT DF_Applications_Status DEFAULT N'Pending',
        CurrentStep NVARCHAR(20) NOT NULL CONSTRAINT DF_Applications_CurrentStep DEFAULT N'Manager',
        Level1By INT NULL,
        Level1At DATETIMEOFFSET(3) NULL,
        Level1Remarks NVARCHAR(500) NULL,
        DecidedBy INT NULL,
        DecidedAt DATETIMEOFFSET(3) NULL,
        DecisionRemarks NVARCHAR(500) NULL,
        CONSTRAINT FK_Applications_LeaveTypes FOREIGN KEY(TenantId,LeaveTypeId) REFERENCES leave.LeaveTypes(TenantId,Id),
        CONSTRAINT CK_Applications_DayType CHECK(DayType IN(N'Full',N'HalfFirst',N'HalfSecond')),
        CONSTRAINT CK_Applications_Dates CHECK(EndDate>=StartDate AND (DayType=N'Full' OR StartDate=EndDate)),
        CONSTRAINT CK_Applications_Days CHECK(Days>0),
        CONSTRAINT CK_Applications_Status CHECK(Status IN(N'Pending',N'Approved',N'Rejected',N'Cancelled')),
        CONSTRAINT CK_Applications_CurrentStep CHECK(CurrentStep IN(N'Manager',N'HR',N'Done')),
        IsActive BIT NOT NULL CONSTRAINT DF_Applications_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_Applications_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id),
        CONSTRAINT UQ_Applications_TenantId_Id UNIQUE(TenantId, Id)
    );
    CREATE INDEX IX_Applications_TenantId_Employee ON leave.Applications(TenantId,EmployeeId,StartDate) INCLUDE(EndDate,Status,LeaveTypeId,Days);
    CREATE INDEX IX_Applications_TenantId_Status ON leave.Applications(TenantId,Status,CurrentStep) INCLUDE(EmployeeId,StartDate,EndDate);
END;
GO
IF OBJECT_ID(N'leave.Adjustments', N'U') IS NULL
BEGIN
    CREATE TABLE [leave].[Adjustments](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Adjustments PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_Adjustments_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        EmployeeId INT NOT NULL,
        LeaveTypeId INT NOT NULL,
        [Year] INT NOT NULL,
        Days DECIMAL(5,1) NOT NULL,   -- positive adds to the balance, negative takes away
        Reason NVARCHAR(500) NULL,
        CONSTRAINT FK_Adjustments_LeaveTypes FOREIGN KEY(TenantId,LeaveTypeId) REFERENCES leave.LeaveTypes(TenantId,Id),
        CONSTRAINT CK_Adjustments_Year CHECK([Year] BETWEEN 2000 AND 2200),
        CONSTRAINT CK_Adjustments_Days CHECK(Days<>0),
        IsActive BIT NOT NULL CONSTRAINT DF_Adjustments_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_Adjustments_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id)
    );
    CREATE INDEX IX_Adjustments_TenantId_Employee ON leave.Adjustments(TenantId,EmployeeId,LeaveTypeId,[Year]) INCLUDE(Days,IsActive);
END;
GO
EXEC security.usp_ApplyTenantPolicy @Schema=N'leave';
GO
-- Balance of one employee for a year, per active leave type. An application counts in the year
-- of its start date. Remaining = Quota + Adjustment - Used - Pending; NULL when the type has no limit.
CREATE OR ALTER FUNCTION leave.fn_Balance(@EmployeeId INT,@Year INT)
RETURNS TABLE AS RETURN
    SELECT lt.Id AS LeaveTypeId,lt.Code,lt.Name,lt.IsPaid,lt.YearlyQuota AS Quota,
           ISNULL(a.Days,0) AS Adjustment,ISNULL(u.Used,0) AS Used,ISNULL(u.Pending,0) AS Pending,
           CASE WHEN lt.YearlyQuota IS NULL THEN NULL
                ELSE lt.YearlyQuota+ISNULL(a.Days,0)-ISNULL(u.Used,0)-ISNULL(u.Pending,0) END AS Remaining
    FROM leave.LeaveTypes lt
    OUTER APPLY (SELECT SUM(ad.Days) AS Days FROM leave.Adjustments ad
                 WHERE ad.EmployeeId=@EmployeeId AND ad.LeaveTypeId=lt.Id AND ad.[Year]=@Year AND ad.IsActive=1) a
    OUTER APPLY (SELECT SUM(CASE WHEN ap.Status=N'Approved' THEN ap.Days ELSE 0 END) AS Used,
                        SUM(CASE WHEN ap.Status=N'Pending' THEN ap.Days ELSE 0 END) AS Pending
                 FROM leave.Applications ap
                 WHERE ap.EmployeeId=@EmployeeId AND ap.LeaveTypeId=lt.Id AND YEAR(ap.StartDate)=@Year AND ap.IsActive=1) u
    WHERE lt.IsActive=1;
GO
CREATE OR ALTER PROCEDURE leave.usp_Balance_Get @EmployeeId INT,@Year INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT LeaveTypeId,Code,Name,IsPaid,Quota,Adjustment,Used,Pending,Remaining FROM leave.fn_Balance(@EmployeeId,@Year) ORDER BY Name;
END;
GO
-- Applies for leave. Days are calendar days (half a day for a half-day application).
--   54001 invalid request   54002 overlaps another application   54003 not enough balance
-- @StartAtHr skips the manager step, for an applicant who has no line manager.
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
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
    SELECT @Id AS Id,@Days AS Days;
END;
GO
-- Approves or rejects a pending application. @CanLevel1 / @CanLevel2 are the caller's rights,
-- resolved by the API. A level-2 (HR) approver may finalise from either step; a level-1
-- approver acts on the manager step only and may not decide their own application.
--   54010 not found   54011 already decided   54012 not allowed at this step   54003 balance
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
-- The applicant withdraws their own pending application.
CREATE OR ALTER PROCEDURE leave.usp_Application_Cancel
    @ApplicationId INT,@EmployeeId INT,@Now DATETIMEOFFSET(3),@ActorId INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE leave.Applications SET Status=N'Cancelled',CurrentStep=N'Done',DecidedBy=@ActorId,DecidedAt=@Now,UpdatedAt=@Now,UpdatedBy=@ActorId
    WHERE Id=@ApplicationId AND EmployeeId=@EmployeeId AND Status=N'Pending' AND IsActive=1;
    IF @@ROWCOUNT=0 THROW 54011,'Only your own pending application can be cancelled.',1;
END;
GO
