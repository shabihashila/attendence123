import { Routes } from '@angular/router';

import { AppLayout } from './app/layout/component/app.layout';

import { authGuard, permissionGuard } from './app/core/api';

import { AuthPage } from './app/pages/public/auth';

import { LandingPage } from './app/pages/public/landing';

import * as crud from './app/pages/crud.configs';



const crudPage = () => import('./app/pages/crud').then((m) => m.CrudPage);



/**

 * The paths under `/app` match the routes seeded in `iam.Menus`

 * (Database/release-1.0/SeedData/01_Metadata.sql). A new page needs both a route here and a

 * menu row there.

 */

export const appRoutes: Routes = [

    { path: '', component: LandingPage, pathMatch: 'full' },

    { path: 'login', component: AuthPage },

    { path: 'register', component: AuthPage, data: { register: true } },

    {

        path: 'app',

        component: AppLayout,

        canActivate: [authGuard],

        canActivateChild: [permissionGuard],

        children: [

            { path: '', redirectTo: 'dashboard', pathMatch: 'full' },

            { path: 'dashboard', loadComponent: () => import('./app/pages/dashboard').then((m) => m.DashboardPage) },

            { path: 'account', loadComponent: () => import('./app/pages/account').then((m) => m.AccountPage) },



            { path: 'attendance/my', data: { permission: 'attendance.punch.self' }, loadComponent: () => import('./app/pages/attendance/my-attendance').then((m) => m.MyAttendancePage) },

            { path: 'attendance/daily', data: { permission: 'attendance.logs.read' }, loadComponent: () => import('./app/pages/attendance/reports').then((m) => m.DailyAttendancePage) },

            { path: 'attendance/monthly', data: { permission: 'attendance.reports.read' }, loadComponent: () => import('./app/pages/attendance/reports').then((m) => m.MonthlyAttendancePage) },

            { path: 'attendance/review', data: { permission: 'attendance.review' }, loadComponent: () => import('./app/pages/attendance/review').then((m) => m.PunchReviewPage) },

            { path: 'attendance/locations', data: { permission: 'attendance.setup.read', crud: crud.workLocations }, loadComponent: crudPage },

            { path: 'attendance/schedules', data: { permission: 'attendance.setup.read' }, loadComponent: () => import('./app/pages/attendance/schedules').then((m) => m.SchedulesPage) },

            { path: 'attendance/holidays', data: { permission: 'attendance.setup.read', crud: crud.holidays }, loadComponent: crudPage },

            { path: 'attendance/settings', data: { permission: 'attendance.setup.write' }, loadComponent: () => import('./app/pages/attendance/settings').then((m) => m.AttendanceSettingsPage) },



            { path: 'leave/my', data: { permission: 'leave.apply.self' }, loadComponent: () => import('./app/pages/leave/leave').then((m) => m.MyLeavePage) },

            { path: 'leave/applications', data: { permission: ['leave.applications.read', 'leave.approve.l1', 'leave.approve.l2'] }, loadComponent: () => import('./app/pages/leave/leave').then((m) => m.LeaveApplicationsPage) },

            { path: 'leave/statement', data: { permission: 'leave.reports.read' }, loadComponent: () => import('./app/pages/leave/leave').then((m) => m.LeaveStatementPage) },

            { path: 'leave/adjustments', data: { permission: 'leave.adjust', crud: crud.leaveAdjustments }, loadComponent: crudPage },

            { path: 'leave/types', data: { permission: 'leave.setup.write', crud: crud.leaveTypes }, loadComponent: crudPage },



            { path: 'organization/employees', data: { permission: 'org.employees.read', crud: crud.employees }, loadComponent: crudPage },

            { path: 'organization/departments', data: { permission: 'org.setup.read', crud: crud.departments }, loadComponent: crudPage },

            { path: 'organization/designations', data: { permission: 'org.setup.read', crud: crud.designations }, loadComponent: crudPage },

            { path: 'organization/branches', data: { permission: 'platform.branches.read', crud: crud.branches }, loadComponent: crudPage },



            { path: 'admin/users', data: { permission: 'iam.users.read' }, loadComponent: () => import('./app/pages/admin/users').then((m) => m.UsersPage) },

            { path: 'admin/roles', data: { permission: 'iam.roles.read' }, loadComponent: () => import('./app/pages/admin/users').then((m) => m.RolesPage) },

            { path: 'admin/branding', data: { permission: 'platform.settings.write' }, loadComponent: () => import('./app/pages/admin/branding').then((m) => m.CompanyBrandingPage) },

            { path: 'admin/licence', loadComponent: () => import('./app/pages/admin/licence').then((m) => m.LicencePage) },



            { path: 'owner/companies', data: { owner: true }, loadComponent: () => import('./app/pages/owner/owner').then((m) => m.OwnerCompaniesPage) },

            { path: 'owner/licence-policy', data: { owner: true }, loadComponent: () => import('./app/pages/owner/owner').then((m) => m.LicencePolicyPage) },

            { path: 'owner/branding', data: { owner: true }, loadComponent: () => import('./app/pages/admin/branding').then((m) => m.ProductBrandingPage) }

        ]

    },

    { path: '**', redirectTo: '' }

];
