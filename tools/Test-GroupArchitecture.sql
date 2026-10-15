:on error exit
USE master;
GO
IF DB_ID(N'FegHR_GroupArchitecture_Test') IS NOT NULL THROW 59000,'Existing test database must be inspected before retrying.',1;
CREATE DATABASE FegHR_GroupArchitecture_Test;
GO
USE FegHR_GroupArchitecture_Test;
GO
:r 00_Deploy.sql
:r 14_GroupAccess.sql
:r 15_ApprovalWorkflow.sql
:r 16_GroupOperations.sql
:r 18_CompanySwitcher.sql
:r 19_CompanyAccessDefaults.sql
GO
SET NOCOUNT ON;
DECLARE @Now DATETIMEOFFSET(3)=SYSDATETIMEOFFSET(),@Today DATE=CONVERT(date,SYSDATETIMEOFFSET());
INSERT iam.Users(Email,NormalizedEmail,DisplayName,SecurityStamp,IsPlatformAdmin,CreatedAt)
SELECT CONCAT(v.Name,N'@test.local'),UPPER(CONCAT(v.Name,N'@test.local')),v.Name,NEWID(),v.Owner,@Now
FROM (VALUES(N'owner',1),(N'groupadmin',0),(N'outsider',0),(N'employee',0),(N'l1',0),(N'l2',0),(N'l3',0),(N'l4',0),(N'l5',0),(N'final',0)) v(Name,Owner);
DECLARE @Owner INT=(SELECT Id FROM iam.Users WHERE DisplayName=N'owner'),@Admin INT=(SELECT Id FROM iam.Users WHERE DisplayName=N'groupadmin'),@EmployeeUser INT=(SELECT Id FROM iam.Users WHERE DisplayName=N'employee');
INSERT platform.Tenants(Slug,Name,CreatedAt) VALUES(N'group',N'Group',@Now),(N'unrelated',N'Unrelated',@Now);
DECLARE @Group INT=(SELECT Id FROM platform.Tenants WHERE Slug=N'group'),@Unrelated INT=(SELECT Id FROM platform.Tenants WHERE Slug=N'unrelated');
EXEC sys.sp_set_session_context @key=N'FegHR.IsPlatformAdmin',@value=1;
EXEC sys.sp_set_session_context @key=N'FegHR.UserId',@value=@Owner;
EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Group;
EXEC platform.usp_Owner_Concern_Create @ParentTenantId=@Group,@Name=N'Concern',@BaseSlug=N'concern',@Now=@Now,@ActorId=@Owner;
DECLARE @Child INT=(SELECT Id FROM platform.Tenants WHERE Slug=N'concern');
EXEC iam.usp_Tenant_SeedRoles @Now=@Now;
EXEC iam.usp_Group_Members @Email=N'groupadmin@test.local',@RoleCode=N'group-admin',@IsActive=1;
EXEC iam.usp_User_GetTenants @UserId=@Admin;
IF NOT EXISTS(SELECT 1 FROM iam.UserTenants WHERE UserId=@Admin AND TenantRefId=@Child AND IsGroupManaged=1) THROW 59001,'Group memberships were not created.',1;
IF EXISTS(SELECT 1 FROM iam.UserTenants WHERE UserId=@Admin AND TenantRefId=@Unrelated) THROW 59002,'Unrelated company membership leaked.',1;
EXEC sys.sp_set_session_context @key=N'FegHR.UserId',@value=@Admin;
EXEC sys.sp_set_session_context @key=N'FegHR.IsPlatformAdmin',@value=0;
EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Child;
IF NOT EXISTS(SELECT 1 FROM iam.fn_UserPermissions(@Admin) WHERE Code=N'iam.users.write') THROW 59003,'Group Admin lacks company management rights.',1;
IF EXISTS(SELECT 1 FROM iam.fn_UserPermissions(@Admin) WHERE Code LIKE N'owner.%') THROW 59004,'Group Admin obtained platform Owner rights.',1;
EXECUTE AS USER=N'feghr_app';
EXEC platform.usp_Group_Context;
EXEC platform.usp_Group_Report;
EXEC platform.usp_Group_Settings @SettingsJson=N'{"feature":true}';
EXEC platform.usp_Group_Concern_Create @Name=N'Second concern',@BaseSlug=N'second-concern',@Now=@Now;
REVERT;
IF TRY_CONVERT(bit,SESSION_CONTEXT(N'FegHR.IsPlatformAdmin'))<>0 THROW 59015,'Scoped provisioning leaked Owner context.',1;
EXEC iam.usp_User_GetTenants @UserId=@Admin;
IF NOT EXISTS(SELECT 1 FROM iam.UserTenants x JOIN platform.Tenants t ON t.Id=x.TenantRefId WHERE x.UserId=@Admin AND t.Slug=N'second-concern') THROW 59016,'New concern is missing group access.',1;
UPDATE platform.Tenants SET ParentTenantRefId=NULL WHERE Id=@Child;
IF EXISTS(SELECT 1 FROM iam.fn_GroupRole(@Admin,@Child)) THROW 59005,'Detached concern retains old group rights.',1;
IF EXISTS(SELECT 1 FROM iam.fn_UserPermissions(@Admin) WHERE Code=N'iam.users.write') THROW 59006,'Detached permissions leaked.',1;
EXEC iam.usp_User_CanAccessTenant @UserId=@Admin,@TenantId=@Child;
UPDATE platform.Tenants SET ParentTenantRefId=@Group WHERE Id=@Child;
UPDATE iam.GroupMembers SET IsActive=0 WHERE GroupId=@Group AND UserId=@Admin;
IF EXISTS(SELECT 1 FROM iam.fn_GroupRole(@Admin,@Child)) THROW 59007,'Revoked group grant remains effective.',1;
UPDATE iam.GroupMembers SET IsActive=1 WHERE GroupId=@Group AND UserId=@Admin;
-- Direct company membership survives changes to group grants and roles stay company-specific.
INSERT iam.TenantUsers(UserId,CreatedAt) VALUES(@EmployeeUser,@Now);
DECLARE @Member INT=CONVERT(int,SCOPE_IDENTITY());
INSERT iam.UserTenants(UserId,TenantRefId,TenantUserId,IsActive) VALUES(@EmployeeUser,@Child,@Member,1);
INSERT iam.UserRoles(TenantUserId,RoleId,CreatedAt) SELECT @Member,Id,@Now FROM iam.Roles WHERE Code=N'employee';
IF EXISTS(SELECT 1 FROM iam.fn_UserPermissions(@EmployeeUser) WHERE Code=N'group.console') THROW 59008,'Company member inherited group management.',1;
EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Unrelated;
EXEC iam.usp_Tenant_SeedRoles @Now=@Now;
INSERT iam.TenantUsers(UserId,CreatedAt) VALUES(@EmployeeUser,@Now);
DECLARE @ViewerMember INT=CONVERT(int,SCOPE_IDENTITY());
INSERT iam.UserTenants(UserId,TenantRefId,TenantUserId,IsActive) VALUES(@EmployeeUser,@Unrelated,@ViewerMember,1);
INSERT iam.UserRoles(TenantUserId,RoleId,CreatedAt) SELECT @ViewerMember,Id,@Now FROM iam.Roles WHERE Code=N'viewer';
IF EXISTS(SELECT 1 FROM iam.fn_UserPermissions(@EmployeeUser) WHERE Code=N'leave.apply.self') THROW 59017,'Company roles crossed company boundaries.',1;
IF NOT EXISTS(SELECT 1 FROM iam.fn_UserPermissions(@EmployeeUser) WHERE Code=N'org.employees.read') THROW 59018,'Viewer role is missing read access.',1;
EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Child;
-- Prepare five supervisors, all company members, plus a Final Approver.
INSERT iam.TenantUsers(UserId,CreatedAt) SELECT Id,@Now FROM iam.Users WHERE DisplayName IN(N'l1',N'l2',N'l3',N'l4',N'l5',N'final');
INSERT iam.UserTenants(UserId,TenantRefId,TenantUserId,IsActive)
SELECT UserId,@Child,Id,1 FROM iam.TenantUsers tu WHERE NOT EXISTS(SELECT 1 FROM iam.UserTenants x WHERE x.UserId=tu.UserId AND x.TenantRefId=@Child);
INSERT iam.UserRoles(TenantUserId,RoleId,CreatedAt)
SELECT tu.Id,r.Id,@Now FROM iam.TenantUsers tu JOIN iam.Users u ON u.Id=tu.UserId CROSS JOIN iam.Roles r WHERE u.DisplayName IN(N'l1',N'l2',N'l3',N'l4',N'l5',N'final') AND r.Code=N'admin';
INSERT org.Employees(Code,FullName,UserId,CreatedAt) SELECT DisplayName,DisplayName,Id,@Now FROM iam.Users WHERE DisplayName IN(N'employee',N'l1',N'l2',N'l3',N'l4',N'l5',N'final');
UPDATE e SET ReportsToId=supervisor.Id FROM org.Employees e JOIN org.Employees supervisor ON supervisor.Code=CASE e.Code WHEN N'employee' THEN N'l1' WHEN N'l1' THEN N'l2' WHEN N'l2' THEN N'l3' WHEN N'l3' THEN N'l4' WHEN N'l4' THEN N'l5' END;
DECLARE @Employee INT=(SELECT Id FROM org.Employees WHERE UserId=@EmployeeUser),@Final INT=(SELECT Id FROM iam.Users WHERE DisplayName=N'final'),@Type INT=(SELECT Id FROM leave.LeaveTypes WHERE Code=N'UL');
DECLARE @LegacyDate DATE=DATEADD(day,-2,@Today);
EXEC leave.usp_Application_Submit @EmployeeId=@Employee,@LeaveTypeId=@Type,@DayType=N'Full',@StartDate=@LegacyDate,@EndDate=@LegacyDate,@Today=@Today,@Now=@Now,@ActorId=@EmployeeUser;
IF NOT EXISTS(SELECT 1 FROM leave.Applications WHERE EmployeeId=@Employee AND StartDate=@LegacyDate AND UsesApprovalWorkflow=0) THROW 59020,'Unconfigured legacy workflow changed.',1;
EXEC leave.usp_ApprovalPolicy @Levels=5,@FinalApproverUserId=@Final,@IsEnabled=1;
EXEC leave.usp_Application_Submit @EmployeeId=@Employee,@LeaveTypeId=@Type,@DayType=N'Full',@StartDate=@Today,@EndDate=@Today,@Today=@Today,@Now=@Now,@ActorId=@EmployeeUser;
DECLARE @Application INT=(SELECT TOP(1) Id FROM leave.Applications WHERE EmployeeId=@Employee ORDER BY Id DESC);
IF (SELECT COUNT(*) FROM leave.ApprovalSteps WHERE ApplicationId=@Application)<>6 THROW 59009,'Six-stage route was not snapshotted.',1;
DECLARE @PolicyVersion BINARY(8)=(SELECT RowVersion FROM leave.ApprovalPolicies);
EXEC leave.usp_ApprovalPolicy @Levels=0,@FinalApproverUserId=@Final,@IsEnabled=0,@RowVersion=@PolicyVersion;
IF (SELECT COUNT(*) FROM leave.ApprovalSteps WHERE ApplicationId=@Application)<>6 THROW 59021,'Changing policy rewrote historical routing.',1;
DECLARE @L1 INT=(SELECT Id FROM iam.Users WHERE DisplayName=N'l1'),@L2 INT=(SELECT Id FROM iam.Users WHERE DisplayName=N'l2');
EXEC leave.usp_Approval_Reassign @ApplicationId=@Application,@NewUserId=@L2,@Reason=N'Offboarding test',@ExpectedApproverId=@L1;
IF NOT EXISTS(SELECT 1 FROM leave.ApprovalReassignments WHERE ApplicationId=@Application AND OldUserId=@L1 AND NewUserId=@L2) THROW 59019,'Reassignment audit missing.',1;
EXEC leave.usp_Approval_Reassign @ApplicationId=@Application,@NewUserId=@L1,@Reason=N'Restore original approver',@ExpectedApproverId=@L2;
BEGIN TRY
    EXEC leave.usp_Workflow_Decide @ApplicationId=@Application,@Approve=1,@Now=@Now,@ActorId=@Final;
    THROW 59010,'Final approval bypassed required supervisors.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>54012 THROW; END CATCH;
BEGIN TRY
    EXEC leave.usp_Application_Decide @ApplicationId=@Application,@Approve=1,@CanLevel1=1,@CanLevel2=1,@Now=@Now,@ActorId=@Final;
    THROW 59011,'Legacy decision bypassed workflow.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>54012 THROW; END CATCH;
DECLARE @Stage INT=1,@Approver INT;
WHILE @Stage<=6
BEGIN
    SET @Approver=(SELECT ApproverUserId FROM leave.ApprovalSteps WHERE ApplicationId=@Application AND Stage=@Stage);
    EXEC sys.sp_set_session_context @key=N'FegHR.UserId',@value=@Approver;
    EXECUTE AS USER=N'feghr_app';
    EXEC leave.usp_Workflow_Decide @ApplicationId=@Application,@Approve=1,@Now=@Now,@ActorId=@Approver;
    REVERT;
    SET @Stage+=1;
END;
IF NOT EXISTS(SELECT 1 FROM leave.Applications WHERE Id=@Application AND Status=N'Approved' AND CurrentStep=N'Done') THROW 59012,'Final completion failed.',1;
IF (SELECT COUNT(*) FROM leave.ApprovalSteps WHERE ApplicationId=@Application AND Status=N'Approved')<>6 THROW 59013,'Approval history is incomplete.',1;
SET @PolicyVersion=(SELECT RowVersion FROM leave.ApprovalPolicies);
EXEC leave.usp_ApprovalPolicy @Levels=5,@FinalApproverUserId=@Final,@IsEnabled=1,@RowVersion=@PolicyVersion;
DECLARE @RejectDate DATE=DATEADD(day,1,@Today),@CancelDate DATE=DATEADD(day,2,@Today),@MissingDate DATE=DATEADD(day,3,@Today),@OtherApplication INT;
EXEC leave.usp_Application_Submit @EmployeeId=@Employee,@LeaveTypeId=@Type,@DayType=N'Full',@StartDate=@RejectDate,@EndDate=@RejectDate,@Today=@Today,@Now=@Now,@ActorId=@EmployeeUser;
SET @OtherApplication=(SELECT Id FROM leave.Applications WHERE StartDate=@RejectDate AND EmployeeId=@Employee);
EXEC leave.usp_Workflow_Decide @ApplicationId=@OtherApplication,@Approve=0,@Now=@Now,@ActorId=@L1;
IF (SELECT COUNT(*) FROM leave.ApprovalSteps WHERE ApplicationId=@OtherApplication AND Status=N'Skipped')<>5 THROW 59022,'Rejection did not skip future steps.',1;
EXEC leave.usp_Application_Submit @EmployeeId=@Employee,@LeaveTypeId=@Type,@DayType=N'Full',@StartDate=@CancelDate,@EndDate=@CancelDate,@Today=@Today,@Now=@Now,@ActorId=@EmployeeUser;
SET @OtherApplication=(SELECT Id FROM leave.Applications WHERE StartDate=@CancelDate AND EmployeeId=@Employee);
EXEC leave.usp_Application_Cancel @ApplicationId=@OtherApplication,@EmployeeId=@Employee,@Now=@Now,@ActorId=@EmployeeUser;
IF (SELECT COUNT(*) FROM leave.ApprovalSteps WHERE ApplicationId=@OtherApplication AND Status=N'Cancelled')<>6 THROW 59023,'Cancellation did not close pending steps.',1;
UPDATE org.Employees SET ReportsToId=Id WHERE Id=@Employee;
BEGIN TRY
    EXEC leave.usp_Application_Submit @EmployeeId=@Employee,@LeaveTypeId=@Type,@DayType=N'Full',@StartDate=@MissingDate,@EndDate=@MissingDate,@Today=@Today,@Now=@Now,@ActorId=@EmployeeUser;
    THROW 59024,'Circular supervisor chain was accepted.',1;
END TRY BEGIN CATCH IF ERROR_NUMBER()<>54021 THROW; END CATCH;
IF EXISTS(SELECT 1 FROM leave.Applications WHERE StartDate=@MissingDate AND EmployeeId=@Employee) THROW 59025,'Failed route left a partial application.',1;
-- RLS remains exact-company scoped, even for a Group Admin.
EXEC sys.sp_set_session_context @key=N'FegHR.TenantId',@value=@Unrelated;
IF EXISTS(SELECT 1 FROM leave.Applications WHERE Id=@Application) THROW 59014,'Application crossed the tenant boundary.',1;
PRINT N'PASS: repeat deployment, runtime permissions, group discovery/creation, isolation, detach/revoke, multi-company roles, L1-L5/Final, policy snapshots, legacy protection, reassign audit, reject/cancel, circular-chain rollback, history and RLS.';
GO
USE master;
GO
DROP DATABASE FegHR_GroupArchitecture_Test;
GO
