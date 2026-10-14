-- Run from FegHR/Database/release-1.0 with sqlcmd -b -I, against db_feg_hr.
:on error exit
SET XACT_ABORT ON;
BEGIN TRANSACTION;
SELECT Id,Name,Slug,ParentTenantRefId INTO #CompaniesBefore FROM platform.Tenants;
GO
:r 13_CompanyManagement.sql
IF EXISTS(SELECT Id,Name,Slug,ParentTenantRefId FROM platform.Tenants EXCEPT SELECT Id,Name,Slug,ParentTenantRefId FROM #CompaniesBefore)
    OR EXISTS(SELECT Id,Name,Slug,ParentTenantRefId FROM #CompaniesBefore EXCEPT SELECT Id,Name,Slug,ParentTenantRefId FROM platform.Tenants)
    THROW 59020,'Existing company identities or hierarchy changed unexpectedly.',1;
COMMIT TRANSACTION;
PRINT N'Company management upgraded; existing names, IDs, slugs and hierarchy preserved.';
GO
