import { Component, inject, signal } from '@angular/core';

import { RouterModule } from '@angular/router';

import { Api, Session } from '@/app/core/api';

import { Notify, PageHeader, UI, date } from '@/app/core/ui';



interface Dashboard {

    date: string;

    employeeName: string | null;

    hasCompanyView: boolean;

    headcount: number;

    present: number;

    late: number;

    absent: number;

    onLeave: number;

    pendingLeave: number;

    punchesToReview: number;

    licence: { kind: string | null; planName: string | null; endsAt: string | null; daysLeft: number | null } | null;

}



@Component({

    standalone: true,

    imports: [...UI, RouterModule, PageHeader],

    template: `<app-page-header [title]="'Good day' + (data()?.employeeName ? ', ' + data()!.employeeName : '')" [subtitle]="data() ? date(data()!.date) + ' · ' + (session.me()?.tenant?.name || '') : ''">

            @if (session.can('attendance.punch.self')) {

                <p-button label="Record attendance" icon="pi pi-map-marker" routerLink="/app/attendance/my" />

            }

            @if (session.can('leave.apply.self')) {

                <p-button label="Apply for leave" icon="pi pi-send" severity="secondary" [outlined]="true" routerLink="/app/leave/my" />

            }

        </app-page-header>



        @if (data()?.licence?.kind === 'Trial') {

            <p-message severity="info" styleClass="mb-6 w-full" [text]="'You are on a free trial with ' + (data()!.licence!.daysLeft ?? 0) + ' day(s) left.'" />

        }



        @if (data()?.hasCompanyView) {

            <div class="grid grid-cols-12 gap-6">

                @for (t of tiles(); track t.label) {

                    <div class="col-span-12 sm:col-span-6 xl:col-span-3">

                        <a class="card !mb-0 block" [routerLink]="t.link">

                            <div class="flex justify-between items-start">

                                <div>

                                    <span class="block text-muted-color font-medium mb-3">{{ t.label }}</span>

                                    <div class="text-surface-900 dark:text-surface-0 font-semibold text-3xl">{{ t.value }}</div>

                                </div>

                                <div class="flex items-center justify-center rounded-border" [class]="t.tone" style="width:2.75rem;height:2.75rem"><i [class]="t.icon + ' text-xl'"></i></div>

                            </div>

                            <span class="text-muted-color text-sm block mt-3">{{ t.note }}</span>

                        </a>

                    </div>

                }

            </div>

        } @else if (data()) {

            <div class="card">

                <h2 class="text-xl font-semibold mt-0">Your day</h2>

                <p class="text-muted-color mb-0">Use the menu to record your attendance, see your attendance history and apply for leave.</p>

            </div>

        }`

})

export class DashboardPage {

    private api = inject(Api);

    private notify = inject(Notify);

    session = inject(Session);

    data = signal<Dashboard | null>(null);

    date = date;



    constructor() {

        this.api

            .get<Dashboard>('/api/reporting/dashboard')

            .then((d) => this.data.set(d))

            .catch((e) => this.notify.fail(e));

    }



    tiles() {

        const d = this.data()!;

        return [

            { label: 'Employees', value: d.headcount, note: 'Active today', icon: 'pi pi-users', tone: 'bg-blue-100 dark:bg-blue-400/10 text-blue-500', link: '/app/organization/employees' },

            { label: 'Present', value: d.present, note: `${d.late} came in late`, icon: 'pi pi-check-circle', tone: 'bg-green-100 dark:bg-green-400/10 text-green-500', link: '/app/attendance/daily' },

            { label: 'Absent', value: d.absent, note: `${d.onLeave} on approved leave`, icon: 'pi pi-user-minus', tone: 'bg-red-100 dark:bg-red-400/10 text-red-500', link: '/app/attendance/daily' },

            { label: 'Waiting for you', value: d.pendingLeave + d.punchesToReview, note: `${d.pendingLeave} leave · ${d.punchesToReview} check-ins to review`, icon: 'pi pi-inbox', tone: 'bg-amber-100 dark:bg-amber-400/10 text-amber-500', link: d.pendingLeave ? '/app/leave/applications' : '/app/attendance/review' }

        ];

    }

}
