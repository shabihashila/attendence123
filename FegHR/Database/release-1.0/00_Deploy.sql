-- Run from release-1.0 using sqlcmd -b -I. Use deploy-db.ps1 for timestamp binding.
:on error exit
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO
:r 01_Schemas.sql
:r 02_Security.sql
:r 03_Platform.sql
:r 04_Iam.sql
:r 05_Organization.sql
:r 06_Attendance.sql
:r 07_Leave.sql
:r 08_Reporting.sql
:r 09_Onboarding.sql
:r 10_AppAccess.sql
:r 11_OwnerCompany.sql

:r 12_CompanyHierarchy.sql
:r SeedData/01_Metadata.sql
COMMIT TRANSACTION;
GO
PRINT N'FegHR database deployed.';
