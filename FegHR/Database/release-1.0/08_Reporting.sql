-- reporting: read-only procedures. This is the one schema allowed to read across module
-- schemas (ARCHITECTURE.md section 3 rule 4). Everything runs under the caller's tenant policy.

-- One row per employee and date in [@From, @To] with the day's verdict:
--   Present / Late   a check-in exists (late: after the shift start plus grace on a working day)
--   Leave            approved leave covers the date
--   Holiday / WeekOff
--   Absent           a working day with no check-in, up to @Today
--   Upcoming         a working day after @Today
CREATE OR ALTER FUNCTION reporting.fn_AttendanceDays(@From DATE,@To DATE,@Today DATE,@EmployeeId INT)
RETURNS TABLE AS RETURN
    WITH Calendar AS (
        SELECT TOP(CASE WHEN @To>=@From THEN DATEDIFF(day,@From,@To)+1 ELSE 0 END)
               DATEADD(day,ROW_NUMBER() OVER(ORDER BY (SELECT NULL))-1,@From) AS D
        FROM sys.all_objects a CROSS JOIN sys.all_objects b
    )
    SELECT e.Id AS EmployeeId,c.D AS LogDate,l.InTime,l.OutTime,l.Source,s.StartTime AS ShiftStart,s.EndTime AS ShiftEnd,
           CASE WHEN l.InTime IS NOT NULL AND l.OutTime IS NOT NULL AND l.OutTime>l.InTime THEN DATEDIFF(minute,l.InTime,l.OutTime) END AS WorkedMinutes,
           CASE WHEN l.InTime IS NOT NULL THEN
                    CASE WHEN ISNULL(s.IsWorkingDay,1)=1 AND h.Id IS NULL AND l.InTime>ISNULL(s.LateAfter,CONVERT(time(0),'09:15')) THEN N'Late' ELSE N'Present' END
                WHEN lv.Id IS NOT NULL THEN N'Leave'
                WHEN h.Id IS NOT NULL THEN N'Holiday'
                WHEN ISNULL(s.IsWorkingDay,1)=0 THEN N'WeekOff'
                WHEN c.D>@Today THEN N'Upcoming'
                ELSE N'Absent' END AS Status,
           COALESCE(lv.TypeName,h.Title) AS Note
    FROM org.Employees e
    CROSS JOIN Calendar c
    LEFT JOIN attendance.DailyLogs l ON l.TenantId=e.TenantId AND l.EmployeeId=e.Id AND l.LogDate=c.D AND l.IsActive=1
    OUTER APPLY attendance.fn_ScheduleForDate(e.Id,c.D) s
    OUTER APPLY (SELECT TOP(1) x.Id,x.Title FROM attendance.Holidays x WHERE x.HolidayDate=c.D AND x.IsActive=1 ORDER BY x.Id) h
    OUTER APPLY (SELECT TOP(1) a.Id,t.Name AS TypeName FROM leave.Applications a
                 JOIN leave.LeaveTypes t ON t.TenantId=a.TenantId AND t.Id=a.LeaveTypeId
                 WHERE a.EmployeeId=e.Id AND a.Status=N'Approved' AND a.IsActive=1 AND a.StartDate<=c.D AND a.EndDate>=c.D
                 ORDER BY a.Id) lv
    WHERE e.IsActive=1 AND (@EmployeeId IS NULL OR e.Id=@EmployeeId)
      AND (e.JoiningDate IS NULL OR e.JoiningDate<=c.D) AND (e.ExitDate IS NULL OR e.ExitDate>=c.D);
GO
CREATE OR ALTER PROCEDURE reporting.usp_Attendance_Daily
    @Date DATE,@Today DATE,@BranchId INT=NULL,@DepartmentId INT=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT e.Id AS EmployeeId,e.Code,e.FullName,dp.Name AS Department,dg.Name AS Designation,b.Name AS Branch,
           a.LogDate,a.InTime,a.OutTime,a.ShiftStart,a.ShiftEnd,a.WorkedMinutes,a.Status,a.Note,a.Source
    FROM reporting.fn_AttendanceDays(@Date,@Date,@Today,NULL) a
    JOIN org.Employees e ON e.Id=a.EmployeeId
    LEFT JOIN org.Departments dp ON dp.TenantId=e.TenantId AND dp.Id=e.DepartmentId
    LEFT JOIN org.Designations dg ON dg.TenantId=e.TenantId AND dg.Id=e.DesignationId
    LEFT JOIN platform.Branches b ON b.TenantId=e.TenantId AND b.Id=e.BranchId
    WHERE (@BranchId IS NULL OR e.BranchId=@BranchId) AND (@DepartmentId IS NULL OR e.DepartmentId=@DepartmentId)
    ORDER BY e.Code;
END;
GO
CREATE OR ALTER PROCEDURE reporting.usp_Attendance_Monthly
    @From DATE,@To DATE,@Today DATE,@BranchId INT=NULL,@DepartmentId INT=NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @From IS NULL OR @To IS NULL OR @To<@From OR DATEDIFF(day,@From,@To)>92
        THROW 55001,'Choose a period of at most three months.',1;
    SELECT e.Id AS EmployeeId,e.Code,e.FullName,dp.Name AS Department,dg.Name AS Designation,
           SUM(CASE WHEN a.Status IN(N'Present',N'Late') THEN 1 ELSE 0 END) AS PresentDays,
           SUM(CASE WHEN a.Status=N'Late' THEN 1 ELSE 0 END) AS LateDays,
           SUM(CASE WHEN a.Status=N'Absent' THEN 1 ELSE 0 END) AS AbsentDays,
           SUM(CASE WHEN a.Status=N'Leave' THEN 1 ELSE 0 END) AS LeaveDays,
           SUM(CASE WHEN a.Status=N'WeekOff' THEN 1 ELSE 0 END) AS WeekOffDays,
           SUM(CASE WHEN a.Status=N'Holiday' THEN 1 ELSE 0 END) AS HolidayDays,
           SUM(ISNULL(a.WorkedMinutes,0)) AS WorkedMinutes
    FROM reporting.fn_AttendanceDays(@From,@To,@Today,NULL) a
    JOIN org.Employees e ON e.Id=a.EmployeeId
    LEFT JOIN org.Departments dp ON dp.TenantId=e.TenantId AND dp.Id=e.DepartmentId
    LEFT JOIN org.Designations dg ON dg.TenantId=e.TenantId AND dg.Id=e.DesignationId
    WHERE (@BranchId IS NULL OR e.BranchId=@BranchId) AND (@DepartmentId IS NULL OR e.DepartmentId=@DepartmentId)
    GROUP BY e.Id,e.Code,e.FullName,dp.Name,dg.Name
    ORDER BY e.Code;
END;
GO
CREATE OR ALTER PROCEDURE reporting.usp_Attendance_Employee
    @EmployeeId INT,@From DATE,@To DATE,@Today DATE
AS
BEGIN
    SET NOCOUNT ON;
    IF @From IS NULL OR @To IS NULL OR @To<@From OR DATEDIFF(day,@From,@To)>366
        THROW 55001,'Choose a period of at most one year.',1;
    SELECT LogDate,InTime,OutTime,ShiftStart,ShiftEnd,WorkedMinutes,Status,Note,Source
    FROM reporting.fn_AttendanceDays(@From,@To,@Today,@EmployeeId) ORDER BY LogDate;
END;
GO
-- Leave balances of every active employee for a year: one row per employee and leave type.
CREATE OR ALTER PROCEDURE reporting.usp_Leave_Statement
    @Year INT,@DepartmentId INT=NULL,@EmployeeId INT=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT e.Id AS EmployeeId,e.Code,e.FullName,dp.Name AS Department,
           b.LeaveTypeId,b.Name AS LeaveType,b.Quota,b.Adjustment,b.Used,b.Pending,b.Remaining
    FROM org.Employees e
    CROSS APPLY leave.fn_Balance(e.Id,@Year) b
    LEFT JOIN org.Departments dp ON dp.TenantId=e.TenantId AND dp.Id=e.DepartmentId
    WHERE e.IsActive=1 AND (@DepartmentId IS NULL OR e.DepartmentId=@DepartmentId) AND (@EmployeeId IS NULL OR e.Id=@EmployeeId)
    ORDER BY e.Code,b.Name;
END;
GO
-- Numbers for the home page of the bound company on @Today.
CREATE OR ALTER PROCEDURE reporting.usp_Dashboard_Summary
    @Today DATE
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT(*) AS Headcount,
           ISNULL(SUM(CASE WHEN a.Status IN(N'Present',N'Late') THEN 1 ELSE 0 END),0) AS Present,
           ISNULL(SUM(CASE WHEN a.Status=N'Late' THEN 1 ELSE 0 END),0) AS Late,
           ISNULL(SUM(CASE WHEN a.Status=N'Absent' THEN 1 ELSE 0 END),0) AS Absent,
           ISNULL(SUM(CASE WHEN a.Status=N'Leave' THEN 1 ELSE 0 END),0) AS OnLeave,
           (SELECT COUNT(*) FROM leave.Applications WHERE Status=N'Pending' AND IsActive=1) AS PendingLeave,
           (SELECT COUNT(*) FROM attendance.Punches WHERE IsReviewed=0 AND IsActive=1
               AND LocationStatus IN(N'OutsideGeofence',N'Unverified',N'Suspicious')) AS PunchesToReview
    FROM reporting.fn_AttendanceDays(@Today,@Today,@Today,NULL) a;
END;
GO
