# FegHR — Overview

Source brief for the FegHR project, transcribed from the owner's `Overview.docx`
(2026-10-12). This is the product/architecture contract the migration is built against.

## Brief

Current HRAccount is in older clean architecture. This project is not live, so breaking
it doesn't affect anything. I want a public registration landing page, a product owner
role who can switch companies, licencing, superpower like DailyCommerce. Public can
register as company Admin role by default like DailyCommerce. Also dynamic branding,
same to same like DailyCommerce — DailyCommerce-level flexible.

The current HR functionality should stay. But the architecture of both db and backend
will change, also the UI will change. So create a new project to migrate HRAccounts
into the new FegHR.

- Modular monolithic.
- SQL Server.
- UI template: 100% on this template — https://github.com/primefaces/sakai-ng
- Module-wise schema conversion (use the `iam` name for user identity auth management),
  database-level access authorization like DailyCommerce.
- For Communication, FileStorage, Logging and Datetime use the shared services.
- For now complete auth, attendance (must add geo-fencing and a picture-giving system
  during attendance) and leave management from the current HR. These three need to be
  shown first; the others will be moved one by one afterwards.
- Name the project FegHR, create a new database `db_feg_hr`. Must maintain
  sprints and handoffs, as multiple agents can work here.
- Or, if renaming and converting the current project is more feasible, do that instead —
  no worries, the master branch also has the same HR.
- Must be db-first.
