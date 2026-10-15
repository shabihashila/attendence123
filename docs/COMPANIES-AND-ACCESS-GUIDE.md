# Companies, group administration, attendance rules and user access

A **company** is a separate business in FegHR. A **child concern** is another company linked to a main company. A **branch** is an office or location inside one company. For example: FEG GROUP -> FegTechnova -> Head Office / another branch. Each concern keeps its own employees, users, roles, attendance, leave and licence.

## 1. Group Console - /app/admin/group

Use this page to oversee the companies in your current group. It brings company summaries, group access, shared configuration, billing contacts and an audit history together.

- **Group companies and reporting:** See employee/user totals, attendance days, pending leave and licence information. Select **Open company** to work inside that company. A group manager can add a concern or save a registered company name.
- **Group access:** First create the person's login under Administration -> Users. Enter that existing email, select a group role and save. **Master Group Admin** manages the group; **Group Auditor** has the group's read permissions; **Group Billing** has its billing permissions. These are powerful group-level assignments. For selected companies and selected branches, use Users -> Company access instead.
- **Global defaults / Group defaults / Effective company configuration:** The application combines global values, group values and company overrides. More specific values replace matching keys. These JSON fields store general configuration; saving an arbitrary key does not make the application implement a feature. Attendance rules, approval workflows, branding and time zone have dedicated pages. Ask a developer before editing unfamiliar JSON.
- **Billing profile:** Stores the group's billing name, email and address. This is contact information; saving it does not issue a licence or charge a payment.
- **Audit history:** Helps identify who changed group settings or access and when.

**Why it matters:** Managers can oversee related companies without mixing their employee records or treating every user as a group administrator.

## 2. Attendance Rules - /app/attendance/settings

These rules apply to the company currently selected in the top bar. Select the correct company first, change the controls, then select **Save rules**.

| Control | Meaning | Example |
| --- | --- | --- |
| Geo-fence: Enforce | Refuse attendance outside configured work locations | Staff must punch from the office area |
| Geo-fence: Flag for review | Record an outside-location punch for HR review | HR checks a travelling employee's punch |
| Geo-fence: Off | Do not judge the punch against the location boundary | Location may still be collected |
| Location is required | Refuse a punch that has no GPS position | Employee must allow browser location access |
| Photo is required | Require a photo when punching | Employee must allow camera access |
| Worst acceptable accuracy (m) | Flag a GPS position that is less accurate than this threshold | 100 means an uncertainty over 100 metres is too coarse |
| Minutes before a check-out | Prevent an immediate second tap from becoming a check-out | 5 gives a five-minute minimum gap |
| Ignore repeats within (s) | Treat quick retries and double taps as duplicates | 30 suppresses a repeat within 30 seconds |

Set up the company's **Work locations** before enforcing a geo-fence. Turning geo-fence Off does not turn off the separate Location is required switch. A tighter GPS threshold is stricter, and indoor GPS may be inaccurate.

**Why it matters:** The company can collect reliable attendance evidence and avoid accidental duplicate punches while allowing HR to review exceptions.

## 3. Product Owner -> Companies - /app/owner/companies

This is the product operator's administration page. It manages registered companies across the product. Normal employees should not need it.

- **Edit:** Change the registered name, attach an existing company to a main company, or detach it by choosing Standalone main company. Branding's display name has a separate setting.
- **Maximum child concerns:** Blank means unlimited, zero prevents new concerns, and a positive number sets a maximum. The limit cannot be lower than the concerns already attached; suspended concerns also count.
- **Add concern:** Create a separate child company under a main company. It receives its own initial trial and settings. Open the concern to configure its staff and organization.
- **Licences:** Review, issue or revoke company licences. This controls entitlement to use the product.
- **Open:** Switch your working company into that company, subject to access.
- **Suspend / Reactivate:** Control whether a company can use the system.

A main company that already has concerns cannot itself become a child. The current hierarchy supports one level of concerns.

**Why it matters:** The product operator controls the structure, licences and availability of client companies. The Group Console handles a group's daily administration.

## 4. The top-bar dropdowns

1. **Main company** chooses an accessible main-company grouping.
2. If that main company has concerns, **Company / child concern** chooses the business you are actually working in. The main company itself appears here only when you have access to it.
3. A company with no concerns has one dropdown.
4. A parent may appear as a grouping label when you only have access to one of its concerns. That label does not grant access to the parent's records.
5. Switching refreshes your permissions, menus, branding and licence information and opens the dashboard.

Only authorized working companies appear. Parent/child relationships alone do not grant access.

## 5. Assign one user to multiple companies and branches

1. Open Administration -> **Users** in a company you manage.
2. Create the login with **New user** if needed. Choose its company role(s), branch(es) and save. An existing email links the same account; it does not need a new password for each company.
3. Find the user and select **Company access**.
4. Choose additional companies in the company multiselect. The owner/superadmin sees all active companies. A company administrator sees the current company and its direct child companies. A child-company administrator sees only that child company. Administration access does not automatically grant personal access to another company's business records.
5. In each company's card, select that company's roles and one or more branches.
6. Select **Save company access** on each changed card. Changes are saved separately per company; simply choosing a company is not a save.
7. On an active accessible company/child card, select **Make default** to choose where that user starts after signing in. A main company with accessible children uses an accessible child as the working default.
8. Use **Deactivate access** to revoke access to that company. Removing a chip only hides the card and does not revoke saved access.

**Empty branch selection means all current and future branches.** Select explicit branches when access must be limited. Branch assignments use each company's own branches and roles. A role decides what the user can do; company and branch access decide where the user can do it. The same person can be HR in one company and an employee in another.

Group-managed protected users are changed through the Group Console. The product owner's access is protected. The server also rejects invalid role/branch IDs, unauthorized companies and removing the final active company administrator.

## 6. Default company and child company

Every user with active company access has one saved working default. Its parent identifies the default main company. If there are accessible child companies, the working default is one of those children. If there are no accessible children, the accessible main/standalone company is the default.

Open **My account -> Default company** to choose your default main company and (when available) child company, then select **Save default company**. Only active memberships appear. Administrators can use **Users -> Company access -> Make default** within their administration scope.

Signing in opens this saved default automatically. The top-bar dropdowns let you work in another authorized company without overwriting the saved default. When switching main-company groups, the saved default is preferred for that group; otherwise an accessible child is chosen before the main company. If access to a default is revoked, the system prefers another accessible child in the same group, then the main company, then another accessible company. It never restores revoked access.

## 7. Rate limiting in development and production

The HTTP rate-limiting middleware now runs only when ASPNETCORE_ENVIRONMENT is Production. Development and other non-production environments do not apply these HTTP request limits. Production retains the login/company-switch policy (5 requests per minute per IP) and registration policy (5 per ten minutes per IP).

The separate protection that temporarily locks an account after failed passwords still applies. Use correct credentials when testing; disabling request rate limits does not disable password protection.

## Deployment note

Fresh database deployment includes 18_CompanySwitcher.sql and 19_CompanyAccessDefaults.sql. For an existing database, apply FegHR/Database/release-1.0/18_CompanySwitcher.sql followed by 19_CompanyAccessDefaults.sql and restart the updated API. These migrations add hierarchy labels, scoped administration checks and saved defaults; they do not reseed companies or reset passwords. The change is already applied to this local development database.
