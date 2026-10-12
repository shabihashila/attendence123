-- DEMO DATA for one company: a sign-in for every role, ten employees, setup rows and a month of
-- attendance and leave, so every screen has something to show. Never run against production.
-- Run by Seed-Demo.ps1, which supplies the company slug and one BCrypt hash for all demo sign-ins.
-- Safe to run again: it stops when the demo employees already exist.
:on error exit
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Slug NVARCHAR(80)=LOWER(LTRIM(RTRIM(N'$(DemoTenantSlug)')));
DECLARE @Hash NVARCHAR(1000)=N'$(DemoPasswordHash)';
DECLARE @Now DATETIMEOFFSET(3)=SYSDATETIMEOFFSET();
DECLARE @Today DATE=CONVERT(date,SWITCHOFFSET(@Now,'+06:00'));
DECLARE @Tenant INT=(SELECT Id FROM platform.Tenants WHERE Slug=@Slug);
IF @Tenant IS NULL THROW 59001,'The demo company slug does not exist.',1;
IF LEN(@Hash)<>60 OR LEFT(@Hash,4) NOT IN(N'$2a$',N'$2b$',N'$2y$') THROW 59002,'A BCrypt hash is required for the demo sign-ins.',1;
EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Tenant;
IF EXISTS(SELECT 1 FROM org.Employees WHERE Code=N'DEMO01')
BEGIN
    EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=NULL;
    PRINT N'Demo data is already present; nothing was changed.';
    RETURN;
END;

BEGIN TRANSACTION;

-- ---- branches, departments, designations ----
IF NOT EXISTS(SELECT 1 FROM platform.Branches WHERE Code=N'CTG')
    INSERT platform.Branches(Code,Name,Address,Phone,CreatedAt) VALUES(N'CTG',N'Chattogram Branch',N'Agrabad C/A, Chattogram',N'01811000000',@Now);
DECLARE @HO INT=(SELECT TOP(1) Id FROM platform.Branches WHERE Code=N'HO'),@CTG INT=(SELECT Id FROM platform.Branches WHERE Code=N'CTG');
IF @HO IS NULL SET @HO=(SELECT TOP(1) Id FROM platform.Branches ORDER BY Id);

INSERT org.Departments(Name,CreatedAt)
SELECT x.Name,@Now FROM (VALUES(N'Human Resources'),(N'Engineering'),(N'Sales'),(N'Finance')) x(Name)
WHERE NOT EXISTS(SELECT 1 FROM org.Departments d WHERE d.Name=x.Name);
INSERT org.Designations(Name,CreatedAt)
SELECT x.Name,@Now FROM (VALUES(N'HR Manager'),(N'Engineering Manager'),(N'Senior Software Engineer'),(N'Software Engineer'),
                               (N'Sales Executive'),(N'Accountant'),(N'Operations Lead'),(N'Intern')) x(Name)
WHERE NOT EXISTS(SELECT 1 FROM org.Designations d WHERE d.Name=x.Name);

-- ---- a sign-in for every role ----
DECLARE @Logins TABLE(Role NVARCHAR(50),Email NVARCHAR(254),Name NVARCHAR(150),UserId INT,MemberId INT);
INSERT @Logins(Role,Email,Name) VALUES
    (N'admin',N'admin.demo.'+@Slug+N'@feghr.test',N'Ayesha Siddiqua'),
    (N'hr',N'hr.demo.'+@Slug+N'@feghr.test',N'Nusrat Jahan'),
    (N'manager',N'manager.demo.'+@Slug+N'@feghr.test',N'Tanvir Ahmed'),
    (N'employee',N'employee.demo.'+@Slug+N'@feghr.test',N'Imran Hossain');
INSERT iam.Users(Email,NormalizedEmail,DisplayName,PasswordHash,SecurityStamp,CreatedAt)
SELECT l.Email,UPPER(l.Email),l.Name,@Hash,NEWID(),@Now FROM @Logins l
WHERE NOT EXISTS(SELECT 1 FROM iam.Users u WHERE u.NormalizedEmail=UPPER(l.Email));
UPDATE l SET UserId=u.Id FROM @Logins l JOIN iam.Users u ON u.NormalizedEmail=UPPER(l.Email);
INSERT iam.TenantUsers(UserId,CreatedAt) SELECT l.UserId,@Now FROM @Logins l
WHERE NOT EXISTS(SELECT 1 FROM iam.TenantUsers tu WHERE tu.UserId=l.UserId);
UPDATE l SET MemberId=tu.Id FROM @Logins l JOIN iam.TenantUsers tu ON tu.UserId=l.UserId;
INSERT iam.UserTenants(UserId,TenantRefId,TenantUserId,IsActive) SELECT l.UserId,@Tenant,l.MemberId,1 FROM @Logins l
WHERE NOT EXISTS(SELECT 1 FROM iam.UserTenants x WHERE x.UserId=l.UserId AND x.TenantRefId=@Tenant);
INSERT iam.UserRoles(TenantUserId,RoleId,CreatedAt) SELECT l.MemberId,r.Id,@Now FROM @Logins l JOIN iam.Roles r ON r.Code=l.Role
WHERE NOT EXISTS(SELECT 1 FROM iam.UserRoles ur WHERE ur.TenantUserId=l.MemberId AND ur.RoleId=r.Id);

-- ---- ten employees ----
DECLARE @People TABLE(Code NVARCHAR(50),Name NVARCHAR(200),Gender NVARCHAR(20),Dob DATE,Phone NVARCHAR(30),Joined DATE,EmpType NVARCHAR(50),
                      Branch INT,Dept NVARCHAR(150),Desig NVARCHAR(150),Manager NVARCHAR(50),Email NVARCHAR(254));
INSERT @People VALUES
    (N'DEMO01',N'Ayesha Siddiqua',N'Female','1986-03-12',N'01711000101','2019-01-06',N'Permanent',@HO,N'Administration',N'Operations Lead',NULL,N'admin.demo.'+@Slug+N'@feghr.test'),
    (N'DEMO02',N'Nusrat Jahan',N'Female','1989-07-25',N'01711000102','2020-02-02',N'Permanent',@HO,N'Human Resources',N'HR Manager',N'DEMO01',N'hr.demo.'+@Slug+N'@feghr.test'),
    (N'DEMO03',N'Tanvir Ahmed',N'Male','1987-11-04',N'01711000103','2019-06-16',N'Permanent',@HO,N'Engineering',N'Engineering Manager',N'DEMO01',N'manager.demo.'+@Slug+N'@feghr.test'),
    (N'DEMO04',N'Imran Hossain',N'Male','1994-05-18',N'01711000104','2022-03-13',N'Permanent',@HO,N'Engineering',N'Software Engineer',N'DEMO03',N'employee.demo.'+@Slug+N'@feghr.test'),
    (N'DEMO05',N'Farhana Akter',N'Female','1992-09-30',N'01711000105','2021-08-01',N'Permanent',@HO,N'Engineering',N'Senior Software Engineer',N'DEMO03',NULL),
    (N'DEMO06',N'Rakibul Islam',N'Male','1996-01-22',N'01711000106','2023-01-08',N'Probation',@HO,N'Engineering',N'Software Engineer',N'DEMO03',NULL),
    (N'DEMO07',N'Sadia Rahman',N'Female','1995-12-09',N'01711000107','2022-10-02',N'Permanent',@CTG,N'Sales',N'Sales Executive',N'DEMO01',NULL),
    (N'DEMO08',N'Mahmudul Hasan',N'Male','1990-04-15',N'01711000108','2020-11-15',N'Permanent',@CTG,N'Sales',N'Sales Executive',N'DEMO01',NULL),
    (N'DEMO09',N'Sharmin Sultana',N'Female','1991-08-27',N'01711000109','2021-04-04',N'Permanent',@HO,N'Finance',N'Accountant',N'DEMO01',NULL),
    (N'DEMO10',N'Arif Chowdhury',N'Male','2001-02-11',N'01711000110','2026-07-05',N'Intern',@HO,N'Engineering',N'Intern',N'DEMO03',NULL);
INSERT org.Employees(Code,FullName,Gender,DateOfBirth,Phone,Email,JoiningDate,EmploymentType,BranchId,DepartmentId,DesignationId,UserId,CreatedAt)
SELECT p.Code,p.Name,p.Gender,p.Dob,p.Phone,ISNULL(p.Email,LOWER(REPLACE(p.Name,N' ',N'.'))+N'@feghr.test'),p.Joined,p.EmpType,p.Branch,
       (SELECT Id FROM org.Departments WHERE Name=p.Dept),(SELECT Id FROM org.Designations WHERE Name=p.Desig),
       (SELECT u.Id FROM iam.Users u WHERE u.NormalizedEmail=UPPER(p.Email)),@Now
FROM @People p;
UPDATE e SET ReportsToId=m.Id FROM org.Employees e JOIN @People p ON p.Code=e.Code JOIN org.Employees m ON m.Code=p.Manager;
DECLARE @Emp TABLE(Id INT PRIMARY KEY,Code NVARCHAR(50),Branch INT,Joined DATE);
INSERT @Emp SELECT Id,Code,BranchId,JoiningDate FROM org.Employees WHERE Code LIKE N'DEMO%';

-- ---- attendance setup ----
IF NOT EXISTS(SELECT 1 FROM attendance.Settings) INSERT attendance.Settings(CreatedAt) VALUES(@Now);
-- "Flag" lets a visitor try a check-in from anywhere and see it land in Punch Review.
UPDATE attendance.Settings SET GeofenceMode=N'Flag';
INSERT attendance.WorkLocations(Name,LocationType,BranchId,Latitude,Longitude,RadiusMeters,Address,CreatedAt)
SELECT x.Name,x.LocationType,x.Branch,x.Lat,x.Lng,x.Radius,x.Address,@Now FROM (VALUES
    (N'Head Office',N'Office',@HO,CONVERT(decimal(9,6),23.780600),CONVERT(decimal(9,6),90.407400),150,N'Gulshan Avenue, Dhaka'),
    (N'Chattogram Branch',N'Office',@CTG,22.323300,91.812300,200,N'Agrabad C/A, Chattogram'),
    (N'Client Site - Uttara',N'Site',NULL,23.875900,90.379500,300,N'Sector 7, Uttara, Dhaka'),
    (N'Home Office (approved)',N'RemoteAllowed',NULL,23.746600,90.376000,500,N'Dhanmondi, Dhaka')) x(Name,LocationType,Branch,Lat,Lng,Radius,Address)
WHERE NOT EXISTS(SELECT 1 FROM attendance.WorkLocations w WHERE w.Name=x.Name);
DECLARE @LocHO INT=(SELECT Id FROM attendance.WorkLocations WHERE Name=N'Head Office'),
        @LocCTG INT=(SELECT Id FROM attendance.WorkLocations WHERE Name=N'Chattogram Branch'),
        @LocHome INT=(SELECT Id FROM attendance.WorkLocations WHERE Name=N'Home Office (approved)');

IF NOT EXISTS(SELECT 1 FROM attendance.WorkSchedules WHERE IsDefault=1)
BEGIN
    INSERT attendance.WorkSchedules(Name,Description,IsDefault,CreatedAt) VALUES(N'General Shift',N'Sunday to Thursday, 09:00 to 18:00',1,@Now);
    INSERT attendance.WorkScheduleDays(WorkScheduleId,DayOfWeek,IsWorkingDay,CreatedAt)
    SELECT CONVERT(int,SCOPE_IDENTITY()),d.Day,CASE WHEN d.Day IN(5,6) THEN 0 ELSE 1 END,@Now FROM (VALUES(0),(1),(2),(3),(4),(5),(6)) d(Day);
END;
IF NOT EXISTS(SELECT 1 FROM attendance.WorkSchedules WHERE Name=N'Morning Shift')
BEGIN
    INSERT attendance.WorkSchedules(Name,Description,IsDefault,CreatedAt) VALUES(N'Morning Shift',N'Sunday to Thursday, 08:00 to 16:00',0,@Now);
    DECLARE @Morning INT=CONVERT(int,SCOPE_IDENTITY());
    INSERT attendance.WorkScheduleDays(WorkScheduleId,DayOfWeek,IsWorkingDay,StartTime,EndTime,GraceMinutes,CreatedAt)
    SELECT @Morning,d.Day,CASE WHEN d.Day IN(5,6) THEN 0 ELSE 1 END,'08:00','16:00',10,@Now FROM (VALUES(0),(1),(2),(3),(4),(5),(6)) d(Day);
    INSERT attendance.EmployeeSchedules(EmployeeId,WorkScheduleId,EffectiveFrom,Remarks,CreatedAt)
    SELECT Id,@Morning,DATEADD(day,-45,@Today),N'Covers the early support window',@Now FROM @Emp WHERE Code=N'DEMO09';
END;
INSERT attendance.Holidays(Title,HolidayDate,Remarks,CreatedAt)
SELECT x.Title,x.D,x.Remarks,@Now FROM (VALUES
    (N'Company Foundation Day',DATEADD(day,-12,@Today),N'Office closed'),
    (N'Victory Day',DATEFROMPARTS(YEAR(@Today),12,16),N'National holiday'),
    (N'International Mother Language Day',DATEFROMPARTS(YEAR(@Today)+1,2,21),N'National holiday'),
    (N'Independence Day',DATEFROMPARTS(YEAR(@Today)+1,3,26),N'National holiday')) x(Title,D,Remarks)
WHERE NOT EXISTS(SELECT 1 FROM attendance.Holidays h WHERE h.HolidayDate=x.D AND h.Title=x.Title);

-- ---- leave: balances, and applications in every state ----
EXEC leave.usp_Tenant_SeedDefaults @Now=@Now;
DECLARE @CL INT=(SELECT Id FROM leave.LeaveTypes WHERE Code=N'CL'),@SL INT=(SELECT Id FROM leave.LeaveTypes WHERE Code=N'SL'),
        @AL INT=(SELECT Id FROM leave.LeaveTypes WHERE Code=N'AL'),@UL INT=(SELECT Id FROM leave.LeaveTypes WHERE Code=N'UL');
DECLARE @HrUser INT=(SELECT UserId FROM @Logins WHERE Role=N'hr'),@MgrUser INT=(SELECT UserId FROM @Logins WHERE Role=N'manager');
INSERT leave.Applications(EmployeeId,LeaveTypeId,DayType,StartDate,EndDate,Days,AppliedOn,Reason,AddressDuringLeave,Status,CurrentStep,
                          Level1By,Level1At,Level1Remarks,DecidedBy,DecidedAt,DecisionRemarks,CreatedAt)
SELECT e.Id,x.TypeId,x.DayType,DATEADD(day,x.S,@Today),DATEADD(day,x.E,@Today),x.Days,DATEADD(day,x.Applied,@Today),x.Reason,x.Address,x.Status,x.Step,
       CASE WHEN x.L1=1 THEN @MgrUser END,CASE WHEN x.L1=1 THEN DATEADD(day,x.Applied+1,@Now) END,CASE WHEN x.L1=1 THEN N'Team is covered' END,
       CASE WHEN x.Status IN(N'Approved',N'Rejected') THEN @HrUser END,CASE WHEN x.Status IN(N'Approved',N'Rejected',N'Cancelled') THEN DATEADD(day,x.Applied+2,@Now) END,
       x.Decision,@Now
FROM (VALUES
    (N'DEMO04',@CL,N'Full',-20,-19,CONVERT(decimal(5,1),2),-27,N'Family programme in Cumilla',N'Cumilla',N'Approved',N'Done',1,N'Approved. Enjoy.'),
    (N'DEMO05',@SL,N'Full',-9,-7,3,-9,N'Fever, doctor advised rest',N'Dhaka',N'Approved',N'Done',1,N'Get well soon'),
    (N'DEMO07',@AL,N'Full',-16,-12,5,-30,N'Annual family trip',N'Cox''s Bazar',N'Approved',N'Done',0,N'Approved'),
    (N'DEMO06',@CL,N'HalfFirst',-5,-5,0.5,-6,N'Bank work in the morning',NULL,N'Approved',N'Done',1,NULL),
    (N'DEMO08',@CL,N'Full',-3,-3,1,-4,N'Personal work',NULL,N'Rejected',N'Done',0,N'Month-end sales closing; please pick another day'),
    (N'DEMO09',@AL,N'Full',4,6,3,-2,N'Sister''s wedding',N'Rajshahi',N'Cancelled',N'Done',0,NULL),
    (N'DEMO04',@AL,N'Full',10,14,5,-1,N'Visiting parents',N'Sylhet',N'Pending',N'Manager',0,NULL),
    (N'DEMO10',@CL,N'Full',3,3,1,0,N'University exam',NULL,N'Pending',N'Manager',0,NULL),
    (N'DEMO05',@CL,N'Full',7,8,2,-1,N'House shifting',N'Dhaka',N'Pending',N'HR',1,NULL),
    (N'DEMO03',@AL,N'Full',20,24,5,-1,N'Planned vacation',N'Bandarban',N'Pending',N'HR',0,NULL),
    (N'DEMO02',@SL,N'Full',1,1,1,0,N'Dental appointment',NULL,N'Pending',N'Manager',0,NULL),
    (N'DEMO08',@UL,N'Full',15,17,3,-1,N'Extended family matter',N'Feni',N'Pending',N'HR',0,NULL)
) x(Code,TypeId,DayType,S,E,Days,Applied,Reason,Address,Status,Step,L1,Decision)
JOIN @Emp e ON e.Code=x.Code;
INSERT leave.Adjustments(EmployeeId,LeaveTypeId,[Year],Days,Reason,CreatedAt)
SELECT e.Id,x.TypeId,YEAR(@Today),x.Days,x.Reason,@Now FROM (VALUES
    (N'DEMO05',@AL,CONVERT(decimal(5,1),3),N'Carried forward from last year'),
    (N'DEMO03',@AL,2,N'Carried forward from last year'),
    (N'DEMO07',@CL,-1,N'Unrecorded absence adjusted against casual leave')) x(Code,TypeId,Days,Reason)
JOIN @Emp e ON e.Code=x.Code;

-- ---- thirty days of attendance ----
-- One row per employee and working day. R1..R3 are independent random numbers that decide
-- whether the person came, how late, and when they left.
DECLARE @Days TABLE(EmployeeId INT,D DATE,InT TIME(0),OutT TIME(0),Loc INT,Odd INT,PRIMARY KEY(EmployeeId,D));
;WITH Cal AS (SELECT TOP(31) DATEADD(day,-(ROW_NUMBER() OVER(ORDER BY (SELECT NULL))-1),@Today) AS D FROM sys.all_objects),
R AS (
    SELECT e.Id,e.Code,e.Branch,c.D,ABS(CHECKSUM(NEWID()))%100 AS R1,ABS(CHECKSUM(NEWID()))%100 AS R2,ABS(CHECKSUM(NEWID()))%100 AS R3
    FROM @Emp e CROSS JOIN Cal c
    WHERE (e.Joined IS NULL OR e.Joined<=c.D)
      AND DATEDIFF(day,'19000107',c.D)%7 NOT IN(5,6)
      AND NOT EXISTS(SELECT 1 FROM attendance.Holidays h WHERE h.HolidayDate=c.D)
      AND NOT EXISTS(SELECT 1 FROM leave.Applications a WHERE a.EmployeeId=e.Id AND a.Status=N'Approved' AND a.DayType=N'Full' AND c.D BETWEEN a.StartDate AND a.EndDate)
)
INSERT @Days(EmployeeId,D,InT,OutT,Loc,Odd)
SELECT Id,D,
       -- most arrive 08:45-09:12; about one in five comes in late; DEMO09 works the 08:00 shift
       DATEADD(minute,CASE WHEN R2<78 THEN R2%28-15 ELSE 16+R2%45 END,CONVERT(time(0),CASE WHEN Code=N'DEMO09' THEN '08:00' ELSE '09:00' END)),
       CASE WHEN D=@Today THEN NULL ELSE DATEADD(minute,R3%50-5,CONVERT(time(0),CASE WHEN Code=N'DEMO09' THEN '16:00' ELSE '18:00' END)) END,
       CASE WHEN Branch=@CTG THEN @LocCTG ELSE @LocHO END,
       -- 0 normal, 1 worked from the approved home office, 2 outside every fence, 3 no location
       CASE WHEN R1 BETWEEN 8 AND 12 THEN 1 WHEN R1 IN(13,14) THEN 2 WHEN R1=15 THEN 3 ELSE 0 END
FROM R
WHERE R1>=8                                   -- about 8% of working days are absences
  AND NOT (D=@Today AND Code IN(N'DEMO06',N'DEMO08'));   -- two people have not arrived yet today

INSERT attendance.EmployeeDevices(EmployeeId,DeviceId,Label,Platform,IsApproved,ApprovedAt,FirstSeenAt,LastSeenAt,CreatedAt)
SELECT e.Id,CONCAT(N'demo-device-',LOWER(e.Code)),N'Android phone (Chrome)',N'Web',1,@Now,DATEADD(day,-30,@Now),@Now,@Now FROM @Emp e;
INSERT attendance.EmployeeDevices(EmployeeId,DeviceId,Label,Platform,IsApproved,FirstSeenAt,LastSeenAt,CreatedAt)
SELECT e.Id,N'demo-device-new-tablet',N'Tablet (Safari)',N'Web',0,DATEADD(day,-2,@Now),DATEADD(day,-2,@Now),@Now FROM @Emp e WHERE e.Code=N'DEMO06';

INSERT attendance.Punches(EmployeeId,Latitude,Longitude,AccuracyMeters,DeviceId,Platform,PunchedAt,LogDate,LocalTime,Direction,Source,IpAddress,
                          MatchedLocationId,DistanceMeters,LocationStatus,FlagsJson,IsReviewed,ReviewedBy,ReviewedAt,ReviewNote,CreatedAt)
SELECT d.EmployeeId,
       CASE d.Odd WHEN 3 THEN NULL WHEN 2 THEN 23.810000 WHEN 1 THEN 23.746900 ELSE w.Latitude+0.000180 END,
       CASE d.Odd WHEN 3 THEN NULL WHEN 2 THEN 90.412000 WHEN 1 THEN 90.376300 ELSE w.Longitude+0.000140 END,
       CASE d.Odd WHEN 3 THEN NULL ELSE 12+ABS(CHECKSUM(NEWID()))%25 END,
       CONCAT(N'demo-device-',LOWER(e.Code)),N'Web',
       TODATETIMEOFFSET(DATEADD(second,DATEDIFF(second,'00:00',p.T),CONVERT(datetime2(3),d.D)),'+06:00'),d.D,p.T,p.Direction,N'Web',N'103.4.145.20',
       CASE d.Odd WHEN 0 THEN d.Loc WHEN 1 THEN @LocHome END,
       CASE d.Odd WHEN 0 THEN 24.6 WHEN 1 THEN 45.2 WHEN 2 THEN 3289.8 END,
       CASE d.Odd WHEN 0 THEN N'InOffice' WHEN 1 THEN N'Remote' WHEN 2 THEN N'OutsideGeofence' ELSE N'Unverified' END,
       CASE d.Odd WHEN 2 THEN N'["OUTSIDE_ALL_FENCES"]' WHEN 3 THEN N'["NO_LOCATION"]' ELSE N'[]' END,
       -- older flagged check-ins have already been looked at; the last week is still waiting
       CASE WHEN d.Odd IN(2,3) AND d.D<DATEADD(day,-7,@Today) THEN 1 ELSE 0 END,
       CASE WHEN d.Odd IN(2,3) AND d.D<DATEADD(day,-7,@Today) THEN @HrUser END,
       CASE WHEN d.Odd IN(2,3) AND d.D<DATEADD(day,-7,@Today) THEN DATEADD(day,1,TODATETIMEOFFSET(CONVERT(datetime2(3),d.D),'+06:00')) END,
       CASE WHEN d.Odd IN(2,3) AND d.D<DATEADD(day,-7,@Today) THEN N'Confirmed with the line manager: client visit' END,
       @Now
FROM @Days d
JOIN @Emp e ON e.Id=d.EmployeeId
JOIN attendance.WorkLocations w ON w.Id=d.Loc
CROSS APPLY (VALUES(N'In',d.InT),(N'Out',d.OutT)) p(Direction,T)
WHERE p.T IS NOT NULL;

INSERT attendance.DailyLogs(EmployeeId,LogDate,InTime,OutTime,Source,CreatedAt)
SELECT EmployeeId,D,InT,OutT,N'Web',@Now FROM @Days;
-- One day keyed in by HR, to show a manual correction.
UPDATE l SET InTime='09:05',OutTime='18:10',Source=N'Manual',Remarks=N'Forgot to check out; corrected by HR'
FROM attendance.DailyLogs l JOIN @Emp e ON e.Id=l.EmployeeId
WHERE e.Code=N'DEMO07' AND l.LogDate=(SELECT MAX(x.LogDate) FROM attendance.DailyLogs x WHERE x.EmployeeId=e.Id AND x.LogDate<@Today);


IF @Slug=N'fegtechnova'
BEGIN
    UPDATE org.Employees SET FullName=CASE Code WHEN N'DEMO01' THEN N'Shabiha Shila' WHEN N'DEMO02' THEN N'Ayshi' WHEN N'DEMO03' THEN N'Fariha' WHEN N'DEMO04' THEN N'Abid' ELSE FullName END;
    UPDATE u SET DisplayName=e.FullName FROM iam.Users u JOIN org.Employees e ON e.UserId=u.Id WHERE e.Code IN(N'DEMO01',N'DEMO02',N'DEMO03',N'DEMO04');
END;
INSERT iam.UserBranches(TenantUserId,BranchId,CreatedAt)
SELECT l.MemberId,@HO,@Now FROM @Logins l WHERE l.Role=N'employee'
AND NOT EXISTS(SELECT 1 FROM iam.UserBranches x WHERE x.TenantUserId=l.MemberId AND x.BranchId=@HO);
-- Inert, expired and revoked demo session; it cannot be used to authenticate.
INSERT iam.RefreshTokens(TenantUserId,TokenHash,ExpiresAt,RevokedAt,CreatedAt)
SELECT l.MemberId,HASHBYTES('SHA2_256',CONCAT(N'expired-demo-',@Slug)),DATEADD(day,-1,@Now),@Now,DATEADD(day,-2,@Now)
FROM @Logins l WHERE l.Role=N'employee';
COMMIT TRANSACTION;
EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Tenant;
SELECT (SELECT COUNT(*) FROM org.Employees) AS Employees,(SELECT COUNT(*) FROM org.Departments) AS Departments,
       (SELECT COUNT(*) FROM attendance.WorkLocations) AS WorkLocations,(SELECT COUNT(*) FROM attendance.Punches) AS Punches,
       (SELECT COUNT(*) FROM attendance.DailyLogs) AS DayLogs,(SELECT COUNT(*) FROM leave.Applications) AS LeaveApplications,
       (SELECT COUNT(*) FROM attendance.Holidays) AS Holidays;
EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=NULL;
PRINT N'Demo data added to ' + @Slug + N'.';
GO
