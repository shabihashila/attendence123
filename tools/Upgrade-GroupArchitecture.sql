:on error exit
SET XACT_ABORT ON;
BEGIN TRANSACTION;
SELECT Id,Name,Slug,ParentTenantRefId INTO #CompaniesBefore FROM platform.Tenants;
SELECT Id,Email,IsActive,SecurityStamp INTO #UsersBefore FROM iam.Users;
CREATE TABLE #LeaveBefore(Id INT NOT NULL,Status NVARCHAR(20),CurrentStep NVARCHAR(20));
DECLARE @BeforePrior sql_variant=SESSION_CONTEXT(N'FegHR.TenantId'),@BeforeTid INT;
DECLARE beforeCompanies CURSOR LOCAL FAST_FORWARD FOR SELECT Id FROM platform.Tenants;
OPEN beforeCompanies; FETCH NEXT FROM beforeCompanies INTO @BeforeTid;
WHILE @@FETCH_STATUS=0
BEGIN
    EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@BeforeTid;
    INSERT #LeaveBefore SELECT Id,Status,CurrentStep FROM leave.Applications;
    FETCH NEXT FROM beforeCompanies INTO @BeforeTid;
END;
CLOSE beforeCompanies; DEALLOCATE beforeCompanies;
EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@BeforePrior;
GO
:r 14_GroupAccess.sql
:r 15_ApprovalWorkflow.sql
:r 16_GroupOperations.sql
:r 17_CompanyAudit.sql
CREATE TABLE #LeaveAfter(Id INT NOT NULL,Status NVARCHAR(20),CurrentStep NVARCHAR(20));
DECLARE @AfterPrior sql_variant=SESSION_CONTEXT(N'FegHR.TenantId'),@AfterTid INT;
DECLARE afterCompanies CURSOR LOCAL FAST_FORWARD FOR SELECT Id FROM platform.Tenants;
OPEN afterCompanies; FETCH NEXT FROM afterCompanies INTO @AfterTid;
WHILE @@FETCH_STATUS=0
BEGIN
    EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@AfterTid;
    INSERT #LeaveAfter SELECT Id,Status,CurrentStep FROM leave.Applications;
    FETCH NEXT FROM afterCompanies INTO @AfterTid;
END;
CLOSE afterCompanies; DEALLOCATE afterCompanies;
EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@AfterPrior;
IF EXISTS(SELECT Id,Name,Slug,ParentTenantRefId FROM platform.Tenants EXCEPT SELECT * FROM #CompaniesBefore)
 OR EXISTS(SELECT * FROM #CompaniesBefore EXCEPT SELECT Id,Name,Slug,ParentTenantRefId FROM platform.Tenants)
 OR EXISTS(SELECT Id,Email,IsActive,SecurityStamp FROM iam.Users EXCEPT SELECT * FROM #UsersBefore)
 OR EXISTS(SELECT * FROM #UsersBefore EXCEPT SELECT Id,Email,IsActive,SecurityStamp FROM iam.Users)
 OR EXISTS(SELECT * FROM #LeaveAfter EXCEPT SELECT * FROM #LeaveBefore)
 OR EXISTS(SELECT * FROM #LeaveBefore EXCEPT SELECT * FROM #LeaveAfter)
    THROW 59030,'Existing company identities, user accounts or leave workflow changed unexpectedly.',1;
COMMIT TRANSACTION;
PRINT N'Group architecture upgraded. Existing companies, user identities and leave applications preserved.';
GO
