# FegHR — Architecture Contract

Multi-company HR SaaS, the successor of `HRAccounts`. The owner's brief is
[OVERVIEW.md](OVERVIEW.md). This document is the contract every contributor (human or
agent) builds against; change it deliberately.

FegHR is the FEG-specific fork of DailyHR. The root README is the setup and
operational handoff; local verification artifacts are kept in ignored `artifacts/`.

FEG adds three demonstration companies and `platform.Tenants.ParentTenantRefId`.
The parent relationship is metadata and does not grant access across tenants.
FegCommunication and FegStorage use separate `db_feg_communication` and
`db_feg_storage` databases. FegHR uses `db_feg_hr`, blue `#1863DC`, teal `#0F9F8F`,
and the bundled FEG TechNova logo. See `scripts/Setup-Local.ps1` for the repeatable
local installation, which preserves existing data and credentials.

## 1. Decision: a new project, not a conversion

`HRAccounts` is clean architecture with MVC controllers, one `dbo` schema and tenancy as an
EF query filter. The brief asks for a modular monolith, module-wise schemas, database-level
access authorization and a different UI, which changes every layer. FegHR is therefore a
new project, and `HRAccounts` stays untouched as the functional reference until each of its
areas has been moved. The foundation (shared kernel, host, tenancy, row-level security,
auth) is taken from DailyRestaurant, which is the modular-monolith form of the DailyCommerce
model.

## 2. Stack and layout

| Layer | Choice |
|---|---|
| Runtime | .NET 10, ASP.NET Core minimal APIs |
| Data | SQL Server, **database first**: `FegHR/Database/release-x.y` is the source of truth. No EF migrations |
| Data access | EF Core for simple CRUD; Dapper + stored procedures (`CommandType.StoredProcedure`) for anything that needs SQL. No SQL text in C# |
| UI | Angular 21 on the [sakai-ng](https://github.com/primefaces/sakai-ng) template (PrimeNG, Tailwind) |
| Shared services | `SharedServices/` FileStorageService (photos, logos), CommunicationService (email/SMS client registered), `Shared.Logging`, `Shared.Time` |

Dev: API `http://localhost:5975`, UI `http://localhost:4425`, database `localhost` /
`db_feg_hr`.

```
FegHR/
  FegHR.slnx, Directory.Build.props / .targets / Directory.Packages.props
  src/
    Host/FegHR.Api/                       Program.cs: explicit ordered module list, auth, CORS, rate limits
    BuildingBlocks/FegHR.SharedKernel/    Modularity, Tenancy, Security (permissions, licence guard), Http, Persistence
    BuildingBlocks/FegHR.SharedServices.Clients/   FileStorageClient, CommunicationClient
    Modules/<Name>/FegHR.Modules.<Name>/            internal implementation
    Modules/<Name>/FegHR.Modules.<Name>.Contracts/  the only surface other modules may use
  Database/release-1.0/                     numbered idempotent scripts, 00_Deploy.sql, SeedData/
  Database/deploy-db.ps1, Install-FreshDatabase.ps1
  Ui/FegHR.Ui/                            Angular workspace
  tools/New-Module.ps1                      scaffolds a module pair; always use it
```

## 3. Modular monolith rules

1. One module = one bounded context = one SQL schema = one DbContext.
2. A module references only `SharedKernel`, `SharedServices.Clients` and other modules'
   `.Contracts`. `Directory.Build.targets` fails the build otherwise.
3. Implementation projects are `internal` except the `IModule` class.
4. No cross-schema foreign keys or joins in module SQL; other modules' rows are referenced
   by id (`TenantId` → `platform.Tenants` is the exception). The `reporting` schema holds
   read-only procedures that may read across schemas.
5. Cross-module calls go through `.Contracts` interfaces (`ITenantDirectory`,
   `IMemberDirectory`, `IEmployeeDirectory`).
6. The host registers modules from an explicit list in `Program.cs`.
7. Inside a module: `Features/` (endpoints, request/response records), `Domain/`,
   `Persistence/`.

| Module | Schema | Owns |
|---|---|---|
| Platform | `platform` | Tenants (companies), TenantHosts, Branches, TenantSettings (time zone, company branding), Features, Plans, LicensePolicy, Licenses, PlatformBranding |
| Iam | `iam` | Users (global), TenantUsers, UserTenants, Roles, Permissions, RolePermissions, RoleGrantSeeds, UserRoles, UserBranches, RefreshTokens, Menus, MenuPermissions |
| Organization | `org` | Departments, Designations, Employees |
| Attendance | `attendance` | Settings, WorkLocations (geo-fences), WorkSchedules/Days, EmployeeSchedules, Holidays, EmployeeDevices, Punches, DailyLogs |
| Leave | `leave` | LeaveTypes, Applications, Adjustments |
| Reporting | `reporting` | Read-only functions and procedures (attendance days, leave statement, dashboard) |
| — | `security` | RLS predicate and policy helper |
| — | `logs` | Serilog sink table |

## 4. Tenant isolation (database-level authorization)

Enforced in SQL Server, so EF, Dapper and procedures are all covered.

1. `security.fn_TenantAccess(@TenantId)` returns a row only when `@TenantId` equals
   `SESSION_CONTEXT('FegHR.TenantId')`. It has no platform-admin bypass.
2. `security.usp_ApplyTenantPolicy @Schema` creates, per table with a `TenantId` column, a
   security policy with a FILTER predicate and BLOCK predicates after insert and update.
   Every module script re-runs it for its schema.
3. `TenantConnectionInterceptor` (EF) and `ITenantDbConnectionFactory` (Dapper) set the
   session context on every connection open.
4. The application role `feghr_runtime` has DML and EXECUTE on module schemas, no DDL, and
   is denied the control-plane tables (`10_AppAccess.sql`).
5. Control-plane tables are global and named with `TenantRefId` where they point at a
   company: `platform.Tenants`, `TenantHosts`, `Plans`, `Features`, `LicensePolicy`,
   `Licenses`, `PlatformBranding`, `iam.Users`, `UserTenants`, `Permissions`, `Menus`,
   `MenuPermissions`. They are reached through procedures that check the caller's context.

## 5. Identity, roles, navigation

- Users are global; a user joins companies through `iam.TenantUsers`. Sign-in returns a
  session, or a company picker when the user belongs to several.
- Permissions are codes (`attendance.review`, `leave.approve.l2`, …) seeded by
  `SeedData/01_Metadata.sql`. Endpoints declare `.RequirePermission(...)`.
- System roles per company: `admin` (Company Admin, holds every company permission by
  rule), `hr`, `manager`, `employee`. Default grants are applied once per company
  (`iam.RoleGrantSeeds`), so a grant a company removed is not restored by a deployment.
- The sidebar is data: `iam.Menus` + `iam.MenuPermissions`, returned by
  `GET /api/iam/navigation`, filtered by the user's permissions and the licence's features.
  A new page needs a route in `Ui/.../app.routes.ts` **and** a menu row.
- **Product owner** (`iam.Users.IsPlatformAdmin`): created by `deploy-db.ps1
  -PlatformOwnerEmail`. They receive an `admin` membership in every company
  (`iam.usp_PlatformOwner_JoinTenant`, run at registration and on every deployment) and
  switch company from the top bar (`POST /api/iam/session/switch-tenant`). Every request
  stays bound to one company under RLS. Permissions coded `owner.*` and the `/api/owner`
  routes are theirs alone.

## 6. Registration and licensing

- `POST /api/iam/auth/register` (public, rate-limited) calls `iam.usp_Company_Register`,
  which in one transaction creates the company, its settings and head-office branch, a
  trial licence, the administrator account with the `admin` role, the administrator's
  employee record, the default shift and leave types, and the product owners' memberships.
  It is the one flow that writes several schemas atomically; it does so by calling each
  module's own onboarding procedure, never by joining their tables.
- `platform.LicensePolicy` (one row) holds the trial length, the trial plan and whether
  registration is open; the product owner edits it.
- `platform.Licenses`: Trial / Paid / Complimentary, a period and a status. An extension is
  a new row; a withdrawal sets `Revoked`. `platform.fn_EffectiveLicense` picks the licence
  in force (Paid over Complimentary over Trial).
- `LicenseGuardMiddleware` answers **402** for company work when no licence is in force or
  the company is suspended. Sign-in, `/me`, navigation, branding, the licence page and the
  owner console are marked `SkipLicenseCheck`. The product owner is never blocked.
- Plan feature codes (`organization`, `attendance`, `leave`) filter the navigation.
- Not yet done: an external licence-key registry (DailyCommerce verifies keys against
  Dailysoftbd) and plan limits (`Plans.MaxEmployees` is stored, not enforced).

## 7. Branding

- **Product branding** (`platform.PlatformBranding`, product owner): brand name as styled
  runs, tagline, colours, support contacts, logo and favicon stored in the row. Public at
  `GET /api/public/platform-branding`.
- **Company branding** (`platform.TenantSettings.BrandingJson`, `platform.settings.write`):
  company name as styled runs, logo (FileStorageService), colours, contacts.
- A styled name is a list of runs `{text, color, background, bold, italic, underline,
  strike, font, fontSize}`. It is data, bound through style bindings, never HTML.

## 8. Attendance

- `POST /api/attendance/my/punch` (multipart): position, accuracy, device id and a photo.
  The employee is resolved from the session; the client never names one.
- `attendance.usp_Punch_Record` decides, in one transaction under a per-employee lock: the
  debounce (a retry returns the first punch), the direction (first punch In; a punch too
  soon after an In repeats it), the geo-fence verdict (`geography` distance to the active
  work locations, or an office IP match), device binding, impossible travel, and the
  `DailyLogs` row (first In, last Out).
- `attendance.Settings.GeofenceMode`: `Enforce` refuses a punch outside every work location
  (THROW 52003 with the distance), `Flag` records it for Punch Review, `Off` records without
  judging. `RequireSelfie` and `RequireLocation` default to on.
- Date and time of a punch are the company's local date and time (`TenantClock`), passed to
  the procedure by the application.
- A `DailyLogs` row with `Source = 'Manual'` is HR's correction and is never overwritten by
  a punch.
- Photos are stored through FileStorageService under `tenants/{id}/punches/...` and read
  through a short-lived view URL after a permission check.
- Day verdicts (Present, Late, Leave, Holiday, WeekOff, Absent, Upcoming) come from
  `reporting.fn_AttendanceDays`, shared by the daily, monthly and employee reports.

## 9. Leave

- `leave.usp_Application_Submit`: calendar days (0.5 for a half day), no overlap with a
  pending or approved application, enough balance for types with a quota.
- Two steps: line manager (`leave.approve.l1`, direct reports only), then HR
  (`leave.approve.l2`, final, at either step). An applicant with no line manager starts at
  the HR step.
- Balance = quota + adjustments − approved − pending, per year of the start date
  (`leave.fn_Balance`).

## 10. Carried over from HRAccounts, and what is not yet

Moved: sign-in and roles, employees (current assignment only), work locations and
geo-punch with device binding and review, work schedules, holidays, manual attendance,
daily and monthly attendance, leave types, applications with two-level approval,
adjustments and the leave statement.

Still in HRAccounts only: employee history tables (branch, department, designation,
grade), contacts, bank accounts and nominees, payroll, loans, performance, assets,
expenses, accounting, notices and notifications, SMS configuration, the MAUI app, and the
extra attendance reports (continuous absent/late, overtime, heatmap). Leave attachments
and employee photos have columns but no upload endpoints yet.

## 11. Verification

Application builds, source inspection and manual smoke checks. `dotnet build
FegHR/FegHR.slnx` treats warnings as errors. A script change is deployed twice to
prove it is idempotent.
