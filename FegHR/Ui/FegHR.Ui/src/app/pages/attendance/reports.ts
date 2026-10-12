import { Component, computed, inject, signal } from '@angular/core';

import { Api, Session } from '@/app/core/api';

import { Notify, PageHeader, UI, date, isoDate, label, minutes, severity, time } from '@/app/core/ui';

import { Lookups, Option } from '../crud';



interface DailyRow {

    employeeId: number;

    code: string;

    fullName: string;

    department: string | null;

    designation: string | null;

    branch: string | null;

    inTime: string | null;

    outTime: string | null;

    shiftStart: string | null;

    workedMinutes: number | null;

    status: string;

    note: string | null;

    source: string | null;

}

interface MonthlyRow {

    employeeId: number;

    code: string;

    fullName: string;

    department: string | null;

    designation: string | null;

    presentDays: number;

    lateDays: number;

    absentDays: number;

    leaveDays: number;

    weekOffDays: number;

    holidayDays: number;

    workedMinutes: number;

}



/** Shared filter state: branch and department pickers. */

abstract class AttendanceReport {

    protected api = inject(Api);

    protected notify = inject(Notify);

    protected lookups = inject(Lookups);

    session = inject(Session);

    loading = signal(false);

    branches = signal<Option[]>([]);

    departments = signal<Option[]>([]);

    branchId: number | null = null;

    departmentId: number | null = null;

    time = time;

    label = label;

    severity = severity;

    minutes = minutes;



    protected async loadFilters() {

        try {

            this.branches.set(await this.lookups.get('branches'));

            this.departments.set(await this.lookups.get('departments'));

        } catch (e) {

            this.notify.fail(e);

        }

    }

}



/** Who is in today: one row per employee with the day's verdict, and HR's manual correction. */

@Component({

    standalone: true,

    imports: [...UI, PageHeader],

    template: `<app-page-header title="Daily Attendance" [subtitle]="summary()">

            <p-floatlabel variant="on"><input pInputText id="flt4" type="date" [(ngModel)]="day" (change)="load()" /><label for="flt4">Date</label></p-floatlabel>

            <p-floatlabel variant="on"><p-select inputId="flt6" [filter]="true" [options]="branches()" optionLabel="label" optionValue="value" [(ngModel)]="branchId" (onChange)="load()" [showClear]="true" /><label for="flt6">Branch</label></p-floatlabel>

            <p-floatlabel variant="on"><p-select inputId="flt7" [filter]="true" [options]="departments()" optionLabel="label" optionValue="value" [(ngModel)]="departmentId" (onChange)="load()" [showClear]="true" /><label for="flt7">Department</label></p-floatlabel>

        </app-page-header>

        <div class="card">

            <p-table #dtDaily [rowHover]="true" [showGridlines]="true" [globalFilterFields]="['code', 'fullName', 'department', 'branch', 'designation', 'status', 'note']" [value]="rows()" [loading]="loading()" stripedRows [paginator]="rows().length > 25" [rows]="25" [tableStyle]="{ 'min-width': '52rem' }">

                <ng-template #caption>

                    <div class="flex flex-wrap justify-between items-center gap-2">

                        <p-button label="Clear" icon="pi pi-filter-slash" [outlined]="true" (onClick)="dtDaily.clear(); dtDailyq.value = ''" />

                        <p-iconfield iconPosition="left" class="ml-auto">

                            <p-inputicon><i class="pi pi-search"></i></p-inputicon>

                            <input pInputText #dtDailyq type="text" (input)="dtDaily.filterGlobal(dtDailyq.value, 'contains')" placeholder="Search keyword" aria-label="Search keyword" />

                        </p-iconfield>

                    </div>

                </ng-template>

                <ng-template #header>

                    <tr><th pSortableColumn="code"><div class="flex justify-between items-center gap-2">Code<span class="flex items-center"><p-sortIcon field="code" /><p-columnFilter type="text" field="code" display="menu" /></span></div></th><th pSortableColumn="fullName"><div class="flex justify-between items-center gap-2">Employee<span class="flex items-center"><p-sortIcon field="fullName" /><p-columnFilter type="text" field="fullName" display="menu" /></span></div></th><th pSortableColumn="department"><div class="flex justify-between items-center gap-2">Department<span class="flex items-center"><p-sortIcon field="department" /><p-columnFilter type="text" field="department" display="menu" /></span></div></th><th pSortableColumn="branch"><div class="flex justify-between items-center gap-2">Branch<span class="flex items-center"><p-sortIcon field="branch" /><p-columnFilter type="text" field="branch" display="menu" /></span></div></th><th pSortableColumn="inTime"><div class="flex justify-between items-center gap-2">In<span class="flex items-center"><p-sortIcon field="inTime" /><p-columnFilter type="text" field="inTime" display="menu" /></span></div></th><th pSortableColumn="outTime"><div class="flex justify-between items-center gap-2">Out<span class="flex items-center"><p-sortIcon field="outTime" /><p-columnFilter type="text" field="outTime" display="menu" /></span></div></th><th pSortableColumn="workedMinutes"><div class="flex justify-between items-center gap-2">Worked<span class="flex items-center"><p-sortIcon field="workedMinutes" /><p-columnFilter type="numeric" field="workedMinutes" display="menu" /></span></div></th><th pSortableColumn="status"><div class="flex justify-between items-center gap-2">Status<span class="flex items-center"><p-sortIcon field="status" /><p-columnFilter type="text" field="status" display="menu" /></span></div></th><th style="width:4rem"></th></tr>

                </ng-template>

                <ng-template #body let-r>

                    <tr>

                        <td>{{ r.code }}</td>

                        <td><span class="font-medium">{{ r.fullName }}</span><div class="text-muted-color text-sm">{{ r.designation }}</div></td>

                        <td>{{ r.department || '—' }}</td>

                        <td>{{ r.branch || '—' }}</td>

                        <td>{{ time(r.inTime) }}</td>

                        <td>{{ time(r.outTime) }}</td>

                        <td>{{ minutes(r.workedMinutes) }}</td>

                        <td>

                            <p-tag [value]="label(r.status)" [severity]="severity(r.status)" />

                            @if (r.note) { <span class="text-muted-color text-sm ml-2">{{ r.note }}</span> }

                            @if (r.source === 'Manual') { <i class="pi pi-pencil text-muted-color ml-2" pTooltip="Entered by HR"></i> }

                        </td>

                        <td>

                            @if (session.can('attendance.logs.write')) {

                                <p-button icon="pi pi-pencil" [rounded]="true" [text]="true" (onClick)="edit(r)" pTooltip="Enter or correct" ariaLabel="Enter or correct attendance" />

                            }

                        </td>

                    </tr>

                </ng-template>

                <ng-template #emptymessage><tr><td colspan="9" class="text-center text-muted-color py-8">No employees match.</td></tr></ng-template>

            </p-table>

        </div>



        <p-dialog [(visible)]="dialog" [header]="'Attendance · ' + (editing?.fullName || '')" [modal]="true" [style]="{ width: '30rem' }" [breakpoints]="{ '640px': '95vw' }">

            <form class="grid grid-cols-12 gap-4 pt-2" (ngSubmit)="save()">

                <p class="col-span-12 text-muted-color m-0">A time entered here replaces the check-ins of that day until you remove it.</p>

                <div class="col-span-6"><p-floatlabel variant="on"><input pInputText id="inTime" name="inTime" type="time" [(ngModel)]="inTime" fluid /><label for="inTime">Check-in</label></p-floatlabel></div>

                <div class="col-span-6"><p-floatlabel variant="on"><input pInputText id="outTime" name="outTime" type="time" [(ngModel)]="outTime" fluid /><label for="outTime">Check-out</label></p-floatlabel></div>

                <div class="col-span-12"><p-floatlabel variant="on"><textarea pTextarea id="remarks" name="remarks" [(ngModel)]="remarks" rows="2" maxlength="500" fluid></textarea><label for="remarks">Remarks</label></p-floatlabel></div>

                <div class="col-span-12 flex justify-between gap-2">

                    <span>

                        @if (editing?.source === 'Manual') {

                            <p-button type="button" label="Remove manual entry" severity="danger" [text]="true" (onClick)="removeManual()" />

                        }

                    </span>

                    <span class="flex gap-2">

                        <p-button type="button" label="Cancel" severity="secondary" [text]="true" (onClick)="dialog = false" />

                        <p-button type="submit" label="Save" icon="pi pi-check" [loading]="saving()" />

                    </span>

                </div>

            </form>

        </p-dialog>`

})

export class DailyAttendancePage extends AttendanceReport {

    rows = signal<DailyRow[]>([]);

    saving = signal(false);

    day = isoDate();

    dialog = false;

    editing: DailyRow | null = null;

    inTime = '';

    outTime = '';

    remarks = '';



    summary = computed(() => {

        const rows = this.rows();

        const count = (...statuses: string[]) => rows.filter((r) => statuses.includes(r.status)).length;

        return rows.length ? `${date(this.day)} · ${count('Present', 'Late')} present (${count('Late')} late) · ${count('Absent')} absent · ${count('Leave')} on leave` : date(this.day);

    });



    constructor() {

        super();

        void this.loadFilters();

        void this.load();

    }



    async load() {

        this.loading.set(true);

        try {

            this.rows.set(await this.api.get<DailyRow[]>('/api/attendance/daily', { date: this.day, branchId: this.branchId, departmentId: this.departmentId }));

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.loading.set(false);

        }

    }



    edit(row: DailyRow) {

        this.editing = row;

        this.inTime = row.inTime?.slice(0, 5) ?? '';

        this.outTime = row.outTime?.slice(0, 5) ?? '';

        this.remarks = '';

        this.dialog = true;

    }



    async save() {

        if (!this.editing) return;

        this.saving.set(true);

        try {

            const result = await this.api.put('/api/attendance/logs', {

                employeeId: this.editing.employeeId,

                logDate: this.day,

                inTime: this.inTime ? this.inTime + ':00' : null,

                outTime: this.outTime ? this.outTime + ':00' : null,

                remarks: this.remarks || null

            });

            this.notify.ok(result.message);

            this.dialog = false;

            await this.load();

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.saving.set(false);

        }

    }



    async removeManual() {

        if (!this.editing) return;

        try {

            this.notify.ok((await this.api.delete('/api/attendance/logs', { employeeId: this.editing.employeeId, logDate: this.day })).message);

            this.dialog = false;

            await this.load();

        } catch (e) {

            this.notify.fail(e);

        }

    }

}



/** The month at a glance: days present, late, absent and on leave per employee. */

@Component({

    standalone: true,

    imports: [...UI, PageHeader],

    template: `<app-page-header title="Monthly Summary" subtitle="Attendance totals per employee for the chosen month.">

            <p-floatlabel variant="on"><input pInputText id="flt5" type="month" [(ngModel)]="month" (change)="load()" /><label for="flt5">Month</label></p-floatlabel>

            <p-floatlabel variant="on"><p-select inputId="flt8" [filter]="true" [options]="branches()" optionLabel="label" optionValue="value" [(ngModel)]="branchId" (onChange)="load()" [showClear]="true" /><label for="flt8">Branch</label></p-floatlabel>

            <p-floatlabel variant="on"><p-select inputId="flt9" [filter]="true" [options]="departments()" optionLabel="label" optionValue="value" [(ngModel)]="departmentId" (onChange)="load()" [showClear]="true" /><label for="flt9">Department</label></p-floatlabel>

            <p-button label="CSV" icon="pi pi-download" severity="secondary" [outlined]="true" (onClick)="exportCsv()" [disabled]="!rows().length" />

        </app-page-header>

        <div class="card">

            <p-table #dtMonthly [rowHover]="true" [showGridlines]="true" [globalFilterFields]="['code', 'fullName', 'department', 'designation']" [value]="rows()" [loading]="loading()" stripedRows [paginator]="rows().length > 25" [rows]="25" sortMode="single" [tableStyle]="{ 'min-width': '56rem' }">

                <ng-template #caption>

                    <div class="flex flex-wrap justify-between items-center gap-2">

                        <p-button label="Clear" icon="pi pi-filter-slash" [outlined]="true" (onClick)="dtMonthly.clear(); dtMonthlyq.value = ''" />

                        <p-iconfield iconPosition="left" class="ml-auto">

                            <p-inputicon><i class="pi pi-search"></i></p-inputicon>

                            <input pInputText #dtMonthlyq type="text" (input)="dtMonthly.filterGlobal(dtMonthlyq.value, 'contains')" placeholder="Search keyword" aria-label="Search keyword" />

                        </p-iconfield>

                    </div>

                </ng-template>

                <ng-template #header>

                    <tr>

                        <th pSortableColumn="code"><div class="flex justify-between items-center gap-2">Code<span class="flex items-center"><p-sortIcon field="code" /><p-columnFilter type="text" field="code" display="menu" /></span></div></th>

                        <th pSortableColumn="fullName"><div class="flex justify-between items-center gap-2">Employee<span class="flex items-center"><p-sortIcon field="fullName" /><p-columnFilter type="text" field="fullName" display="menu" /></span></div></th>

                        <th pSortableColumn="department"><div class="flex justify-between items-center gap-2">Department<span class="flex items-center"><p-sortIcon field="department" /><p-columnFilter type="text" field="department" display="menu" /></span></div></th>

                        <th pSortableColumn="presentDays" class="text-right"><div class="flex justify-between items-center gap-2">Present<span class="flex items-center"><p-sortIcon field="presentDays" /><p-columnFilter type="numeric" field="presentDays" display="menu" /></span></div></th>

                        <th pSortableColumn="lateDays" class="text-right"><div class="flex justify-between items-center gap-2">Late<span class="flex items-center"><p-sortIcon field="lateDays" /><p-columnFilter type="numeric" field="lateDays" display="menu" /></span></div></th>

                        <th pSortableColumn="absentDays" class="text-right"><div class="flex justify-between items-center gap-2">Absent<span class="flex items-center"><p-sortIcon field="absentDays" /><p-columnFilter type="numeric" field="absentDays" display="menu" /></span></div></th>

                        <th pSortableColumn="leaveDays" class="text-right"><div class="flex justify-between items-center gap-2">Leave<span class="flex items-center"><p-sortIcon field="leaveDays" /><p-columnFilter type="numeric" field="leaveDays" display="menu" /></span></div></th>

                        <th pSortableColumn="weekOffDays" class="text-right"><div class="flex justify-between items-center gap-2">Week off<span class="flex items-center"><p-sortIcon field="weekOffDays" /><p-columnFilter type="numeric" field="weekOffDays" display="menu" /></span></div></th>

                        <th pSortableColumn="holidayDays" class="text-right"><div class="flex justify-between items-center gap-2">Holiday<span class="flex items-center"><p-sortIcon field="holidayDays" /><p-columnFilter type="numeric" field="holidayDays" display="menu" /></span></div></th>

                        <th pSortableColumn="workedMinutes" class="text-right"><div class="flex justify-between items-center gap-2">Worked<span class="flex items-center"><p-sortIcon field="workedMinutes" /><p-columnFilter type="numeric" field="workedMinutes" display="menu" /></span></div></th>

                    </tr>

                </ng-template>

                <ng-template #body let-r>

                    <tr>

                        <td>{{ r.code }}</td>

                        <td><span class="font-medium">{{ r.fullName }}</span><div class="text-muted-color text-sm">{{ r.designation }}</div></td>

                        <td>{{ r.department || '—' }}</td>

                        <td class="text-right">{{ r.presentDays }}</td>

                        <td class="text-right" [class.text-amber-600]="r.lateDays > 0">{{ r.lateDays }}</td>

                        <td class="text-right" [class.text-red-500]="r.absentDays > 0">{{ r.absentDays }}</td>

                        <td class="text-right">{{ r.leaveDays }}</td>

                        <td class="text-right">{{ r.weekOffDays }}</td>

                        <td class="text-right">{{ r.holidayDays }}</td>

                        <td class="text-right">{{ minutes(r.workedMinutes) }}</td>

                    </tr>

                </ng-template>

                <ng-template #emptymessage><tr><td colspan="10" class="text-center text-muted-color py-8">No employees match.</td></tr></ng-template>

            </p-table>

        </div>`

})

export class MonthlyAttendancePage extends AttendanceReport {

    rows = signal<MonthlyRow[]>([]);

    month = isoDate().slice(0, 7);



    constructor() {

        super();

        void this.loadFilters();

        void this.load();

    }



    async load() {

        const [year, month] = this.month.split('-').map(Number);

        if (!year || !month) return;

        const last = new Date(year, month, 0).getDate();

        this.loading.set(true);

        try {

            this.rows.set(

                await this.api.get<MonthlyRow[]>('/api/attendance/monthly', {

                    from: `${this.month}-01`,

                    to: `${this.month}-${String(last).padStart(2, '0')}`,

                    branchId: this.branchId,

                    departmentId: this.departmentId

                })

            );

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.loading.set(false);

        }

    }



    exportCsv() {

        const head = ['Code', 'Employee', 'Department', 'Designation', 'Present', 'Late', 'Absent', 'Leave', 'Week off', 'Holiday', 'Worked minutes'];

        const lines = this.rows().map((r) => [r.code, r.fullName, r.department ?? '', r.designation ?? '', r.presentDays, r.lateDays, r.absentDays, r.leaveDays, r.weekOffDays, r.holidayDays, r.workedMinutes]);

        // Quote everything and neutralise cells a spreadsheet would run as a formula.

        const cell = (v: unknown) => `"${String(v).replace(/^([=+\-@])/, "'$1").replace(/"/g, '""')}"`;

        const blob = new Blob(['\uFEFF' + [head, ...lines].map((l) => l.map(cell).join(',')).join('\r\n')], { type: 'text/csv;charset=utf-8' });

        const link = document.createElement('a');

        link.href = URL.createObjectURL(blob);

        link.download = `attendance-${this.month}.csv`;

        link.click();

        setTimeout(() => URL.revokeObjectURL(link.href), 60000);

    }

}
