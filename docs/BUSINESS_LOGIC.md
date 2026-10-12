# FegHR User Manual Business Logic

এই document `usermanual.docx`-এর detailed business reference-এর editable companion। মূল Word manual-এর ১–১২ chapter-এ existing illustrated quick guide রাখা হয়েছে। নিচের ১৩–৩১ chapter সেই guide-এর বিস্তারিত ব্যাখ্যা; একই button-by-button instructions অপ্রয়োজনে পুনরাবৃত্তি করা হয়নি।

Reviewed version: Git commit `f2e3133`, 12 October 2026। এই review-তে application source, SQL, settings বা business data পরিবর্তন করা হয়নি।

## 13 এই manual কীভাবে ব্যবহার করবেন

Employee প্রথমে chapter ২–৫ পড়বেন; HR/Admin chapter ৬–১১-এর সঙ্গে নিচের setup, rules ও report chapters পড়বেন। Product Owner-এর জন্য chapter ২৫–২৭। কোনো feature নিয়ে সন্দেহ হলে chapter ২৮-এর status register দেখুন।

**Implemented** মানে current repository-তে সংশ্লিষ্ট working UI/API/SQL path পাওয়া গেছে। **Partial** মানে feature-এর কিছু অংশ আছে, কিন্তু UI, enforcement, integration বা business coverage অসম্পূর্ণ। **Missing** মানে current FegHR-এ ব্যবহারযোগ্য workflow পাওয়া যায়নি। Implemented label সব edge case production-tested হওয়ার দাবি নয়।

যেখানে examples আছে, সেগুলো বোঝানোর জন্য; নতুন transaction এই documentation task-এ submit করা হয়নি। আগের startup verification-এ login form, administrator login, company selection, authenticated dashboard এবং backend readiness যাচাই হয়েছিল। Attendance punches, leave approvals, registration, licensing বা settings save করে নতুন business data তৈরি করা হয়নি। এই workflows-এর rules UI source, API validation ও SQL থেকে যাচাই করা হয়েছে।

**কোম্পানি আগে মিলিয়ে নিন।** সব HR transaction active company-এর মধ্যে হয়। LightHouse Consulting ltd parent company হলেও FegTechnova বা FuturePathGlobal-এর records স্বয়ংক্রিয়ভাবে দেখতে পারে না। Company membership, role permission এবং company switch প্রয়োজন। Parent relationship hierarchy metadata; এটি cross-company authority নয়। [S01, S02, S03]

## 14 দায়িত্ব এবং Permission Matrix

Role হলো permissions-এর collection। একজন user একই company-তে একাধিক role পেতে পারেন; তাঁর effective permission সাধারণত active roles-এর মিলিত rights। Role name পরিবর্তিত হতে পারে; default role code এবং actual grants দেখে access বুঝতে হবে।

| কাজ | Employee | Line Manager | HR Manager | Company Admin |
|---|---|---|---|---|
| নিজের punch ও leave | আছে | আছে | আছে | আছে |
| Employees list | নেই | Read | Read ও Write | Read ও Write |
| Daily ও monthly attendance | নেই | Company-wide Read | Read ও correction | Read ও correction |
| Punch Review ও device approval | নেই | নেই | আছে | আছে |
| Location shift holiday rules | নেই | নেই | Read ও Write | Read ও Write |
| Leave Applications list | নিজের My Leave | Company-wide list | Company-wide list | Company-wide list |
| Leave decision | নেই | Direct report Level 1 | Level 2 final | Level 2 final |
| Leave Types এবং Adjustments | নেই | নেই | আছে | আছে |
| Leave Statement | নিজের balance | Company-wide Read | Company-wide Read | Company-wide Read |
| Users management | নেই | নেই | Read only | Read ও Write |
| Roles এবং Company Branding | নেই | নেই | Default-এ নেই | আছে |
| Product Owner console | নেই | নেই | নেই | শুধু platform owner হলে |

এই matrix seeded defaults-এর জন্য। Company Admin role সব non-owner company permission পায় by rule; তার checkbox grants কমিয়ে restricted admin বানানো যায় না। অন্য roles-এর grants Admin পরিবর্তন করতে পারেন। HR default-এ `iam.users.read` আছে, কিন্তু user create/edit permission নেই। তাই HR employee record তৈরি করতে পারলেও login account বানাতে Company Admin-এর সাহায্য লাগতে পারে। [S04, S05]

**Manager visibility ও decision আলাদা।** Seeded Manager role-এর `leave.applications.read` থাকায় company-wide leave list দেখা যায়। Level 1 approval কেবল active linked manager employee-এর direct report, Pending status এবং Manager step-এ সীমাবদ্ধ। Direct-report-only visibility চাইলে Company Admin-কে সেই read grant-এর ব্যবহার বুঝে role configure করতে হবে; default-কে team-only বলা ঠিক নয়। Attendance reports ও Leave Statement-ও default Manager-এর জন্য company-wide। [S04, S10, S11]

**Branch access Partial।** Backend-এ branch assignments এবং `X-Branch-Id` validation আছে; user API branch IDs নিতে পারে। কিন্তু current Users form branch picker দেখায় না, এবং সব list/report query assigned branches দিয়ে বাধ্যতামূলকভাবে narrow করা হয় না। Branch filter হলো report selection; এটি নিজে security boundary নয়। Tenant/company isolation implemented; universal branch-level data isolation ধরে নেবেন না। [S05, S06, S11]

### 14 1 Permission reference

| Permission | ব্যবহার |
|---|---|
| platform.settings.read | Settings ও licence menu access |
| platform.settings.write | Company Branding save |
| platform.branches.read ও platform.branches.write | Branch list ও maintenance |
| iam.users.read ও iam.users.write | User list ও membership management |
| iam.roles.read ও iam.roles.write | Role list ও grant management |
| iam.permissions.read | Permission catalog; Roles Write-ও catalog পড়তে পারে |
| org.employees.read ও org.employees.write | Employee records দেখা ও সংরক্ষণ |
| org.setup.read ও org.setup.write | Departments ও Designations |
| attendance.punch.self | নিজের Today Punch ও attendance history |
| attendance.logs.read | Daily attendance ও employee day report |
| attendance.logs.write | Manual correction এবং manual entry removal |
| attendance.review | Punch review device approval এবং photo access |
| attendance.setup.read ও attendance.setup.write | Locations schedules holidays rules |
| attendance.reports.read | Monthly Summary |
| leave.apply.self | নিজের balance applications এবং pending cancellation |
| leave.applications.read | Company-wide leave list ও employee balance read |
| leave.approve.l1 | Direct-report Manager-step decision |
| leave.approve.l2 | Final approval rejection এবং Apply on behalf |
| leave.adjust | Balance adjustments |
| leave.setup.write | Leave Types maintenance |
| leave.reports.read | Company-wide Leave Statement |
| owner.companies.manage | Product-owner navigation code |
| owner.licenses.manage | Product-owner licence navigation code |
| owner.branding.manage | Product-owner branding navigation code |

Owner API routes additionally require `IsPlatformAdmin`; company role তৈরি করে `owner.*` লিখলেই platform owner হওয়া যায় না। Sidebar grants ও plan feature codes দিয়ে filtered হয়; menu না থাকা মানেই record নেই নয়। Licence page API সব signed-in staff পড়তে পারে, যদিও menu default-এ settings read permission অনুযায়ী দেখানো হয়। [S04, S06, S15]

## 15 Account Users এবং Roles

### 15 1 Login এবং account lifecycle

Chapter ২-এর login ও company selection steps অনুসরণ করুন। Email login case-normalised হয়; mobile login backend-এর supported normalised number হিসেবে মিলতে হয়। Password case-sensitive। Minimum password ৮ characters, অন্তত একটি letter ও একটি digit; maximum ১২৮ characters। Special character বাধ্যতামূলক নয়। Default SQL policy-তে ৫ failed credential attempts-এর পরে ১৫ minutes lockout থাকে; API rate limiting আলাদাও প্রযোজ্য। [S05, S07]

Users global identity, কিন্তু TenantUser membership company-specific। একই email/mobile-এর existing user-কে নতুন company-তে link করলে তাঁর existing password থাকে; create form-এর password existing identity-র password replace করে না। একই company-তে duplicate membership block হয়। Existing পরিচয় ব্যবহার করে নতুন public registration করা যায় না; সেই flow duplicate email/mobile reject করে। [S05, S08]

নিজের profile দেখতে top bar-এর account icon থেকে **My account** খুলুন। **Current password** ও **New password** দিয়ে **Change password** চাপুন। বর্তমান password verify হয়; নতুন password global identity-তে সংরক্ষিত হয়। Current company membership-এর refresh sessions revoke হয় এবং UI পুনরায় sign-in করায়। অন্য company memberships-এর সব refresh tokens immediate revoke হচ্ছে এমন নিশ্চয়তা current code দেয় না। [S07]

**Missing recovery flows।** Current UI/API-তে Forgot password, email reset link, OTP reset, administrator password reset, MFA বা SSO workflow পাওয়া যায়নি। Password ভুলে গেলে administrator-কে escalation করুন; Users page-এ এমন reset button আছে বলে ধরে নেবেন না। Recovery বাস্তবায়িত না থাকায় manual কোনও database password-change shortcut prescribe করে না।

Sign out session refresh token invalidate করে এবং browser session clear করে; এটি attendance Check out নয়। Browser tab বন্ধ করা বা Sign out চাপা দিয়ে কর্মীর Out time তৈরি হয় না।

### 15 2 User membership manage করুন

Create user-এর basic steps chapter ৭-এ আছে। New identity হলে name, email অথবা valid mobile, policy-compliant password এবং অন্তত একটি active role দিন। Existing identity হলে display name/password নতুন করে পরিবর্তন হয় না; user-কে তাঁর existing credentials ব্যবহার করতে বলুন। Employee link আলাদা কাজ, chapter ৬ ও ১৬ দেখুন।

Edit user-এ বর্তমান UI roles এবং Active membership পরিবর্তন করে; name/email/mobile edit এবং branch assignment picker নেই। নিজের account deactivate করা যায় না। Company-তে অন্তত একজন active company administrator থাকতে হবে; Product Owner membership এই required local administrator-এর substitute নয়। Product Owner-এর company access company admin edit/remove করতে পারেন না। Membership inactive করলে ঐ membership-এর refresh sessions revoke হয়। [S05]

### 15 3 Roles configure করুন

Administration > **Roles** খুলুন। **New role** দিয়ে unique lowercase code, name, Active এবং প্রয়োজনীয় permission checkboxes দিন; **Save** করে Users-এ role assign করুন। Custom code lowercase letter দিয়ে শুরু হবে; letters, digits ও dash ব্যবহার করা যায়। Roles page-এ module অনুযায়ী permission catalog দেখা যায়। [S05]

Built-in role-এর code rename বা delete করা যায় না। Company Admin role deactivate করা যায় না, এবং তার effective grants সব company permissions। Custom role delete করার আগে staff assignment সরাতে হয়।

Non-admin delegated user/role editor নিজের role পরিবর্তন করতে পারেন না, নিজের rights-এর বাইরে grants manage করতে পারেন না, administrator role assign/remove করতে পারেন না, এবং নিজের বা administrator member-এর roles change করতে পারেন না। Role পরিবর্তনের পরে permissions cache generation invalidates হয়; UI menu পুরোনো দেখালে reload/sign in করুন। [S05]

**Scenario।** HR user employee data maintenance করেন কিন্তু Users create করতে পারেন না। তাঁকে প্রয়োজনীয় অতিরিক্ত permissions দেওয়া একটি explicit company-admin decision; role name HR হলেই সব administration rights পাওয়া যায় না।

## 16 Organization এবং Employee Onboarding

### 16 1 Company setup order

একটি ব্যবহারযোগ্য setup sequence হলো Branches > Departments > Designations > Users > Employees > Work Locations > Work Schedules > Leave Types। Public registration কিছু defaults তৈরি করে, কিন্তু real office location, actual roles এবং employee reporting lines HR/Admin-কে complete করতে হয়।

**Branches**-এ New branch দিয়ে unique Code, Name, optional Address/Phone এবং Active দিন। Branch records edit/deactivate করা যায়; current API/UI-তে branch delete workflow নেই। Code letters, digits ও dash ব্যবহার করে।

**Departments** ও **Designations**-এ unique Name ও Active দিন। Employee reference থাকলে Delete block হয়; operationalভাবে deactivate করুন। Deactivated setup item নতুন active lookups-এ আসে না, কিন্তু historical employee reference নিজের থেকে মুছে যায় না। [S09]

### 16 2 Employee record rules

Chapter ৬-এর illustrated form ব্যবহার করুন। Full name required। Code blank রাখলে next employee code generate হয়; supplied code company-তে unique হতে হবে। Gender, birth date, phone/email, joining/exit dates, employment type, branch, department, designation, reports-to এবং user account optional fields। UI employment choices Permanent, Probation, Contract, Intern, Part-time।

Exit date joining date-এর আগে হতে পারে না। Selected branch/department/designation/manager একই company-এর known record হতে হবে। Employee নিজের কাছে report করতে পারেন না; reporting-chain loop reject হয়। User account অবশ্যই company member হতে হবে, এবং একই company-তে একই user দুই employee-তে link করা যায় না। [S09]

একই global user অন্য company-তে আলাদা employee record পেতে পারেন। তাই company switch-এর পরে employee identity ও schedule নতুন company অনুযায়ী resolve হয়। Employee active record না থাকলে self-attendance ও self-leave unavailable হয়। Account deactivate এবং employee deactivate দুটি আলাদা operation।

Employee record-এ current assignment রাখা হয়; transfer/history timeline, grade history, bank details, nominees বা employee photo upload বর্তমান employee workflow নয়। Joining ও exit dates report calendar inclusion নিয়ন্ত্রণ করে; inactive employees attendance and leave statements-এর normal active list থেকে বাদ যায়।

### 16 3 Practical onboarding checklist

1. সঠিক company নির্বাচন করুন এবং active branch/department/designation আছে কি না দেখুন।
2. Company Admin Users-এ account create/link করে appropriate roles দিন।
3. HR Employees-এ record create করে নিজের login **User account**-এ link করুন।
4. **Reports to**-তে actual approving manager দিন; manager-এরও linked active employee record ও Level 1 permission দরকার।
5. Default shift যথেষ্ট না হলে effective date দিয়ে employee schedule assign করুন।
6. Approved work location ও attendance rules company policy-এর সঙ্গে মিলিয়ে নিন।
7. Employee-কে নিজের login, My Attendance, My Leave এবং reporting contact জানান।

**Success criteria।** Employee sign-in করার পরে correct name/code, own attendance card ও leave balance দেখতে পারবেন। “Sign-in is not linked to an employee record” হলে নতুন account বানানোর আগে existing employee-user link পরীক্ষা করুন।

**Offboarding।** Exit date ও Active employee status policy অনুযায়ী update করুন; প্রয়োজন হলে user company membership inactive করুন। Existing history preserve করার জন্য employee বা database delete করা onboarding/offboarding-এর requirement নয়।

## 17 Work Locations এবং Attendance Rules

### 17 1 Approved location তৈরি করুন

Attendance > **Work Locations** > **New work location** খুলুন। Name, Type, optional Branch, Latitude/Longitude, Radius এবং optional office network IP addresses দিন। GPS facility থাকলে CRUD form-এর current-location helper দিয়ে coordinates নিতে পারেন; save-এর আগে অফিসের actual position মিলিয়ে নিন। **Save**-এর পরে list-এ record ও Active state যাচাই করুন।

Office বা Client/project site-এর matched punch **InOffice**; Remote work allowed location-এর matched punch **Remote**। Radius ১০–১০০০০০ metres; latitude -৯০–৯০ ও longitude -১৮০–১৮০। Coordinates দুটোই দিতে হবে অথবা দুটোই blank রাখতে হবে। Location-এর জন্য coordinates অথবা অন্তত একটি valid IP লাগবে। Allowed IPs comma-separated individual addresses; CIDR subnet rule নয়। Name company-তে unique। [S12, S13]

IP match server-observed remote IP-এর সঙ্গে হয়; device-এর private Wi-Fi address দিলেই কাজ করবে এমন নয়। Reverse proxy থাকলে IT-কে actual IP handling যাচাই করতে বলুন। **Require Location on থাকলে GPS আগে দিতে হবে**, কারণ API missing coordinates প্রথমেই reject করে; IP fallback পরে verdict-এ ব্যবহৃত হয়। “Office IP আছে, তাই GPS সবসময় optional” বলা ঠিক নয়।

All active approved locations একই company-এর মধ্যে evaluation-এ আসে। Branch association location metadata; employee নিজের branch-এর fence-তেই limited এমন rule current punch procedure-এ নেই। Historical punch location reference থাকলে Delete action record delete না করে deactivate করে। Unused location hard-delete হতে পারে; বাস্তবে অনুমতি ও data-retention policy অনুসরণ করুন।

### 17 2 Company rules save করুন

**Attendance Rules** খুলে Photo is required, Location is required, Geofence mode, Worst acceptable accuracy, Duplicate-tap interval এবং minimum check-in-to-check-out gap policy অনুযায়ী দিন। Form-এর **Save rules** button দিয়ে save করুন; concurrent update conflict হলে reload করে নতুন values মিলিয়ে আবার save করুন।

| Rule | Default | Allowed range বা effect |
|---|---|---|
| Require Selfie | On | প্রতি new punch-এ photo required |
| Require Location | On | Valid GPS coordinate pair required |
| Geofence Mode | Enforce | Enforce Flag অথবা Off |
| Max Accuracy | 200 m | 10–5000 m; বেশি হলে flag |
| Debounce | 90 sec | 0–600 sec; recent punch return |
| Min In to Out | 5 min | 0–240 min; too-soon punch In হয় |

Default values standard initial setup-এর; company edit করতে পারে। Table-কে current company policy-এর screenshot হিসেবে ধরবেন না।

**Enforce।** Configured fences থাকলে সব approved locations-এর বাইরে বা confirmed location না পাওয়া punch refuse হয়। Reported mock location-ও refuse হয়। কিন্তু কোনও fence configured না থাকলে procedure Unverified/NO_LOCATION_CONFIGURED punch record করতে পারে; mode Enforce হলেই office setup সম্পূর্ণ হয় না। Selfie/location mandatory checks আলাদা।

**Flag।** Outside/Unverified/Suspicious punch record হয়; suspicious verdict হলে HR review queue-তে আসে। কাজের সময় manual reject/absent হয়ে যায় না।

**Off।** Geofence mismatch-এর কারণে punch block হয় না। Outside/Unverified ফল Unverified-এ normalise হয়; configured photo/location requirement, device/travel checks এবং flags চালু থাকে। Off মানে “কোনও review নেই” নয়। [S12, S13]

**GPS accuracy nuance।** Accuracy threshold exceed করলে ACCURACY_TOO_COARSE flag থাকে; এটিকে একমাত্র কারণে automatic rejection করা হয়নি। InOffice/Remote punch-এর flags থাকলেও queue location verdict অনুযায়ী filtered হওয়ায় শুধু accuracy/device flag-এর জন্য queue entry নিশ্চিত নয়। Chapter ২০-এ এই gap ব্যাখ্যা আছে।

## 18 Work Schedules Holidays এবং Assignment

### 18 1 Company default schedule

**Work Schedules**-এ **New schedule** খুলুন। Name, Description, Active, Default এবং সপ্তাহের প্রতিটি দিনের Working/Start/End/Grace দিন। **Save** করুন। একটি schedule-এর Sunday–Saturday মোট সাতটি unique day row লাগবে; অন্তত একটি working day থাকতে হবে। Working day-এর End, Start-এর পরে হতে হবে; Grace ০–৪৮০ minutes। [S13]

নতুন company-এর General Shift default হলো Sunday–Thursday 09:00–18:00, Friday/Saturday Week off, ১৫-minute grace। Company policy আলাদা হলে default edit করুন। একসময় একটি default active schedule থাকে। অন্য schedule default করলে আগের default সরিয়ে নতুনটি হয়; existing default off/deactivate করার আগে আরেকটি default বানাতে হবে। Schedule delete workflow current UI/API-তে পাওয়া যায়নি।

**Late example।** Shift starts 09:00, grace ১৫ হলে threshold 09:15। ঠিক 09:15 check-in Late নয়; 09:15-এর পরে working non-holiday date-এ Late। Default ছাড়া schedule missing থাকলে report fallback working day ও 09:15 threshold ব্যবহার করে; এই fallback real shift setup-এর বিকল্প নয়। [S11, S13]

### 18 2 Individual schedule assign করুন

Work Schedules-এর **Assign to employee** খুলুন; Employee, Schedule, Effective from এবং optional remarks দিয়ে **Assign** করুন। UI picker active employees দেয়; API employee existence এবং active schedule যাচাই করে। নতুন assignment দিলে আগের open assignment নতুন start date-এর আগের দিন close হয়। Existing open assignment একই বা পরবর্তী date-এ শুরু থাকলে নতুন request reject হয়।

Date অনুযায়ী effective employee assignment company default-এর আগে priority পায়। একাধিক applicable assignment থাকলে latest effective start/date/id নির্বাচন হয়। **Remove assignment** current assignment deactivate করে; তখন অন্য valid assignment থাকলে সেটি apply হতে পারে, নয়তো company default। পুরোনো closed assignment automatic reopen হয় না। [S13]

Assignment history stored হলেও schedule definition edit করলে পুরোনো date-এর calculation-এ current definition প্রভাব ফেলতে পারে; versioned schedule snapshot বা historical assignment data guarantee নেই। Retrospective change করলে month-end report আবার review করুন।

### 18 3 Holiday calendar

**Holidays** > **New holiday**-তে Title, Date, Remarks দিয়ে Save করুন। একই date এবং একই title duplicate reject হয়। একই date-এ different title থাকলে SQL report display-এর জন্য first holiday নেয়। Holiday company-wide; branch-specific holiday scope নেই। Edit concurrent row version যাচাই করে; Delete history/report-এর verdict বদলাতে পারে। [S13]

Holiday বা Week off-এ check-in থাকলে report Present দেখাতে পারে, তবে সেই দিনের check-in late হিসেবে judgement হয় না। Leave count calendar-day ভিত্তিক হওয়ায় holidays/week-offs leave duration থেকে automatic বাদ হয় না।

### 18 4 Overnight coverage Partial

Working-day schedule এবং manual correction-এ End/Out, Start/In-এর পরে একই দিনের time হতে হয়। 22:00–06:00 overnight shift model supported নয়। Raw punches company-local calendar date দিয়ে split হয় এবং প্রতিদিন প্রথম punch In হিসেবে শুরু হয়। Night shift, paid overtime বা break-time deduction-এর জন্য supported workflow current manual দেয় না।

## 19 Self Attendance এর Business Rules

Chapter ৪-এর camera/location workflow এখানে repeat করা হয়নি। এই chapter বোঝায় button চাপার পরে system কী সিদ্ধান্ত নেয়। আগে linked active employee, self-punch permission, valid company licence এবং required photo/location নিশ্চিত করুন।

### 19 1 Server কী নিয়ন্ত্রণ করে

Self-punch request নিজে employee ID, official direction বা official timestamp নির্ধারণ করে না। Employee session থেকে resolve হয়; official time server clock থেকে company time zone-এ convert হয়। Browser-এর client time শুধু observation। Device clock বদলে official backdated attendance দেওয়া যায় না। [S12]

একই employee/day-এর transaction serialise হয়। Debounce window-এর recent punch থাকলে নতুন record না করে প্রথম punch return করে: **Already recorded a moment ago**। Double tap বা slow network retry-তে এটি duplicate prevention। UI display দেখে success confirm করুন; বারবার চাপবেন না।

Debounce-এর বাইরে সেই date-এ প্রথম punch **In**। আগের punch In হলে minimum gap পার হওয়ার পরে **Out**; gap-এর আগে আবার In হতে পারে। আগের punch Out হলে পরেরটি In। উদাহরণ: 09:00 In, 90-sec debounce শেষ হওয়ার পর 09:03 punch এবং 5-min gap এখনও না হলে এটি আরেকটি In; সেই দিন 18:00 punch Out হতে পারে। Daily projection প্রথম In এবং শেষ Out ব্যবহার করে।

একাধিক In/Out pairs-এর total sum current Worked Minutes নয়। 09:00 In > 13:00 Out > 14:00 In > 18:00 Out হলে projection 09:00–18:00 = ৫৪০ minutes; lunch break স্বয়ংক্রিয়ভাবে বাদ হয় না।

### 19 2 Photo এবং privacy

Photo required থাকলে প্রত্যেক নতুন punch-এ fresh photo নিতে হয়। Capture-এর পরে successful submit হলে preview clear হয়। API non-empty photo, maximum ২ MB এবং actual JPEG/PNG/WebP bytes যাচাই করে। FileStorage unavailable হলে photo-store failure-এর জন্য punch complete হয় না। [S12, S17]

Photo company-prefixed unique storage key-তে থাকে। Employee নিজের punch photo দেখতে পারেন; `attendance.review` অথবা `attendance.logs.read`-যুক্ত staff company scope-এ photo দেখতে পারেন। View URL short-lived, punch-photo endpoint ১০-minute URL চায়। Link expired হলে আবার View photo খুলুন।

Photo হলো HR-review evidence। Automated face recognition, live-person verification, identity matching বা guaranteed anti-spoofing implemented নয়। Device-camera fallback actual UI-তে আছে; browser/device capture behaviour বদলাতে পারে। Production camera/location-এর জন্য HTTPS লাগে; localhost development exception আছে।

### 19 3 Location status এবং flags

| Verdict | মানে | Normal review queue |
|---|---|---|
| InOffice | Approved Office বা Site GPS/IP match | নেই |
| Remote | Approved RemoteAllowed match | নেই |
| OutsideGeofence | সব configured GPS fences-এর বাইরে | আছে |
| Unverified | Location confirmed নয় বা Off-mode mismatch | আছে |
| Suspicious | Reported mock বা impossible travel | আছে |

Verdict হলো location evidence-এর classification; এটি daily Present/Late/Absent status নয়। Accurate location থাকলেও device unknown হতে পারে; flagged punch মানেই absent নয়। [S12, S14]

| Flag | কী বোঝায় |
|---|---|
| NO_LOCATION | GPS coordinates নেই |
| NO_LOCATION_CONFIGURED | Approved GPS/IP fence configured নেই |
| OUTSIDE_ALL_FENCES | কোনও approved GPS fence-এ নেই |
| ACCURACY_TOO_COARSE | Position accuracy configured threshold-এর চেয়ে বড় |
| IP_MATCHED | Server-observed IP approved network-এর সঙ্গে matched |
| MOCK_LOCATION | Client reported simulated location |
| UNKNOWN_DEVICE | Device identifier নেই |
| UNAPPROVED_DEVICE | পরিচিত approved device list-এ নেই |
| IMPOSSIBLE_TRAVEL | Prior located punch থেকে implausible movement |

Flags reviewer-এর clue; সব flag অপরাধ বা একই severity বোঝায় না। IP_MATCHED একটি confirmation flag। Browser mock-location detection guaranteed নয়; Web UI standard geolocation observations পাঠায়, OS-level spoof detection নয়।

### 19 4 Device binding এবং travel check

Web device ID browser local storage-এ রাখা identifier; hardware identity নয়। Browser data clear, নতুন browser বা incognito session নতুন ID হতে পারে। First observed active device auto-approved হয়; পরবর্তী unfamiliar device unapproved flag পায়। Unapproved device নিজে punch block করে না। [S12]

আগের located punch থেকে অন্তত ১ km movement এবং calculated speed ৯০০ km/h-এর বেশি হলে IMPOSSIBLE_TRAVEL flag ও Suspicious verdict হয়। এই check enforcement-এর পরে হয়; Enforce mode-এও এই travel anomaly recorded/flagged হতে পারে। Timestamp, latitude, longitude বা stored observations HR review দিয়ে বদলানো হয় না।

## 20 Punch Review Manual Correction এবং Devices

### 20 1 HR review workflow

**Punch Review**-এ date range দিন; default প্রায় শেষ ৩০ দিন। Normal list unreviewed OutsideGeofence, Unverified ও Suspicious punches দেখায়। Include reviewed চালু করলে ঐ verdicts-এর reviewed rows-ও দেখা যায়। **Review** দিয়ে photo, time, employee, position/verdict ও flags পড়ুন। **Your verdict**, **Note**, **Save review** দিয়ে decision record করুন। [S14]

Review update IsReviewed, ReviewedBy, ReviewedAt, ReviewNote এবং optional LocationStatus। এটি captured timestamp/photo/GPS/device evidence rewrite করে না এবং punch cancel করে না। Reclassify InOffice/Remote করলে record ঐ filtered review list থেকে সরে যেতে পারে; history হারিয়েছে এমন নয়। Flagged punches report-এ প্রথম থেকেই counted হতে পারে; “review approved না হওয়া পর্যন্ত attendance counted নয়” এমন rule নেই।

**Scenario।** Employee client site-এ geofence mismatch পেয়েছেন। HR record/photo ও business approval দেখে Note লিখে Remote/InOffice বা existing verdict save করেন। Time ভুল হলে আলাদা manual attendance correction করতে হবে; location review দিয়ে time correction হয় না।

### 20 2 Manual attendance correction

**Daily Attendance**-এ correct date/company/employee row-এর pencil **Enter or correct attendance** খুলুন। Check-in, Check-out এবং Remarks লিখে **Save** করুন। `attendance.logs.write` permission লাগে। Future date reject হয়; Out দিলে In প্রয়োজন; Out অবশ্যই In-এর পরে হবে। দুই time blank থাকাও API গ্রহণ করে, ফলে no-check-in report verdict apply হতে পারে। Reason optional হলেও বাস্তব operational policy অনুযায়ী explanation লিখুন। [S14]

Saved daily row **Manual** source হয়। Future punches raw history-তে আসে, কিন্তু automatic reconciliation এই manual row overwrite করে না। এজন্য daily report ও raw punch timeline-এর time আলাদা হতে পারে।

**Remove manual entry** explicit removal action; manual daily correction remove হয় এবং existing active punches থাকলে first In/last Out থেকে projection rebuild হয়। Raw punches delete হয় না। No punches থাকলে no daily projection থেকে Leave/Holiday/WeekOff/Absent rules apply হয়। Removal চাপার আগে company ও employee/date আবার মিলিয়ে নিন।

### 20 3 Device approval Partial

Backend-এ `/api/attendance/devices` list এবং device approval add/remove endpoint আছে; `attendance.review` লাগে। কিন্তু current Punch Review page-এ dedicated device list বা Approve device workflow নেই। Ordinary user manual-এ nonexistent device button নির্দেশ করা হয়নি। Authorised support/API workflow ছাড়া HR UI থেকে full device management সম্ভব নয়।

Queue শুধু abnormal location verdicts নেয়। InOffice/Remote record-এ UNAPPROVED_DEVICE বা ACCURACY_TOO_COARSE flag থাকলেও normal queue-তে তা নাও আসতে পারে। এই limitation operator-কে জানতে হবে; complete all-flags review coverage implemented বলা যাবে না। [S12, S14]

## 21 Attendance Reports এবং Calculation

### 21 1 Daily status priority

Daily Attendance, Monthly Summary ও employee history একই reporting function-এর verdict ব্যবহার করে। Active employee-এর JoiningDate-এর আগে এবং ExitDate-এর পরে dates বাদ যায়। Current active employee status-ও inclusion-এ প্রভাব ফেলে; inactive staff history normal report-এ নাও দেখা যেতে পারে। [S11]

| Priority | Condition | Result |
|---|---|---|
| 1 | In time আছে | Present অথবা Late |
| 2 | In নেই এবং approved leave covers date | Leave |
| 3 | In নেই leave নেই company holiday | Holiday |
| 4 | উপরগুলো নেই schedule nonworking | WeekOff |
| 5 | Working date company Today-এর পরে | Upcoming |
| 6 | Working date today অথবা past এবং In নেই | Absent |

In time থাকলে holiday/approved leave থেকেও Present result আগে আসে। Working non-holiday date-এ check-in shift start plus grace-এর পরে হলে Late; otherwise Present। Pending leave absence excuse হিসেবে counted হয় না। আজ In না থাকলে দিন শেষের আগেও Absent দেখাতে পারে; এটি final disciplinary verdict নয়।

**Half-day limitation।** Approved half-day covers date হলে no In day-এ report Leave count ১ day হতে পারে। Attendance report DayType split করে ০.৫ leave ও ০.৫ present হিসাব করে না। Leave balance নিজে half-day count করে। Attendance report ও leave balance-এর totals সবসময় একই হবে এমন নয়।

### 21 2 Worked time এবং monthly totals

Worked Minutes = Out minus In, যখন দুটো আছে এবং Out > In। Missing Out হলে worked value blank/null; Monthly Summary সেই missing value ০ হিসেবে sum করে। এটি overtime, paid hours, deductions বা payroll payable days নয়। Overnight spans supported নয়।

Monthly Present Days-এর মধ্যে Late Days অন্তর্ভুক্ত; Present + Late আবার যোগ করলে double count হবে। Absent, Leave, Holiday, Week off আলাদা counts; Upcoming future working dates Present/Absent হিসেবে added হয় না। UI **Monthly Summary** মাস নির্বাচন করে; backend period difference maximum ৯২ days, employee day history maximum difference ৩৬৬ days। Error হলে ছোট range দিন। [S11, S14]

**Example।** ২০ attendance days-এর মধ্যে ৩ late হলে Present = ২০ এবং Late = ৩; মোট attended = ২০, ২৩ নয়। 09:30 In ও 18:00 Out হলে Worked ৫১০ minutes, lunch deduction auto নয়।

### 21 3 Filters এবং CSV

Chapter ৮-এর report selection steps অনুসরণ করুন। Date/month, Branch ও Department filter server data নির্বাচন করে। Table Search keyword, sorting ও column filters displayed rows-এ কাজ করে; **Clear** table search/column filters reset করে, company/date/branch selection নিজে সবসময় reset করে না।

Monthly Summary-তে **CSV** download implemented। Export currently loaded monthly rows ব্যবহার করে; UI table-এর search/filter করা subset-এর সঙ্গে export সবসময় এক নয়। CSV Worked column raw minutes; screen formatted hours/minutes হতে পারে। Current Daily Attendance বা Leave Statement-এ dedicated PDF/Excel export workflow পাওয়া যায়নি।

### 21 4 Dashboard বুঝুন

`attendance.logs.read` থাকলে Dashboard company-wide Headcount, Present, Late, Absent, OnLeave এবং pending work দেখায়; Employee default role company counters পায় না। Headcount ওই day-এর report-eligible active staff count, payroll headcount নয়। PendingLeave all pending applications; PunchesToReview all unreviewed abnormal location verdicts, শুধু আজকের নয়। [S11]

সিদ্ধান্তের আগে raw punches, manual source, approved leave, schedule/holiday এবং active employee dates মিলিয়ে নিন। Reports থেকে payroll deduction, overtime payment বা automated disciplinary action বর্তমান functionality নয়।

## 22 Leave Types Application এবং Balance

### 22 1 Leave policy setup

**Leave Types** > **New leave type** খুলে Code, Name, Days per year, Paid leave এবং Active দিন। Unique code API uppercase করে সংরক্ষণ করে; letters, digits ও dash allowed। Quota ০–৩৬৬; blank quota no limit। Leave type deactivate করলে নতুন apply list ও active balance list থেকে বাদ যায়; historical applications record নিজে delete হয় না। Current workflow-তে leave type delete button/API নেই। [S10]

নতুন company-এর seed defaults CL Casual ১০, SL Sick ১৪, AL Annual ১৫ এবং UL Unpaid unlimited। এগুলো software seed values, statutory entitlement বা আপনার company policy-এর ঘোষণা নয়। Paid flag reporting metadata; salary deduction বা payroll calculation চালায় না। Quota shared leave type-এ annual amount; individual exceptions adjustments দিয়ে হয়।

### 22 2 My Leave এবং Apply

Chapter ৫-এর application form steps অনুসরণ করুন। **Year** নির্বাচন করলে ঐ start-year applications ও balance আসে। **Apply for leave**-তে active type, Full day/First half/Second half, From, To, optional Reason ও Address during leave দিন। Reason API-তে optional, যদিও company-এর operational policy reason চাইতে পারে। Application submit-এর পরে status ও balance refresh করে দেখুন।

Full leave calendar dates inclusive: From 10 October এবং To 12 October = ৩ days; weekend/holiday automatic deduction বাদ নেই। HalfFirst/HalfSecond = ০.৫ day এবং From = To হতে হবে। End date Start-এর আগে নয়। Start/end difference maximum ৩৬৫ days, অর্থাৎ সর্বোচ্চ ৩৬৬ inclusive calendar dates। অতীতের leave application current API/SQL শুধু past বলে reject করে না; employee organisation policy মেনে চলবেন। [S10]

### 22 3 Overlap এবং year rule

একই employee-এর active Pending/Approved application-এর overlapping dates block হয়, leave type ভিন্ন হলেও। একই date-এর HalfFirst এবং HalfSecond একে অন্যকে overlap হিসেবে block করে না; দুই opposite halves total ১ day। Full-day versus half-day বা একই half-এর পুনরায় application block হয়। Rejected/Cancelled records নতুন date request block করে না।

সব application days **StartDate-এর year**-এ balance হিসেবে counted। December 31 থেকে January 2 Full leave হলে ৩ days start-year quota থেকে যায়; years আলাদা করে split হয় না। Annual carry-forward, monthly accrual, joining-date proration বা automatic year-end reset transaction current code-এ নেই। Year-specific query ও adjustments-এর মাধ্যমে হিসাব হয়।

### 22 4 Balance formula এবং worked example

**Remaining = Quota + Adjustment − Approved Used − Pending।** Pending days reserve হয়; submit করার সময় sufficient balance লাগে। Approved Used actual leave already consumed-by-status total, শুধু অতীতের দিন নয়। Future approved leave-ও এই year-এর Used total-এ counted হয়। Unlimited type-এ Remaining null/no limit; numeric remaining দাবি করবেন না। [S10]

উদাহরণ: Annual quota ১৫, adjustment +২, Approved ৪, Pending ৩ হলে Remaining = ১৫ + ২ − ৪ − ৩ = ১০। নতুন ২-day request submit হলে Pending ৫ এবং Remaining ৮। Request final approved হলে Used ৬, Pending ৩, Remaining এখনও ৮; approval দ্বিগুণ deduct করে না। Request reject/cancel হলে pending reservation release হয়।

Final approval-এর সময় quota আবার check হয়, application-এর own pending reservation যোগ করে available amount বিচার করা হয়। Quota বা adjustment meantime বদলে গেলে earlier displayed balance দেখে approval নিশ্চিত ধরে নেওয়া যাবে না।

### 22 5 Cancel এবং unavailable actions

নিজের **Pending** application-এ **Cancel** আছে; Manager step pass করে HR step-এ গেলেও এখনও Pending থাকলে employee cancel করতে পারেন। অন্যের application এবং Approved/Rejected/Cancelled request cancel করা যায় না। Application submit-এর পরে edit/resubmit-in-place endpoint নেই; প্রয়োজন হলে own pending cancel করে নতুন request দিন। Approved leave revoke/withdraw, attachment upload এবং retroactive leave adjustment-as-edit current workflow নয়।

## 23 Manager এবং HR Approval Workflow

### 23 1 State transition

| Current state | Actor ও action | Next state |
|---|---|---|
| Submit with Reports to | Employee অথবা HR on behalf | Pending Manager |
| Submit without Reports to | Employee অথবা HR on behalf | Pending HR |
| Pending Manager | Direct-report L1 Approve | Pending HR |
| Pending Manager | Direct-report L1 Reject | Rejected Done |
| Either Pending step | L2 Approve | Approved Done |
| Either Pending step | L2 Reject | Rejected Done |
| Any own Pending step | Employee Cancel | Cancelled Done |
| Approved Rejected Cancelled | Repeat decision | Reject request no new decision |

Status ও CurrentStep আলাদা। **With line manager** এবং **With HR** Pending-এর display stage; আলাদা final status নয়। Manager approval final approval নয়। No line manager থাকলে শুরুতেই HR। [S10]

### 23 2 Decision করার practical steps

1. **Leave Applications** খুলে Pending filter দিন; company ও applicant মিলিয়ে নিন।
2. Type, dates, duration, reason, current step এবং dialog balance দেখুন।
3. Eligible row-এর **Approve** অথবা **Reject** খুলুন।
4. Decision comments দিন; final button দিয়ে confirm করুন।
5. Application list এবং applicant My Leave-এ updated state দেখুন।

Manager-এর linked employee record ছাড়া direct-report relation resolve হয় না। শুধু role name Manager দেওয়া বা employee-এর Reports to দেওয়া যথেষ্ট নয়; manager user linkage ও Level 1 permission দুইটি দরকার। Approval scope nested indirect subordinates নয়, সরাসরি report।

### 23 3 Approval controls এবং partial separation

Level 1 নিজের leave approve/reject করতে পারে না; অন্য employee-র pending Manager-step request এবং direct-report relation লাগে। Level 2 approval direct-report relation বা Manager-first completion দাবি করে না; যে কোনও Pending step finalise করতে পারে। [S10]

**Separation of duties Partial।** Current Level 2 branch-এ same-applicant self-approval block নেই। Company Admin/HR with L2 নিজের pending application decision করতে technically পারেন। এই manual-এ company policy হিসেবে independent approver review recommend করা হয়েছে, কিন্তু এটিকে software-enforced four-eyes control বলা হয়নি। No notification/escalation job automatically next approver-কে mail/SMS পাঠায় এমন implementation নেই।

### 23 4 Apply on behalf

HR/Admin with Level 2 permission **Apply on behalf** খুলে Employee select করে normal dates/type/reason দিন। Active employee required; overlap/quota/half-day rules একই। Application employee-এর My Leave-এ আসে এবং reporting line অনুযায়ী Manager বা HR step-এ যায়। এটি submit করার সঙ্গে final approval নয়। [S10]

**Scenario।** Employee অসুস্থ হয়ে HR-কে ফোন করেছেন। HR তাঁর হয়ে SL request submit করেন। Reports to set থাকলে Manager step তৈরি হয়; L2 HR পরে explicit decision দিয়ে finalise করতে পারেন। Documentation task-এ এমন transaction তৈরি করা হয়নি।

## 24 Leave Statement এবং Balance Adjustments

### 24 1 Leave Statement পড়ুন

**Leave Statement**-এ Year ও optional Department দিন। প্রতি active employee/active leave type-এর Quota, Adjusted, Used, Pending, Remaining দেখুন। Manager default-এ company-wide statement পড়তে পারে। Table Search keyword ও Clear displayed rows narrow/reset করে; statement history payroll statement নয়। [S10, S11]

Quota blank হলে unlimited; Remaining blank/dash মানে missing deduction নয়। Application start-year, active type, employee active state এবং adjustments মিলিয়ে difference diagnose করুন। Half-day applications এখানে ০.৫ হিসাবেই Used/Pending-এ থাকে; daily attendance verdict-এর day totals আলাদা হতে পারে।

### 24 2 Adjustment workflow

**Balance Adjustments** > **New adjustment**-এ Employee, Leave type, Year, Days ও Reason দিন। Positive adds এবং negative takes away। Days nonzero এবং half-day increments; range -৩৬৬–৩৬৬, Year ২০০০–২২০০। Save-এর পরে employee balance/statement পুনরায় মিলিয়ে নিন। [S10]

**Example।** Company-approved carry-forward ২.৫ days হলে সংশ্লিষ্ট leave type/year-এ +২.৫ adjustment দিন; এটি manual administrative decision। System automatic carry-forward calculate করে না। Reason-এ policy/reference লিখুন।

Adjustment create/delete আছে, edit-in-place নেই। Wrong entry হলে authorised removal existing record inactive করে, তারপর correct replacement entry দিন। এটি deliberate business action; real system-এ প্রয়োজনীয় approval ছাড়া করবেন না। Negative adjustment lower balance বা future approval failure তৈরি করতে পারে; API adjustment save-এর সময় already approved leaves automatically reverse করে না।

## 25 Public Registration এবং Company Licensing

### 25 1 Register your company

Public landing page `/`-এ **Register your company** অথবা trial button দেখুন; `/register` form খুলে Company name, address/phone, Your name, email/mobile ও password দিন। **Create my company** submit করলে নতুন company ও administrator session তৈরি হয়। Required company/admin names minimum ২ characters, email valid, password chapter ১৫ policy মেনে হতে হবে। Existing email/mobile registration-এ conflict দেয়। [S08]

Registration এক transaction-এ company/settings, head office branch, trial licence, admin membership/role, admin employee, general shift, default leave types এবং existing Product Owners-এর memberships তৈরি করে। New admin immediately signed in হয়। Actual staff, location, schedules, roles এবং leave rules operationally complete করতে chapter ১৬-এর checklist ব্যবহার করুন।

RegistrationOpen false হলে public buttons absent/closed message থাকে এবং backend request reject করে। Product Owner policy-এর trial plan/day values offer-এ দেখানো হয়। Email verification, payment collection, onboarding email বা external licence activation এই flow-এর অংশ নয়। Company slug name থেকে generate হয় এবং conflict হলে suffix দিয়ে unique হয়; slug নিজে secret/access permission নয়।

### 25 2 Company licence status

**Licence** page-এ Kind, Plan, date period, Days left ও company status দেখুন। Company কাজের জন্য active company এবং current effective licence লাগে। Trial, Paid, Complimentary kinds আছে; owner-issued licence reference payment-confirmation নয়। Current registered company trial length policy-dependent; seeded initial policy ৬০ days, কিন্তু existing settings change হতে পারে। [S15]

Effective licence selection eligible active-period rows থেকে Paid > Complimentary > Trial priority নেয়; StartsAt <= current time এবং End null অথবা End > current time। Extension একটি new licence row; history overwrite নয়। Expired/revoked licence থাকলে valid অন্য row fallback হতে পারে।

লাইসেন্স না থাকলে বা suspended company-তে normal company work HTTP 402 দিয়ে refused হয় এবং UI licence guidance দেখায়। Sign-in, user identity/navigation, branding read, account password change ও licence information-এর exempt paths আছে। Product Owner bypass আছে; ordinary Employee/Company Admin-এর জন্য bypass নয়। Suspended/Closed company selection active membership discovery অনুযায়ী unavailable হতে পারে।

### 25 3 Plan enforcement Partial

Seeded Starter plan organization/attendance এবং Business plan organization/attendance/leave feature codes রাখে। Sidebar plan feature codes অনুযায়ী filtered হয়। কিন্তু current licence guard licence validity check করে, individual API endpoint-এ প্রতিটি plan-feature entitlement enforce করে না। Organization menu rows-ও FeatureCode null আছে। তাই “hidden menu দিয়ে complete plan access security” দাবি করা হয়নি। [S04, S06, S15]

`MaxEmployees` stored থাকলেও employee create limit enforce হয় না। MonthlyPrice metadata, payment gateway/billing/subscription collection নেই। Owner UI existing active plans list করে; dedicated create/edit plan management workflow নেই। Licence key/reference externally verify হয় না।

## 26 Company Branding এবং Product Branding

### 26 1 Company branding

Administration > **Company Branding** permission অনুযায়ী খুলুন। Display/styled name, tagline, colours এবং available contact fields সম্পাদনা করুন; **Save branding** দিয়ে result দেখুন। নামের styled runs-এ text, colour/background, bold, italic, underline, strike, font preset ও font size supported। Plain display name styled text থেকে derive হতে পারে; এটি legal company record name change-এর সঙ্গে একই action নয়। Social-link fields backend-এ আছে, কিন্তু current Company Branding form-এ editing controls নেই। [S16]

Colour valid six-digit hex, যেমন #1863DC; allowed font keys sans/display/serif/script/mono এবং run font size ৮–৭২। Form preview দেখে readable contrast ও name continuity মিলিয়ে নিন। API formatted name data validate করে; arbitrary HTML render নয়।

Company logo এবং hero upload endpoints JPEG/PNG/WebP, maximum ২ MB actual bytes গ্রহণ করে। Company logo current UI-তে upload/remove workflow আছে। Hero backend slot supported, কিন্তু current Company Branding page-এ hero upload/edit control নেই; এটি Partial। Company images tenant-confined FileStorage service-এ থাকে; branding upload ব্যর্থ হলে network/storage error হতে পারে। [S16, S17]

### 26 2 Product branding

Product Owner > **Product Branding** FegHR public/product appearance নিয়ন্ত্রণ করে; ordinary company admin নিজের brand ছাড়া product brand manage করেন না। Product name/styled segments, colours, tagline, support contacts, logo ও favicon relevant controls-এ দিন এবং save করুন। [S16]

Product logo maximum ১ MB JPEG/PNG/WebP। Favicon maximum ২৫৬ KB PNG/ICO/WebP; JPEG নয়। Product images platform database row-এ stored, company storage service-এর উপর নির্ভর করে না। Public image cache প্রায় ২৪ hours হতে পারে; old logo দেখা গেলে cache effect verify করুন, বারবার records overwrite করবেন না।

Save uses row version/concurrency checking। Another editor meantime change করলে reload করে values reconcile করুন। Product brand, company brand, legal/company metadata এবং parent hierarchy পৃথক concepts; logo save করে membership বা company access বদলায় না।

## 27 Product Owner Operations

### 27 1 Companies এবং company switching

Product Owner হলো global user-এর `IsPlatformAdmin` flag; Company Admin role-এর সমার্থক নয়। এই flag deployment/platform operation দিয়ে provision হয়; public registration user-কে Company Admin দেয়, Product Owner করে না। Companies screen active/inactive licence summary, search এবং company history দেখায়। **Open** দিয়ে owner-এর own company membership ব্যবহার করে company context switch হয়; পরের request-ও single company scope-এ চলে। [S03, S05, S15]

Company status **Suspend/Reactivate** actions UI-তে আছে। Backend Active/Suspended/Closed নিতে পারে; dedicated Closed action current UI-তে নেই। Suspension data deletion নয়। Change করার আগে company নাম/status যাচাই করুন; final manual শুধু authorised operation বোঝায়, documentation review-এ কোনও status change করা হয়নি।

### 27 2 Issue অথবা revoke licence

Companies-এর target row > **Licences** খুলুন। Existing history দেখুন; **Issue a licence** form-এ Kind, existing Plan, From/Until, optional Licence key/reference ও Note দিন। **Issue licence** explicit submit-এর পরে list ও effective status যাচাই করুন। API End <= Start reject করে; End optional indefinite licence হতে পারে। UI end-date convention selected final day-এর end ব্যবহার করে। [S15]

**Revoke** selected active row revoked করে, deletes history নয়। Revoke করার পরে another valid row থাকলে company এখনও licensed থাকতে পারে। Trial থেকে Paid যেতে reference ইচ্ছেমতো field-এ লিখলেই external payment বা key authenticity verified হয় না।

### 27 3 Licence Policy

**Licence Policy**-এ Trial days ১–৩৬৫০, active Trial Plan এবং registration-open flag নির্বাচন করে **Save policy** করুন। Change future public registrations-এর জন্য; existing trials-এর expiry automatic rewrite হয় না। Concurrent policy change হলে reload required। Existing trial বাড়াতে target company-তে new licence issue করুন। [S15]

### 27 4 Owner company এবং hierarchy

Optional `IsOwnerCompany` সর্বোচ্চ একটি company-তে থাকতে পারে। Owner company licence ছাড়াই all active feature codes পায় এবং suspend/close করা যায় না। Flag deployment operation দিয়ে set হয়; normal company-branding setting নয়। Demo parent LightHouse Consulting ltd থাকলেই owner-company flag automatically true হয় না। [S02, S15]

ParentTenantRefId hierarchy metadata seed-এ আছে; current UI-তে parent-child organization management, consolidated attendance, combined payroll বা cross-company rollup workflow পাওয়া যায়নি। Product Owner company switch করে পৃথক records review করেন; multi-company admin মানেই merged database access view নয়।

## 28 Implemented Partial এবং Missing Feature Register

এই register current FegHR repository-এর scope। পুরোনো HRAccounts-এর capability বা `OVERVIEW.md`-এর requested future scope current functionality হিসেবে ধরে নেওয়া হয়নি।

| Feature | Status | বাস্তব সীমা |
|---|---|---|
| Public landing registration trial | Implemented | Duplicate identity registration reject; no email verification/payment |
| Login company picker switching | Implemented | Active memberships; current request single company |
| My account password change | Implemented | Current password লাগে; global identity change, membership-scoped refresh revocation |
| Users Roles permission delegation | Implemented | Admin invariants; existing identity password preserved |
| Tenant isolation and dynamic navigation | Implemented | Company boundary; role/plan menu filtering |
| Branch access assignment | Partial | Backend/header checks; no Users branch picker or universal row filtering |
| Employees setup and user linking | Implemented | Current assignment; no employee history/finance extensions |
| Selfie GPS geofence IP punch | Implemented | Server time/direction; photo evidence, not biometric proof |
| Browser mock and anti-spoof assurance | Partial | Reported mock/travel flags; no guaranteed OS detection or liveness |
| Device registration approval | Partial | Auto-registration and API approval; no full management UI |
| Punch Review | Implemented | Abnormal location verdicts; all flags are not guaranteed queued |
| Manual daily correction | Implemented | Manual precedence; removal rebuilds punches |
| Day shifts assignment holiday | Implemented | Same-day times and current schedule definitions |
| Overnight shift and break totals | Missing | No supported cross-midnight or break subtraction workflow |
| Daily monthly history dashboard | Implemented | Active eligible staff; Present includes Late |
| Monthly attendance CSV | Implemented | Loaded rows; raw Worked minutes |
| General PDF or XLSX report export | Missing | No current dedicated daily/leave export workflow |
| Quota half-day leave overlap balance | Implemented | Calendar days and start-year attribution |
| Half-day attendance report split | Partial | Leave quota 0.5; daily verdict does not split fractions |
| Manager and HR decisions cancellation | Implemented | L1 direct report; L2 final either step; own pending cancel |
| Independent approver enforcement | Partial | L1 self-decision blocked; L2 self-decision not blocked |
| HR Apply on behalf | Implemented | Explicit application then separate decision |
| Leave adjustments statement | Implemented | Manual annual adjustment; no automatic accrual/carry-forward |
| Leave attachments | Partial | Storage column exists; no upload/API/form workflow |
| Employee photos | Partial | Storage field exists; no employee-photo upload workflow |
| Company name colours logo | Implemented | Tenant storage and save concurrency |
| Company hero and social-link editing | Partial | Backend fields/slot; no current Company Branding controls |
| Product branding logo favicon | Implemented | Owner-only; database-stored product images |
| Licence validity issue revoke status | Implemented | References local; no external verification/payment |
| Plan-feature access controls | Partial | Menu filtering; no universal feature endpoint gate |
| Employee plan limit | Partial | MaxEmployees stored; not enforced |
| Licence plan create edit | Missing | Current owner workflow lists existing plans only |
| Owner company special flag | Implemented | Deployment-provisioned; no normal toggle UI |
| Parent company hierarchy | Partial | Metadata and demo relationship; no management/rollup workflow |
| Communication service | Partial | Service/client exists; automatic HR notifications unwired |
| Storage logging shared time | Implemented | Infrastructure; not end-user modules |
| Forgot password admin reset MFA SSO | Missing | No current recovery or additional authentication UI/API |
| Payroll loans performance assets expenses | Missing | Not migrated into current FegHR |
| Bank nominees contacts employment history | Missing | No current employee extension workflows |
| Accounting notices notification centre MAUI | Missing | Outside current delivered FegHR modules |
| Continuous absent late overtime heatmap | Missing | Extra HRAccounts reports not migrated |
| Automatic leave accrual carry-forward | Missing | Manual adjustment only |
| Edit approved leave or revoke approval | Missing | No workflow for approved application mutation |

Implemented entries are bounded by the notes above. Partial বা Missing requirement থাকলে software change দরকার; manual completion নিজে application functionality complete করে না। [S01–S17]

### 28 1 Shared service responsibilities

**FegStorage** punch photos এবং company branding assets রাখে; failure-এ upload/view URL unavailable হতে পারে। Local storage default আছে; backend provider implementation/configuration end user বেছে নেন না। Product logo/favicon আলাদা platform database storage।

**FegCommunication** email/SMS API ও typed clients আছে। Demo configuration email disabled এবং SMS local Log provider। Attendance/Leave application/decision থেকে automatic mail/SMS sender calls পাওয়া যায়নি; “Approve-এর পরে employee SMS পাবেন” এমন promise এই manual দেয় না। Delivered service success এবং actual external message delivery এক বিষয় নয়।

**Shared.Logging** এবং **Shared.Time** support diagnostics ও consistent official timestamp দেয়। User-facing audit-history browser, attendance fraud decision, payroll time calculation বা scheduled notification system এদের থেকে automatic পাওয়া যায় না। Integration paths reviewed; documentation review-এ email/SMS পাঠানো বা storage file upload/delete করা হয়নি। [S17]

## 29 Troubleshooting এবং Support Handoff

Chapter ১২-এ everyday troubleshooting আছে। নিচের checklist HR/Admin-এর জন্য; কোনও problem solve করতে database reset, user recreation বা destructive cleanup default পদক্ষেপ নয়।

| Symptom | আগে কী দেখবেন | Owner of follow-up |
|---|---|---|
| Wrong credentials | Email/mobile normalisation password এবং lockout | User এবং authorised account support |
| Sign-in linked employee নয় | Active company membership user link employee Active | HR/Admin |
| Menu বা button নেই | Active roles actual grants licence feature menu | Company Admin |
| Cannot decide leave | Pending step direct reports L1/L2 and manager linkage | HR/Admin |
| Not enough balance | Start-year quota pending adjustments inclusive dates | HR |
| Dates overlap | Pending/Approved full or same-half request | Employee/HR |
| Location required | Browser permission valid coordinates secure origin | Employee/IT |
| Outside location | Coordinates radius active fences mode office IP | HR/IT |
| Photo could not be stored | Required photo bytes/size storage service | IT support |
| Photo link unavailable | Reopen expired view URL permissions storage | HR/IT |
| Punch recorded but report differs | Manual row first In/last Out holiday leave date | HR |
| Latest changes cannot save | Stale row version another editor | Reload reconcile then save |
| Licence restriction or 402 | Active company current licence expiry/revocation | Product Owner |
| Monthly CSV differs from filtered table | Export uses loaded monthly rows not all view filters | HR |

401 সাধারণত session invalid/expired; পুনরায় sign-in করুন। 403 permission/registration/branch constraint হতে পারে; displayed message পড়ুন। 409 linking/duplicate/concurrency conflict হতে পারে। 422 invalid business input; values সংশোধন করুন। 429 rate limit; wait করুন। Photo-store 502 হলে storage integration check দরকার। শুধু numeric error code দেখে নতুন database/setup চালাবেন না।

Support-কে company name, active user/role, page name, employee code/ID, relevant date/range, exact message, attempted action এবং success/failure time দিন। Password, JWT, refresh token, service key বা unrelated employee photo পাঠানোর প্রয়োজন নেই। Screenshot দিলে প্রয়োজনমতো personal details limit করুন।

### 29 1 Data correction policy

Employee/attendance/leave data correction responsible role-এর current supported action দিয়ে হবে। Employee code/login mismatch আগে correct mapping দিয়ে solve করুন। Manual attendance removal, adjustment removal, role deletion, logo removal, licence revocation বা company suspension প্রত্যেকটি real business change; প্রয়োজনীয় authority ও policy অনুযায়ী করুন। Documentation review-এ এগুলো execute করা হয়নি।

Database backup, deployment credentials, external mail/SMS configuration ও recovery operations IT/operator-এর responsibility। এই manual user workflow ব্যাখ্যা করে; destructive SQL বা production database reset procedure দেয় না।

## 30 Practical End to End Scenarios

### 30 1 নতুন employee প্রথম দিন কাজে যোগ দিলেন

**Actors:** Company Admin, HR, Employee। **Preconditions:** correct company, valid licence, office location এবং applicable shift। Admin Users-এ identity create/link করেন; HR Employees-এ record, joining date, department/designation, reports-to ও user account set করেন। Employee company select করে My Attendance-এ নিজের নাম দেখেন, GPS ও photo প্রস্তুত করেন, Check in now submit করেন এবং recorded time দেখেন।

**Expected result:** server-dated raw In punch এবং daily projection; working date-এ shift plus grace-এর তুলনায় Present/Late। Link না থাকলে punch করতে পারবেন না; account/password নতুন করে বানানোর আগে mapping ঠিক করতে হবে। Photo/GPS permission employee নিজে দেন। Chapter ৬, ৭, ১৬, ১৭, ১৯ দেখুন।

### 30 2 একই employee দুই company-তে কাজ করেন

**Actors:** member employee ও respective Admins। **Preconditions:** global user-এর দুই active membership; দুই company-তেই আলাদা linked employee records। Employee picker/top bar দিয়ে company A select করে A-এর attendance দেখেন; company B select করলে B-এর employee/schedule/leave context আসে।

**Expected result:** এক company-এর check-in দিয়ে অন্য company-এর attendance automatic পূরণ হয় না। Parent-child metadata data sharing entitlement দেয় না। Cross-company consolidated report current feature নয়। Chapter ২, ১৩, ১৬, ২৭ দেখুন।

### 30 3 GPS drift এবং client-site attendance

**Actors:** Employee, HR। **Preconditions:** approved office/site location এবং declared company policy। Employee near office হলেও inaccurate GPS পেলে Refresh location করেন। Enforce configured fence-এর বাইরে record reject করতে পারে; Flag mode-এ outside verdict record ও review হয়। HR photo/time/location evidence review করেন এবং explanatory Note save করেন।

**Expected result:** location decision history changes, captured timestamp/GPS/photo থাকে; attendance time correction আলাদা manual workflow। Accuracy/device flag সব ক্ষেত্রে queue-তে আসে না; company policy ও reviewer scope মিলিয়ে operate করতে হবে। Chapter ১৭, ১৯, ২০ দেখুন।

### 30 4 Check out ভুলে যাওয়া অথবা network retry

**Actors:** Employee, HR। Employee Sign out করে চলে গেলে Out punch হয় না। পরদিনের প্রথম punch নতুন date-এর In; আগের দিনের Out হিসেবে বসে না। HR attendance evidence দেখে past day-এর manual In/Out correction এবং Remarks দেন।

একই moment slow connection-এ duplicate tap করলে debounce recent record return করে; এটি new check-out নয়। **Expected result:** manual day report authoritative এবং raw punch evidence untouched। Missing Out without correction-এর Worked time blank/zero aggregation; software নিজে full shift end invent করে না। Chapter ৪, ১৯–২১ দেখুন।

### 30 5 দুই দিনের leave এবং manager approval

**Actors:** Employee, linked Line Manager, HR। **Preconditions:** sufficient quota, non-overlapping dates, valid reports-to relation। Employee My Leave-এ দুই inclusive calendar dates Full leave apply করেন। Pending balance ২ days reserve হয়। Manager Approve দিলে Pending HR হয়; HR Approve দিলে Approved Done।

**Expected result:** manager action final leave approval নয়; final approval Used বাড়ায়, Pending কমায়, balance double deduct হয় না। L1 manager নিজের আবেদন decide করতে পারেন না; L2 self-decision technical gap company policy দিয়ে handle করতে হবে। Chapter ২২–২৪ দেখুন।

### 30 6 Half day এবং year boundary

এক date-এ HalfFirst ও HalfSecond পৃথক requests allowed হতে পারে; Full request existing half-এর সঙ্গে overlap reject হয়। Half-day quota ০.৫; daily report approved no-In date-এ Leave ১ দিন দেখাতে পারে। December 31–January 2 request start-year-এ ৩ days counted হয়।

**Expected result:** HR leave balance ও attendance day count আলাদা definitions দিয়ে reconcile করেন। Cross-year split বা half-day report fraction আছে ধরে monthly payroll adjustment করবেন না। Chapter ২১–২২ দেখুন।

### 30 7 Carry forward এবং pending reservation

**Actors:** HR/Admin with leave.adjust। Verified company carry-forward entitlement ২.৫ days হলে chosen year/type-এ +২.৫ adjustment এবং reference reason save করুন। Statement-এ adjusted amount ও remaining দেখুন। Negative adjustment দিলে existing Pending/Approved totals আবার মিলিয়ে নিন।

**Expected result:** adjustment হিসাবের ওপর প্রভাব ফেলে; automatic carry-forward engine বা application reversal চলে না। Wrong adjustment edit-in-place নেই; authorised remove-and-replace workflow। Chapter ২৪ দেখুন।

### 30 8 Trial শেষ এবং licence renewal

**Actors:** Company Admin, Product Owner। Admin Licence page-এর expiry/restriction দেখেন এবং support-কে correct company reference দেন। Owner Companies > Licences-এ existing plan দিয়ে new Paid/Complimentary row issue করেন; validity dates মিলিয়ে company status যাচাই করেন।

**Expected result:** valid licence থাকলে company work আবার allowed; data preserve হয়। New policy save পুরোনো trial extend করে না। Licence key reference payment proof নয়। Revoke existing row করলে valid fallback থাকতে পারে। Chapter ২৫–২৭ দেখুন।

### 30 9 HR month end reconciliation

HR আগে Manual entries ও abnormal punches review করেন, approved leave এবং current schedules/holidays মেলান। Monthly Summary-এর Present-এর মধ্যে Late আছে বুঝে totals পড়েন। CSV download loaded data দিয়ে reconcile করেন; table-only search result export-এ আলাদা হতে পারে। Joining/exit/inactive staff এবং future Upcoming dates scope check করেন।

**Expected result:** supported attendance/leave summary পাওয়া যায়। Overtime, paid work hours, payroll deductions বা accounting ledger তৈরি হয় না; এই downstream capabilities Missing register-এ আছে। Chapter ২০–২১, ২৪, ২৮ দেখুন।

## 31 Glossary Sources এবং Verification Record

### 31 1 সহজ glossary

| Term | এখানে অর্থ |
|---|---|
| Tenant বা Company | একটি data-isolated organisation |
| User | Global sign-in identity |
| Membership | User-এর company-specific active access |
| Employee | Company-specific HR/personnel record |
| Role এবং Permission | কাজের grouping এবং specific granted action |
| Product Owner | Platform administrator flag; global control plane |
| Company Admin | Current company administration role |
| Punch | Raw server-timed In/Out event ও evidence |
| Daily Log | First In/last Out projection অথবা HR manual correction |
| Location Verdict | InOffice Remote Outside Unverified Suspicious |
| Attendance Status | Present Late Leave Holiday WeekOff Absent Upcoming |
| Pending এবং CurrentStep | Undecided application ও Manager/HR stage |
| Used এবং Remaining | Approved total এবং quota-based balance |
| Adjustment | Year/type-specific manual balance change |
| Licence | Company usage permission valid for a time period |
| Row version | Concurrent edit detect করার revision value |

### 31 2 Source register

Paths repository root `E:/Attendence_system` থেকে relative। এই references technical support/review-এর জন্য; daily users-এর source code পড়ার প্রয়োজন নেই।

**S01 Product scope:** `docs/OVERVIEW.md` এবং `docs/ARCHITECTURE.md`। Requested scope versus architecture decisions এবং inherited gaps।

**S02 Demo and hierarchy:** `README.md`, `scripts/Setup-Local.ps1`, `Database/Seed-Companies.sql`, `Database/Seed-OwnerEmployee.sql`, `FegHR/Database/release-1.0/12_CompanyHierarchy.sql`। Demo creation credential preservation parent relationship।

**S03 Tenant isolation:** `FegHR/Database/release-1.0/02_Security.sql`, `10_AppAccess.sql`; `FegHR/src/BuildingBlocks/FegHR.SharedKernel/Tenancy/` এবং Persistence connection binding।

**S04 Default permissions:** `FegHR/Database/release-1.0/SeedData/01_Metadata.sql`; `04_Iam.sql` permission and navigation functions।

**S05 User and role controls:** `FegHR/src/Modules/Iam/FegHR.Modules.Iam/Features/Users/UserEndpoints.cs`, `Features/Roles/RoleEndpoints.cs`, `Security/DelegationGuard.cs`; `FegHR/Ui/FegHR.Ui/src/app/pages/admin/users.ts`।

**S06 Navigation and branch scope:** Iam `Features/Me/MeEndpoints.cs`, `Security/AccessServices.cs`; SharedKernel `Tenancy/TenantResolutionMiddleware.cs`; UI `src/app.routes.ts` ও `src/app/core/api.ts`।

**S07 Authentication and passwords:** Iam `Security/PasswordHasher.cs`, `LoginNormalizer.cs`, `TokenService.cs`, `Features/Auth/AuthService.cs`, `Features/Me/MeEndpoints.cs`; `04_Iam.sql`; UI `src/app/pages/account.ts`।

**S08 Registration defaults:** Iam `Features/Registration/RegistrationEndpoints.cs`; `FegHR/Database/release-1.0/09_Onboarding.sql`; UI `src/app/pages/public/auth.ts` ও `landing.ts`।

**S09 Organization:** Organization `Features/EmployeeEndpoints.cs`, `SetupEndpoints.cs`; Platform `Features/Branches/BranchEndpoints.cs`; `05_Organization.sql`; UI `src/app/pages/crud.configs.ts` ও `crud.ts`।

**S10 Leave workflow:** `FegHR/Database/release-1.0/07_Leave.sql`; Leave `Features/LeaveEndpoints.cs`; UI `src/app/pages/leave/leave.ts` ও leave CRUD configs।

**S11 Report definitions:** `FegHR/Database/release-1.0/08_Reporting.sql`; Reporting `ReportingModule.cs`; UI `src/app/pages/dashboard.ts` এবং attendance report/leave statement components।

**S12 Punch algorithm:** `FegHR/Database/release-1.0/06_Attendance.sql`; Attendance `Features/SelfServiceEndpoints.cs`; UI `src/app/pages/attendance/my-attendance.ts`।

**S13 Attendance setup:** Attendance `Features/SetupEndpoints.cs`; UI `src/app/pages/attendance/settings.ts`, `schedules.ts`, `crud.configs.ts`।

**S14 Review and corrections:** Attendance `Features/ManagementEndpoints.cs`; UI `src/app/pages/attendance/review.ts`, `reports.ts`।

**S15 Licensing and owner:** Platform `Features/Licensing/LicenseEndpoints.cs`; SharedKernel `Security/LicenseGate.cs`; `03_Platform.sql`, `11_OwnerCompany.sql`; UI `src/app/pages/owner/owner.ts`, `admin/licence.ts`।

**S16 Branding:** Platform `Features/Branding/BrandingEndpoints.cs`, `PlatformBrandingEndpoints.cs`, `Features/Media/MediaEndpoints.cs`; UI `src/app/pages/admin/branding.ts`, `core/name-editor.ts`, `core/branding.ts`।

**S17 Infrastructure:** `FegHR/src/BuildingBlocks/FegHR.SharedServices.Clients/`; `SharedServices/FegStorage.WebApi/`, `FegCommunication.WebApi/`, `Shared.Logging/`, `Shared.Time/`; service default settings এবং source usage inspection।

### 31 3 Verified rule checklist

| Rule | Evidence | Consequence |
|---|---|---|
| Single company scope no parent bypass | S02 S03 | Switch membership ছাড়া merged access নয় |
| Manager read versus L1 decide | S04 S10 | Default visibility broad decision narrow |
| Server time and punch direction | S12 | Client clock official backdate নয় |
| Debounce versus minimum gap | S12 S13 | Duplicate return এবং repeated In পৃথক |
| Manual projection priority | S12 S14 | Later punches manual times overwrite নয় |
| Report priority and Late inclusion | S11 | No double count; In first priority |
| Calendar-day leave and start-year | S10 | Holidays/year split automatic নয় |
| Pending reserve and approval recheck | S10 | Balance changes before final decision matter |
| L2 may finalise either Pending step | S10 | Mandatory L1-first enforcement নেই |
| Licence precedence and owner exception | S15 | Effective row valid at current time |
| Branch and plan-feature coverage gaps | S04 S06 S15 | Menu filter universal security নয় |
| Communication not wired to HR events | S17 | Automatic notifications promised নয় |

Original saved Word quick guide reviewed in full: ১২ chapters, ১২ illustrations, ২ original tables। Correct introductory workflows preserved; inaccurate admin-reset implication corrected, and unstable hardcoded demo employee code removed. Original obsolete fixed-page index replaced with updated Word TOC. Architecture and Overview contracts preserved as source references, without rewriting their historical requirements.

Rendered layout verification covers the final Word pages, Bangla glyphs, headings, tables, original illustrations, TOC and page numbering। Business-rule verification is source-based as described in chapter ১৩; implemented/partial/missing classification does not imply destructive business scenarios were executed.
