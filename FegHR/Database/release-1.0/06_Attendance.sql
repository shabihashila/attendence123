-- attendance: geo-fenced self-service punches with a photo, the day-shaped log derived from
-- them, work schedules, holidays and work locations. EmployeeId refers to org.Employees by id.
IF OBJECT_ID(N'attendance.Settings', N'U') IS NULL
BEGIN
    -- One row per company; absent means the defaults below.
    CREATE TABLE [attendance].[Settings](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Settings PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_Settings_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        RequireSelfie BIT NOT NULL CONSTRAINT DF_Settings_RequireSelfie DEFAULT 1,
        RequireLocation BIT NOT NULL CONSTRAINT DF_Settings_RequireLocation DEFAULT 1,
        -- Enforce: a punch outside every work location is refused.
        -- Flag:    it is recorded and sent to the review queue.
        -- Off:     location is recorded but not judged.
        GeofenceMode NVARCHAR(10) NOT NULL CONSTRAINT DF_Settings_GeofenceMode DEFAULT N'Enforce',
        MaxAccuracyMeters INT NOT NULL CONSTRAINT DF_Settings_MaxAccuracyMeters DEFAULT 200,
        DebounceSeconds INT NOT NULL CONSTRAINT DF_Settings_DebounceSeconds DEFAULT 90,
        MinInToOutMinutes INT NOT NULL CONSTRAINT DF_Settings_MinInToOutMinutes DEFAULT 5,
        CONSTRAINT UQ_Settings_TenantId UNIQUE(TenantId),
        CONSTRAINT CK_Settings_GeofenceMode CHECK(GeofenceMode IN(N'Enforce',N'Flag',N'Off')),
        CONSTRAINT CK_Settings_MaxAccuracyMeters CHECK(MaxAccuracyMeters BETWEEN 10 AND 5000),
        CONSTRAINT CK_Settings_DebounceSeconds CHECK(DebounceSeconds BETWEEN 0 AND 600),
        CONSTRAINT CK_Settings_MinInToOutMinutes CHECK(MinInToOutMinutes BETWEEN 0 AND 240),
        IsActive BIT NOT NULL CONSTRAINT DF_Settings_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_Settings_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id)
    );
END;
GO
-- A geofence a punch is matched against. BranchId NULL applies company-wide. Coordinates are
-- DECIMAL(9,6) (about 0.11 m), exact, so distances are reproducible. A RemoteAllowed location may
-- have no centre and be anchored by AllowedPublicIps only.
IF OBJECT_ID(N'attendance.WorkLocations', N'U') IS NULL
BEGIN
    CREATE TABLE [attendance].[WorkLocations](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkLocations PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_WorkLocations_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        BranchId INT NULL,
        Name NVARCHAR(150) NOT NULL,
        LocationType NVARCHAR(30) NOT NULL CONSTRAINT DF_WorkLocations_LocationType DEFAULT N'Office',
        Latitude DECIMAL(9,6) NULL,
        Longitude DECIMAL(9,6) NULL,
        RadiusMeters INT NOT NULL CONSTRAINT DF_WorkLocations_RadiusMeters DEFAULT 150,
        AllowedPublicIps NVARCHAR(500) NULL,
        Address NVARCHAR(500) NULL,
        CONSTRAINT UQ_WorkLocations_TenantId_Name UNIQUE(TenantId,Name),
        CONSTRAINT CK_WorkLocations_LocationType CHECK(LocationType IN(N'Office',N'Site',N'RemoteAllowed')),
        CONSTRAINT CK_WorkLocations_Radius CHECK(RadiusMeters BETWEEN 10 AND 100000),
        CONSTRAINT CK_WorkLocations_Lat CHECK(Latitude IS NULL OR Latitude BETWEEN -90 AND 90),
        CONSTRAINT CK_WorkLocations_Lng CHECK(Longitude IS NULL OR Longitude BETWEEN -180 AND 180),
        CONSTRAINT CK_WorkLocations_LatLngPair CHECK((Latitude IS NULL AND Longitude IS NULL) OR (Latitude IS NOT NULL AND Longitude IS NOT NULL)),
        IsActive BIT NOT NULL CONSTRAINT DF_WorkLocations_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_WorkLocations_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id),
        CONSTRAINT UQ_WorkLocations_TenantId_Id UNIQUE(TenantId, Id)
    );
END;
GO
IF OBJECT_ID(N'attendance.WorkSchedules', N'U') IS NULL
BEGIN
    CREATE TABLE [attendance].[WorkSchedules](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkSchedules PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_WorkSchedules_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        Name NVARCHAR(150) NOT NULL,
        Description NVARCHAR(500) NULL,
        IsDefault BIT NOT NULL CONSTRAINT DF_WorkSchedules_IsDefault DEFAULT 0,
        CONSTRAINT UQ_WorkSchedules_TenantId_Name UNIQUE(TenantId,Name),
        IsActive BIT NOT NULL CONSTRAINT DF_WorkSchedules_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_WorkSchedules_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id),
        CONSTRAINT UQ_WorkSchedules_TenantId_Id UNIQUE(TenantId, Id)
    );
    -- At most one default schedule per company.
    CREATE UNIQUE INDEX UQ_WorkSchedules_TenantId_Default ON attendance.WorkSchedules(TenantId) WHERE IsDefault=1;
END;
GO
-- One row per schedule and weekday. DayOfWeek: 0 = Sunday ... 6 = Saturday.
-- A check-in after StartTime + GraceMinutes is late.
IF OBJECT_ID(N'attendance.WorkScheduleDays', N'U') IS NULL
BEGIN
    CREATE TABLE [attendance].[WorkScheduleDays](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkScheduleDays PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_WorkScheduleDays_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        WorkScheduleId INT NOT NULL,
        DayOfWeek TINYINT NOT NULL,
        IsWorkingDay BIT NOT NULL CONSTRAINT DF_WorkScheduleDays_IsWorkingDay DEFAULT 1,
        StartTime TIME(0) NOT NULL CONSTRAINT DF_WorkScheduleDays_StartTime DEFAULT '09:00',
        EndTime TIME(0) NOT NULL CONSTRAINT DF_WorkScheduleDays_EndTime DEFAULT '18:00',
        GraceMinutes INT NOT NULL CONSTRAINT DF_WorkScheduleDays_GraceMinutes DEFAULT 15,
        CONSTRAINT UQ_WorkScheduleDays_TenantId_Schedule_Day UNIQUE(TenantId,WorkScheduleId,DayOfWeek),
        CONSTRAINT FK_WorkScheduleDays_WorkSchedules FOREIGN KEY(TenantId,WorkScheduleId) REFERENCES attendance.WorkSchedules(TenantId,Id) ON DELETE CASCADE,
        CONSTRAINT CK_WorkScheduleDays_DayOfWeek CHECK(DayOfWeek BETWEEN 0 AND 6),
        CONSTRAINT CK_WorkScheduleDays_GraceMinutes CHECK(GraceMinutes BETWEEN 0 AND 480),
        IsActive BIT NOT NULL CONSTRAINT DF_WorkScheduleDays_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_WorkScheduleDays_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id)
    );
END;
GO
-- Effective-dated schedule of an employee. Without a row the company default applies.
IF OBJECT_ID(N'attendance.EmployeeSchedules', N'U') IS NULL
BEGIN
    CREATE TABLE [attendance].[EmployeeSchedules](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmployeeSchedules PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_EmployeeSchedules_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        EmployeeId INT NOT NULL,
        WorkScheduleId INT NOT NULL,
        EffectiveFrom DATE NOT NULL,
        EffectiveTo DATE NULL,
        Remarks NVARCHAR(500) NULL,
        CONSTRAINT FK_EmployeeSchedules_WorkSchedules FOREIGN KEY(TenantId,WorkScheduleId) REFERENCES attendance.WorkSchedules(TenantId,Id),
        CONSTRAINT CK_EmployeeSchedules_Dates CHECK(EffectiveTo IS NULL OR EffectiveTo>=EffectiveFrom),
        IsActive BIT NOT NULL CONSTRAINT DF_EmployeeSchedules_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_EmployeeSchedules_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id)
    );
    CREATE INDEX IX_EmployeeSchedules_TenantId_EmployeeId ON attendance.EmployeeSchedules(TenantId,EmployeeId,EffectiveFrom DESC) INCLUDE(EffectiveTo,WorkScheduleId,IsActive);
END;
GO
IF OBJECT_ID(N'attendance.Holidays', N'U') IS NULL
BEGIN
    CREATE TABLE [attendance].[Holidays](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Holidays PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_Holidays_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        Title NVARCHAR(200) NOT NULL,
        HolidayDate DATE NOT NULL,
        Remarks NVARCHAR(500) NULL,
        CONSTRAINT UQ_Holidays_TenantId_Date_Title UNIQUE(TenantId,HolidayDate,Title),
        IsActive BIT NOT NULL CONSTRAINT DF_Holidays_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_Holidays_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id)
    );
END;
GO
-- The handsets an employee punches from. The first one is approved automatically; later ones
-- are recorded unapproved and their punches are flagged for review.
IF OBJECT_ID(N'attendance.EmployeeDevices', N'U') IS NULL
BEGIN
    CREATE TABLE [attendance].[EmployeeDevices](
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmployeeDevices PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_EmployeeDevices_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        EmployeeId INT NOT NULL,
        DeviceId NVARCHAR(100) NOT NULL,
        Label NVARCHAR(150) NULL,
        Platform NVARCHAR(30) NULL,
        IsApproved BIT NOT NULL CONSTRAINT DF_EmployeeDevices_IsApproved DEFAULT 0,
        ApprovedBy INT NULL,
        ApprovedAt DATETIMEOFFSET(3) NULL,
        FirstSeenAt DATETIMEOFFSET(3) NOT NULL,
        LastSeenAt DATETIMEOFFSET(3) NOT NULL,
        CONSTRAINT UQ_EmployeeDevices_TenantId_Employee_Device UNIQUE(TenantId,EmployeeId,DeviceId),
        IsActive BIT NOT NULL CONSTRAINT DF_EmployeeDevices_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_EmployeeDevices_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id)
    );
END;
GO
-- Append-only raw punch events. The client-claimed columns are untrusted observations; the
-- server-resolved columns are written by attendance.usp_Punch_Record only. Only the review
-- columns are ever updated.
IF OBJECT_ID(N'attendance.Punches', N'U') IS NULL
BEGIN
    CREATE TABLE [attendance].[Punches](
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Punches PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_Punches_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        EmployeeId INT NOT NULL,
        -- client-claimed
        Latitude DECIMAL(9,6) NULL,
        Longitude DECIMAL(9,6) NULL,
        AccuracyMeters FLOAT NULL,
        IsMockLocation BIT NULL,
        DeviceId NVARCHAR(100) NULL,
        Platform NVARCHAR(30) NULL,
        ClientPunchedAt DATETIMEOFFSET(3) NULL,
        SelfieKey NVARCHAR(400) NULL,
        Remarks NVARCHAR(500) NULL,
        -- server-resolved
        PunchedAt DATETIMEOFFSET(3) NOT NULL,
        LogDate DATE NOT NULL,          -- calendar date in the company's time zone
        LocalTime TIME(0) NOT NULL,     -- wall-clock time in the company's time zone
        Direction NVARCHAR(10) NOT NULL,
        Source NVARCHAR(30) NOT NULL CONSTRAINT DF_Punches_Source DEFAULT N'Web',
        IpAddress NVARCHAR(64) NULL,
        MatchedLocationId INT NULL,
        DistanceMeters FLOAT NULL,
        LocationStatus NVARCHAR(30) NOT NULL,
        FlagsJson NVARCHAR(1000) NOT NULL CONSTRAINT DF_Punches_FlagsJson DEFAULT N'[]',
        -- review
        IsReviewed BIT NOT NULL CONSTRAINT DF_Punches_IsReviewed DEFAULT 0,
        ReviewedBy INT NULL,
        ReviewedAt DATETIMEOFFSET(3) NULL,
        ReviewNote NVARCHAR(500) NULL,
        CONSTRAINT FK_Punches_WorkLocations FOREIGN KEY(TenantId,MatchedLocationId) REFERENCES attendance.WorkLocations(TenantId,Id),
        CONSTRAINT CK_Punches_Direction CHECK(Direction IN(N'In',N'Out')),
        CONSTRAINT CK_Punches_LocationStatus CHECK(LocationStatus IN(N'InOffice',N'Remote',N'OutsideGeofence',N'Unverified',N'Suspicious')),
        CONSTRAINT CK_Punches_Lat CHECK(Latitude IS NULL OR Latitude BETWEEN -90 AND 90),
        CONSTRAINT CK_Punches_Lng CHECK(Longitude IS NULL OR Longitude BETWEEN -180 AND 180),
        CONSTRAINT CK_Punches_FlagsJson CHECK(ISJSON(FlagsJson)=1),
        IsActive BIT NOT NULL CONSTRAINT DF_Punches_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_Punches_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id)
    );
    CREATE INDEX IX_Punches_TenantId_Employee_Date ON attendance.Punches(TenantId,EmployeeId,LogDate) INCLUDE(PunchedAt,LocalTime,Direction,LocationStatus);
    CREATE INDEX IX_Punches_TenantId_Review ON attendance.Punches(TenantId,IsReviewed,LogDate) INCLUDE(EmployeeId,LocationStatus,PunchedAt);
END;
GO
-- The day-shaped projection the reports read: one row per employee and date. Derived from the
-- punches of that day; a row keyed in by HR (Source = Manual) is never overwritten by a punch.
IF OBJECT_ID(N'attendance.DailyLogs', N'U') IS NULL
BEGIN
    CREATE TABLE [attendance].[DailyLogs](
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DailyLogs PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_DailyLogs_TenantId DEFAULT CONVERT(int, SESSION_CONTEXT(N'FegHR.TenantId')),
        EmployeeId INT NOT NULL,
        LogDate DATE NOT NULL,
        InTime TIME(0) NULL,
        OutTime TIME(0) NULL,
        Source NVARCHAR(30) NOT NULL,
        Remarks NVARCHAR(500) NULL,
        CONSTRAINT UQ_DailyLogs_TenantId_Employee_Date UNIQUE(TenantId,EmployeeId,LogDate),
        IsActive BIT NOT NULL CONSTRAINT DF_DailyLogs_IsActive DEFAULT 1,
        CreatedAt DATETIMEOFFSET(3) NOT NULL,
        CreatedBy INT NULL,
        UpdatedAt DATETIMEOFFSET(3) NULL,
        UpdatedBy INT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_DailyLogs_Tenants FOREIGN KEY(TenantId) REFERENCES platform.Tenants(Id)
    );
    CREATE INDEX IX_DailyLogs_TenantId_Date ON attendance.DailyLogs(TenantId,LogDate) INCLUDE(EmployeeId,InTime,OutTime);
END;
GO
EXEC security.usp_ApplyTenantPolicy @Schema=N'attendance';
GO
-- The shift of an employee on a date: the employee's own schedule when one is in force,
-- otherwise the company default. No row means no schedule is configured, which the reports
-- read as an ordinary working day starting 09:00 with 15 minutes of grace.
CREATE OR ALTER FUNCTION attendance.fn_ScheduleForDate(@EmployeeId INT,@Date DATE)
RETURNS TABLE AS RETURN
    SELECT TOP(1) d.IsWorkingDay,d.StartTime,d.EndTime,d.GraceMinutes,
           CONVERT(time(0),DATEADD(minute,d.GraceMinutes,d.StartTime)) AS LateAfter,s.Name AS ScheduleName
    FROM attendance.WorkSchedules s
    JOIN attendance.WorkScheduleDays d ON d.TenantId=s.TenantId AND d.WorkScheduleId=s.Id
         AND d.DayOfWeek=CONVERT(tinyint,DATEDIFF(day,'19000107',@Date)%7)
    OUTER APPLY (SELECT TOP(1) es.WorkScheduleId FROM attendance.EmployeeSchedules es
                 WHERE es.EmployeeId=@EmployeeId AND es.IsActive=1 AND es.EffectiveFrom<=@Date
                   AND (es.EffectiveTo IS NULL OR es.EffectiveTo>=@Date)
                 ORDER BY es.EffectiveFrom DESC,es.Id DESC) own
    WHERE s.IsActive=1 AND ((own.WorkScheduleId IS NOT NULL AND s.Id=own.WorkScheduleId)
                            OR (own.WorkScheduleId IS NULL AND s.IsDefault=1));
GO
CREATE OR ALTER PROCEDURE attendance.usp_Schedule_ForDate @EmployeeId INT,@Date DATE
AS
BEGIN
    SET NOCOUNT ON;
    SELECT IsWorkingDay,StartTime,EndTime,GraceMinutes,ScheduleName FROM attendance.fn_ScheduleForDate(@EmployeeId,@Date);
END;
GO
-- Rebuilds the day row of one employee from that day's punches: first In, last Out.
CREATE OR ALTER PROCEDURE attendance.usp_DailyLog_Reconcile
    @EmployeeId INT,@LogDate DATE,@Source NVARCHAR(30),@Now DATETIMEOFFSET(3),@ActorId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @In TIME(0),@Out TIME(0);
    SELECT TOP(1) @In=LocalTime FROM attendance.Punches WHERE EmployeeId=@EmployeeId AND LogDate=@LogDate AND Direction=N'In' AND IsActive=1 ORDER BY PunchedAt;
    SELECT TOP(1) @Out=LocalTime FROM attendance.Punches WHERE EmployeeId=@EmployeeId AND LogDate=@LogDate AND Direction=N'Out' AND IsActive=1 ORDER BY PunchedAt DESC;
    IF EXISTS(SELECT 1 FROM attendance.DailyLogs WHERE EmployeeId=@EmployeeId AND LogDate=@LogDate AND Source=N'Manual') RETURN;
    UPDATE attendance.DailyLogs SET InTime=@In,OutTime=@Out,Source=@Source,UpdatedAt=@Now,UpdatedBy=@ActorId
    WHERE EmployeeId=@EmployeeId AND LogDate=@LogDate;
    IF @@ROWCOUNT=0
        INSERT attendance.DailyLogs(EmployeeId,LogDate,InTime,OutTime,Source,CreatedAt,CreatedBy)
        VALUES(@EmployeeId,@LogDate,@In,@Out,@Source,@Now,@ActorId);
END;
GO
-- Records one self-service punch. The direction, the location verdict and the day row are
-- decided here, never by the client. @LogDate and @LocalTime are @Now in the company's time
-- zone, computed by the application clock.
--   52001 a photo is required          52002 location is required
--   52003 outside every work location  52004 the device reports a simulated location
CREATE OR ALTER PROCEDURE attendance.usp_Punch_Record
    @EmployeeId INT,@Latitude DECIMAL(9,6)=NULL,@Longitude DECIMAL(9,6)=NULL,@AccuracyMeters FLOAT=NULL,
    @IsMockLocation BIT=NULL,@DeviceId NVARCHAR(100)=NULL,@DeviceLabel NVARCHAR(150)=NULL,@Platform NVARCHAR(30)=NULL,
    @ClientPunchedAt DATETIMEOFFSET(3)=NULL,@SelfieKey NVARCHAR(400)=NULL,@Remarks NVARCHAR(500)=NULL,
    @IpAddress NVARCHAR(64)=NULL,@Now DATETIMEOFFSET(3),@LogDate DATE,@LocalTime TIME(0),@ActorId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @TenantId INT=TRY_CONVERT(int,SESSION_CONTEXT(N'FegHR.TenantId'));
    IF ISNULL(@TenantId,0)<=0 OR @Now IS NULL OR @LogDate IS NULL OR @LocalTime IS NULL OR ISNULL(@EmployeeId,0)<=0
        THROW 52000,'Tenant context, employee and timestamps are required.',1;
    SET @DeviceId=NULLIF(LTRIM(RTRIM(@DeviceId)),N'');
    SET @SelfieKey=NULLIF(LTRIM(RTRIM(@SelfieKey)),N'');
    IF @Latitude IS NULL OR @Longitude IS NULL BEGIN SET @Latitude=NULL; SET @Longitude=NULL; END;

    DECLARE @RequireSelfie BIT=1,@RequireLocation BIT=1,@Mode NVARCHAR(10)=N'Enforce',@MaxAccuracy INT=200,
            @Debounce INT=90,@MinGap INT=5;
    SELECT @RequireSelfie=RequireSelfie,@RequireLocation=RequireLocation,@Mode=GeofenceMode,@MaxAccuracy=MaxAccuracyMeters,
           @Debounce=DebounceSeconds,@MinGap=MinInToOutMinutes
    FROM attendance.Settings WHERE IsActive=1;

    DECLARE @Lock INT,@Resource NVARCHAR(255)=CONCAT(N'Attendance.Punch.',@TenantId,N'.',@EmployeeId);
    DECLARE @Flags TABLE(Flag NVARCHAR(40) PRIMARY KEY);
    DECLARE @PunchId BIGINT,@Duplicate BIT=0,@Direction NVARCHAR(10),@Status NVARCHAR(30),@LocationId INT,@LocationName NVARCHAR(150),
            @LocationType NVARCHAR(30),@Distance FLOAT,@Message NVARCHAR(400),@Source NVARCHAR(30),@FlagsJson NVARCHAR(1000);
    BEGIN TRY
        BEGIN TRANSACTION;
        EXEC @Lock=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
        IF @Lock<0 THROW 52005,'Attendance is busy; try again.',1;

        -- Idempotency: a double tap or a retry on a weak connection returns the original punch.
        SELECT TOP(1) @PunchId=Id FROM attendance.Punches
        WHERE EmployeeId=@EmployeeId AND LogDate=@LogDate AND IsActive=1 AND PunchedAt>=DATEADD(second,-@Debounce,@Now)
        ORDER BY PunchedAt DESC;
        IF @PunchId IS NOT NULL SET @Duplicate=1;
        ELSE
        BEGIN
            IF @RequireSelfie=1 AND @SelfieKey IS NULL THROW 52001,'A photo is required to record attendance.',1;
            IF @RequireLocation=1 AND @Latitude IS NULL
                THROW 52002,'Your location is required to record attendance. Allow location access and try again.',1;

            -- ---- location verdict ----
            DECLARE @HasFences BIT=CASE WHEN EXISTS(SELECT 1 FROM attendance.WorkLocations
                WHERE IsActive=1 AND (Latitude IS NOT NULL OR NULLIF(AllowedPublicIps,N'') IS NOT NULL)) THEN 1 ELSE 0 END;
            IF @Latitude IS NOT NULL
            BEGIN
                DECLARE @Here geography=geography::Point(@Latitude,@Longitude,4326);
                -- Nearest location the fix is inside of; otherwise simply the nearest one.
                SELECT TOP(1) @LocationId=w.Id,@LocationName=w.Name,@LocationType=w.LocationType,@Distance=x.Meters,
                       @Status=CASE WHEN x.Meters<=w.RadiusMeters THEN CASE w.LocationType WHEN N'RemoteAllowed' THEN N'Remote' ELSE N'InOffice' END
                                    ELSE N'OutsideGeofence' END
                FROM attendance.WorkLocations w
                CROSS APPLY (SELECT @Here.STDistance(geography::Point(w.Latitude,w.Longitude,4326)) AS Meters) x
                WHERE w.IsActive=1 AND w.Latitude IS NOT NULL
                ORDER BY CASE WHEN x.Meters<=w.RadiusMeters THEN 0 ELSE 1 END,x.Meters;
                IF @AccuracyMeters IS NOT NULL AND @AccuracyMeters>@MaxAccuracy INSERT @Flags VALUES(N'ACCURACY_TOO_COARSE');
            END
            ELSE INSERT @Flags VALUES(N'NO_LOCATION');

            -- A recognised office network vouches for a punch the GPS could not place.
            IF ISNULL(@Status,N'')<>N'InOffice' AND @IpAddress IS NOT NULL
            BEGIN
                DECLARE @IpLocation INT,@IpName NVARCHAR(150),@IpType NVARCHAR(30);
                SELECT TOP(1) @IpLocation=w.Id,@IpName=w.Name,@IpType=w.LocationType FROM attendance.WorkLocations w
                WHERE w.IsActive=1 AND EXISTS(SELECT 1 FROM STRING_SPLIT(w.AllowedPublicIps,N',') s WHERE LTRIM(RTRIM(s.value))=@IpAddress)
                ORDER BY w.Id;
                IF @IpLocation IS NOT NULL
                BEGIN
                    SELECT @LocationId=@IpLocation,@LocationName=@IpName,@LocationType=@IpType,@Distance=NULL,
                           @Status=CASE @IpType WHEN N'RemoteAllowed' THEN N'Remote' ELSE N'InOffice' END;
                    INSERT @Flags VALUES(N'IP_MATCHED');
                END;
            END;

            IF @Status IS NULL
            BEGIN
                SET @Status=N'Unverified';
                IF @HasFences=0 INSERT @Flags VALUES(N'NO_LOCATION_CONFIGURED');
            END;
            IF @Status=N'OutsideGeofence' INSERT @Flags VALUES(N'OUTSIDE_ALL_FENCES');
            IF @Status NOT IN(N'InOffice',N'Remote') SET @LocationId=NULL;   -- the name stays for the message
            IF @IsMockLocation=1 BEGIN INSERT @Flags VALUES(N'MOCK_LOCATION'); SET @Status=N'Suspicious'; END;

            -- ---- enforcement ----
            IF @Mode=N'Off' AND @Status IN(N'OutsideGeofence',N'Unverified') SET @Status=N'Unverified';
            IF @Mode=N'Enforce'
            BEGIN
                IF @IsMockLocation=1
                    THROW 52004,'This device is reporting a simulated location. Turn off mock location and try again.',1;
                IF @HasFences=1 AND @Status NOT IN(N'InOffice',N'Remote')
                BEGIN
                    SET @Message=CASE
                        WHEN @Distance IS NULL THEN N'Your location could not be confirmed. Attendance can only be recorded at an approved work location.'
                        ELSE CONCAT(N'You are ',CASE WHEN @Distance<1000 THEN CONCAT(CONVERT(int,ROUND(@Distance,0)),N' m')
                                                     ELSE CONCAT(CONVERT(decimal(9,1),@Distance/1000.0),N' km') END,
                                    N' from ',@LocationName,N'. Attendance can only be recorded inside an approved work location.') END;
                    THROW 52003,@Message,1;
                END;
            END;

            -- ---- direction ----
            DECLARE @LastDirection NVARCHAR(10),@LastAt DATETIMEOFFSET(3);
            SELECT TOP(1) @LastDirection=Direction,@LastAt=PunchedAt FROM attendance.Punches
            WHERE EmployeeId=@EmployeeId AND LogDate=@LogDate AND IsActive=1 ORDER BY PunchedAt DESC;
            SET @Direction=CASE WHEN @LastDirection IS NULL THEN N'In'
                                WHEN @LastDirection=N'In' AND DATEDIFF(second,@LastAt,@Now)<@MinGap*60 THEN N'In'
                                WHEN @LastDirection=N'In' THEN N'Out' ELSE N'In' END;

            -- ---- device binding ----
            IF @DeviceId IS NULL INSERT @Flags VALUES(N'UNKNOWN_DEVICE');
            ELSE
            BEGIN
                DECLARE @Approved BIT;
                SELECT @Approved=IsApproved FROM attendance.EmployeeDevices WHERE EmployeeId=@EmployeeId AND DeviceId=@DeviceId;
                IF @Approved IS NULL
                BEGIN
                    SET @Approved=CASE WHEN EXISTS(SELECT 1 FROM attendance.EmployeeDevices WHERE EmployeeId=@EmployeeId AND IsActive=1) THEN 0 ELSE 1 END;
                    INSERT attendance.EmployeeDevices(EmployeeId,DeviceId,Label,Platform,IsApproved,ApprovedAt,FirstSeenAt,LastSeenAt,CreatedAt,CreatedBy)
                    VALUES(@EmployeeId,@DeviceId,@DeviceLabel,@Platform,@Approved,CASE WHEN @Approved=1 THEN @Now END,@Now,@Now,@Now,@ActorId);
                END
                ELSE UPDATE attendance.EmployeeDevices SET LastSeenAt=@Now WHERE EmployeeId=@EmployeeId AND DeviceId=@DeviceId;
                IF @Approved=0 INSERT @Flags VALUES(N'UNAPPROVED_DEVICE');
            END;

            -- ---- impossible travel: faster than an airliner since the last located punch ----
            IF @Latitude IS NOT NULL
            BEGIN
                DECLARE @PrevLat DECIMAL(9,6),@PrevLng DECIMAL(9,6),@PrevAt DATETIMEOFFSET(3),@Moved FLOAT;
                SELECT TOP(1) @PrevLat=Latitude,@PrevLng=Longitude,@PrevAt=PunchedAt FROM attendance.Punches
                WHERE EmployeeId=@EmployeeId AND Latitude IS NOT NULL AND IsActive=1 ORDER BY PunchedAt DESC;
                IF @PrevAt IS NOT NULL AND @PrevAt<@Now
                BEGIN
                    SET @Moved=@Here.STDistance(geography::Point(@PrevLat,@PrevLng,4326));
                    IF @Moved>=1000 AND (@Moved/1000.0)/(DATEDIFF_BIG(millisecond,@PrevAt,@Now)/3600000.0)>900
                    BEGIN
                        INSERT @Flags VALUES(N'IMPOSSIBLE_TRAVEL');
                        SET @Status=N'Suspicious';
                    END;
                END;
            END;

            SET @Source=CASE WHEN @Platform IS NULL OR @Platform IN(N'Web',N'web') THEN N'Web' ELSE N'Mobile' END;
            SET @FlagsJson=ISNULL((SELECT N'['+STRING_AGG(N'"'+Flag+N'"',N',')+N']' FROM @Flags),N'[]');
            INSERT attendance.Punches(EmployeeId,Latitude,Longitude,AccuracyMeters,IsMockLocation,DeviceId,Platform,ClientPunchedAt,SelfieKey,Remarks,
                PunchedAt,LogDate,LocalTime,Direction,Source,IpAddress,MatchedLocationId,DistanceMeters,LocationStatus,FlagsJson,CreatedAt,CreatedBy)
            VALUES(@EmployeeId,@Latitude,@Longitude,@AccuracyMeters,@IsMockLocation,@DeviceId,@Platform,@ClientPunchedAt,@SelfieKey,NULLIF(LTRIM(RTRIM(@Remarks)),N''),
                @Now,@LogDate,@LocalTime,@Direction,@Source,@IpAddress,@LocationId,@Distance,@Status,@FlagsJson,@Now,@ActorId);
            SET @PunchId=CONVERT(bigint,SCOPE_IDENTITY());
            EXEC attendance.usp_DailyLog_Reconcile @EmployeeId=@EmployeeId,@LogDate=@LogDate,@Source=@Source,@Now=@Now,@ActorId=@ActorId;
        END;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
    SELECT p.Id,p.PunchedAt,p.LogDate,p.LocalTime,p.Direction,p.LocationStatus,p.DistanceMeters,p.FlagsJson,p.SelfieKey,
           ISNULL(w.Name,@LocationName) AS LocationName,@Duplicate AS WasDuplicate
    FROM attendance.Punches p
    LEFT JOIN attendance.WorkLocations w ON w.TenantId=p.TenantId AND w.Id=p.MatchedLocationId
    WHERE p.Id=@PunchId;
END;
GO
