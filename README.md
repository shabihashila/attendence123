# FEG Microservices

FegHR is the FEG-branded fork of DailyHR, with dedicated communication and storage services. Open `FEG-MICROSERVICES.slnx` in Visual Studio, or use the commands below.

## Set up and run

Install .NET 10 SDK, Node.js 22.12 or later in a supported LTS release, SQL Server and `sqlcmd`. Windows authentication needs permission to create the three local databases. From this directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Setup-Local.ps1
dotnet build FEG-MICROSERVICES.slnx
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Start-Local.ps1
```

Open <http://localhost:4425>. Sign in with `Administrator@feghr.com` / `Admin123@!`, choose a company, and use the company selector to switch.

Setup creates `db_feg_hr`, `db_feg_communication`, and `db_feg_storage`. It deploys versioned SQL, seeds the demo, and generates JWT and service keys in ignored `appsettings.Development.json` files. Repeat setup preserves existing passwords, keys, and demo employees; it never drops a database. Use `-SkipUiInstall` when dependencies are installed. Setup targets this machine only and is a demo installer.

| Application | URL | Project |
|---|---|---|
| FegHR UI | http://localhost:4425 | `FegHR/Ui/FegHR.Ui` |
| FegHR API | http://localhost:5975 | `FegHR/src/Host/FegHR.Api` |
| FegCommunication | http://localhost:5185 | `SharedServices/FegCommunication.WebApi` |
| FegStorage | http://localhost:5285 | `SharedServices/FegStorage.WebApi` |

Swagger is at `/swagger`; HR readiness at `/health/ready`. The launcher records process IDs and output under ignored `artifacts/`. Run services individually with `dotnet run --project <project-folder> --launch-profile http`; run `npm start` in the UI folder.

## Demo companies and data

LightHouse Consulting ltd is the parent of FegTechnova and FuturePathGlobal. The relationship is in `platform.Tenants.ParentTenantRefId`; access stays independently scoped to each company through SQL row-level security.

Each company has eleven demonstration employees plus a linked employee record for the FegHR administrator, branches, departments, designations, shifts, locations, devices, a month of attendance, holidays, leave applications, adjustments, role grants and memberships. FegTechnova includes Shabiha Shila, Ayshi, Fariha and Abid. The owner can use My Attendance in all three companies. Every table has demo rows, including inert expired refresh tokens and shared service audit rows. Setup creates an actual storage demo file.

Company administrators: `admin@fegtechnova.feghr.test`, `admin@futurepathglobal.feghr.test`, `admin@lighthouse.feghr.test`. Role examples: `admin.demo.<slug>@feghr.test`, `hr.demo.<slug>@feghr.test`, `manager.demo.<slug>@feghr.test`, `employee.demo.<slug>@feghr.test`. Slugs: `fegtechnova`, `futurepathglobal`, `lighthouse-consulting`. All newly created demo accounts use `Admin123@!`.

Email is disabled and SMS uses the local Log provider. Demo setup and smoke checks do not send messages externally. Shared service endpoints require the generated FegHR API key.

## Database and deployment

HR schema and procedures: `FegHR/Database/release-1.0`; `deploy-db.ps1` applies them transactionally. Shared schemas and seeds: `Database/`. Setup generates the BCrypt hash; SQL stores password hashes.

To run a downloaded copy, copy the source to the target machine and run the local installer against its SQL Server. For hosting, deploy the versioned scripts using an authorized SQL account and create `appsettings.Production.json` for each service with fresh secrets, database connections, HTTPS service URLs, and actual CORS origins. Configure mail/SMS delivery separately.

```powershell
cd FegHR/Ui/FegHR.Ui
npm ci
npm run build
```

UI output is `dist/sakai-ng/browser`. Serve with an SPA fallback and reverse proxy `/api`, `/health` to HR. Camera and location require HTTPS except on localhost.

## Source and branding

Source: `SakibASP/Sakib-Microservices`, branch `v2-commerce-suite`, commit `2ef01588b20c3bf2c3749813856fcad2b69dd30d`. The source checkout was switched and pulled before copying. The requirement's `v2-commerce-suit` spelling resolves to the existing `v2-commerce-suite` branch.

The logo is bundled in `FegHR/Ui/FegHR.Ui/public/feg-technova-logo.png` and the branding SQL seed. Blue `#1863DC` and teal `#0F9F8F` come from the [FEG TechNova website](https://fegtechnova.com). Company branding remains editable.

Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Package-Source.ps1` to create `FEG-MICROSERVICES-source.zip`. It includes source, locked UI dependencies and SQL/install scripts, and excludes installed dependencies, builds, local secrets, stored files and test artifacts.

See [architecture](docs/ARCHITECTURE.md). Inherited scope includes organization, attendance, leave, licensing, dynamic menus and the product owner console. Payroll and features present only in HRAccounts are outside this DailyHR fork.
