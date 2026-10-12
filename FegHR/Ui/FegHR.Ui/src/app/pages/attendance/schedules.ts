import { Component, computed, inject, signal } from '@angular/core';

import { Api, Session } from '@/app/core/api';

import { Notify, PageHeader, UI, date, isoDate, time } from '@/app/core/ui';

import { Lookups, Option } from '../crud';



interface ScheduleDay {

    dayOfWeek: number;

    isWorkingDay: boolean;

    startTime: string;

    endTime: string;

    graceMinutes: number;

}

interface Schedule {

    id: number;

    name: string;

    description: string | null;

    isDefault: boolean;

    isActive: boolean;

    days: ScheduleDay[];

    rowVersion: string;

}

interface Assignment {

    id: number;

    employeeId: number;

    employeeName: string;

    workScheduleId: number;

    scheduleName: string;

    effectiveFrom: string;

    effectiveTo: string | null;

    remarks: string | null;

}



const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];



/** Shifts: working days, hours and grace minutes, and which employees follow a shift other than the default. */

@Component({

    standalone: true,

    imports: [...UI, PageHeader],

    template: `<app-page-header title="Work Schedules" subtitle="A check-in after the start time plus the grace minutes counts as late. Employees follow the default schedule unless one is assigned to them.">

            @if (canWrite()) {

                <p-button label="Assign to employee" icon="pi pi-user-plus" severity="secondary" [outlined]="true" (onClick)="openAssign()" />

                <p-button label="New schedule" icon="pi pi-plus" (onClick)="open(null)" />

            }

        </app-page-header>



        <div class="grid grid-cols-12 gap-6">

            @for (s of schedules(); track s.id) {

                <div class="col-span-12 lg:col-span-6">

                    <div class="card !mb-0 h-full">

                        <div class="flex items-start justify-between gap-3 mb-4">

                            <div>

                                <h2 class="text-xl font-semibold m-0">{{ s.name }}

                                    @if (s.isDefault) { <p-tag value="Default" severity="success" styleClass="ml-2" /> }

                                    @if (!s.isActive) { <p-tag value="Inactive" severity="secondary" styleClass="ml-2" /> }

                                </h2>

                                <span class="text-muted-color">{{ s.description }}</span>

                            </div>

                            @if (canWrite()) {

                                <p-button icon="pi pi-pencil" [rounded]="true" [text]="true" (onClick)="open(s)" ariaLabel="Edit schedule" />

                            }

                        </div>

                        <ul class="list-none p-0 m-0">

                            @for (d of s.days; track d.dayOfWeek) {

                                <li class="flex justify-between py-2 border-b border-surface last:border-0">

                                    <span [class.text-muted-color]="!d.isWorkingDay">{{ dayName(d.dayOfWeek) }}</span>

                                    <span [class.text-muted-color]="!d.isWorkingDay">{{ d.isWorkingDay ? time(d.startTime) + ' – ' + time(d.endTime) + ' · ' + d.graceMinutes + ' min grace' : 'Week off' }}</span>

                                </li>

                            }

                        </ul>

                    </div>

                </div>

            }

        </div>



        <div class="card mt-6">

            <h2 class="text-xl font-semibold mt-0 mb-4">Employees on their own schedule</h2>

            <p-table #dtAssign [rowHover]="true" [showGridlines]="true" [globalFilterFields]="['employeeName', 'scheduleName', 'remarks']" [value]="assignments()" stripedRows [tableStyle]="{ 'min-width': '40rem' }">

                <ng-template #caption>

                    <div class="flex flex-wrap justify-between items-center gap-2">

                        <p-button label="Clear" icon="pi pi-filter-slash" [outlined]="true" (onClick)="dtAssign.clear(); dtAssignq.value = ''" />

                        <p-iconfield iconPosition="left" class="ml-auto">

                            <p-inputicon><i class="pi pi-search"></i></p-inputicon>

                            <input pInputText #dtAssignq type="text" (input)="dtAssign.filterGlobal(dtAssignq.value, 'contains')" placeholder="Search keyword" aria-label="Search keyword" />

                        </p-iconfield>

                    </div>

                </ng-template>

                <ng-template #header><tr><th pSortableColumn="employeeName"><div class="flex justify-between items-center gap-2">Employee<span class="flex items-center"><p-sortIcon field="employeeName" /><p-columnFilter type="text" field="employeeName" display="menu" /></span></div></th><th pSortableColumn="scheduleName"><div class="flex justify-between items-center gap-2">Schedule<span class="flex items-center"><p-sortIcon field="scheduleName" /><p-columnFilter type="text" field="scheduleName" display="menu" /></span></div></th><th pSortableColumn="effectiveFrom"><div class="flex justify-between items-center gap-2">From<span class="flex items-center"><p-sortIcon field="effectiveFrom" /><p-columnFilter type="text" field="effectiveFrom" display="menu" /></span></div></th><th pSortableColumn="effectiveTo"><div class="flex justify-between items-center gap-2">To<span class="flex items-center"><p-sortIcon field="effectiveTo" /><p-columnFilter type="text" field="effectiveTo" display="menu" /></span></div></th><th pSortableColumn="remarks"><div class="flex justify-between items-center gap-2">Remarks<span class="flex items-center"><p-sortIcon field="remarks" /><p-columnFilter type="text" field="remarks" display="menu" /></span></div></th><th style="width:4rem"></th></tr></ng-template>

                <ng-template #body let-a>

                    <tr>

                        <td>{{ a.employeeName }}</td><td>{{ a.scheduleName }}</td><td>{{ date(a.effectiveFrom) }}</td><td>{{ a.effectiveTo ? date(a.effectiveTo) : 'Ongoing' }}</td><td>{{ a.remarks || '' }}</td>

                        <td>

                            @if (canWrite()) {

                                <p-button icon="pi pi-trash" severity="danger" [rounded]="true" [text]="true" (onClick)="removeAssignment(a)" ariaLabel="Remove assignment" />

                            }

                        </td>

                    </tr>

                </ng-template>

                <ng-template #emptymessage><tr><td colspan="6" class="text-center text-muted-color py-6">Everyone follows the default schedule.</td></tr></ng-template>

            </p-table>

        </div>



        <p-dialog [(visible)]="dialog" [header]="editing ? 'Edit schedule' : 'New schedule'" [modal]="true" [style]="{ width: '46rem' }" [breakpoints]="{ '960px': '95vw' }">

            <form class="flex flex-col gap-4 pt-2" (ngSubmit)="save()" #form="ngForm">

                <div class="grid grid-cols-12 gap-4">

                    <div class="col-span-12 md:col-span-6"><p-floatlabel variant="on"><input pInputText id="name" name="name" [(ngModel)]="model.name" required maxlength="150" fluid /><label for="name">Name *</label></p-floatlabel></div>

                    <div class="col-span-12 md:col-span-6"><p-floatlabel variant="on"><input pInputText id="description" name="description" [(ngModel)]="model.description" maxlength="500" fluid /><label for="description">Description</label></p-floatlabel></div>

                </div>

                <div class="overflow-x-auto">

                    <table class="w-full" style="min-width:34rem">

                        <thead><tr class="text-left text-muted-color"><th class="py-2">Day</th><th>Working</th><th>Start</th><th>End</th><th>Grace (min)</th></tr></thead>

                        <tbody>

                            @for (d of model.days; track d.dayOfWeek) {

                                <tr>

                                    <td class="py-1 font-medium">{{ dayName(d.dayOfWeek) }}</td>

                                    <td><p-checkbox [binary]="true" [(ngModel)]="d.isWorkingDay" [name]="'w' + d.dayOfWeek" [ariaLabel]="dayName(d.dayOfWeek) + ' is a working day'" /></td>

                                    <td><input pInputText type="time" [(ngModel)]="d.startTime" [name]="'s' + d.dayOfWeek" [disabled]="!d.isWorkingDay" required /></td>

                                    <td><input pInputText type="time" [(ngModel)]="d.endTime" [name]="'e' + d.dayOfWeek" [disabled]="!d.isWorkingDay" required /></td>

                                    <td><input pInputText type="number" min="0" max="480" [(ngModel)]="d.graceMinutes" [name]="'g' + d.dayOfWeek" [disabled]="!d.isWorkingDay" style="width:6rem" /></td>

                                </tr>

                            }

                        </tbody>

                    </table>

                </div>

                <div class="flex flex-wrap gap-6">

                    <div class="flex items-center gap-2"><p-checkbox inputId="isDefault" [binary]="true" [(ngModel)]="model.isDefault" name="isDefault" /><label for="isDefault">Default schedule of the company</label></div>

                    <div class="flex items-center gap-2"><p-checkbox inputId="isActive" [binary]="true" [(ngModel)]="model.isActive" name="isActive" /><label for="isActive">Active</label></div>

                </div>

                <div class="flex justify-end gap-2">

                    <p-button type="button" label="Cancel" severity="secondary" [text]="true" (onClick)="dialog = false" />

                    <p-button type="submit" label="Save" icon="pi pi-check" [loading]="saving()" [disabled]="form.invalid || saving()" />

                </div>

            </form>

        </p-dialog>



        <p-dialog [(visible)]="assignDialog" header="Assign a schedule" [modal]="true" [style]="{ width: '32rem' }" [breakpoints]="{ '640px': '95vw' }">

            <form class="flex flex-col gap-4 pt-2" (ngSubmit)="assign()" #assignForm="ngForm">

                <div><p-floatlabel variant="on"><p-select [filter]="true" inputId="employee" name="employee" [options]="employees()" optionLabel="label" optionValue="value" [(ngModel)]="a.employeeId" required appendTo="body" fluid /><label for="employee">Employee *</label></p-floatlabel></div>

                <div><p-floatlabel variant="on"><p-select [filter]="true" inputId="schedule" name="schedule" [options]="scheduleOptions()" optionLabel="label" optionValue="value" [(ngModel)]="a.workScheduleId" required appendTo="body" fluid /><label for="schedule">Schedule *</label></p-floatlabel></div>

                <div><p-floatlabel variant="on"><input pInputText id="from" name="from" type="date" [(ngModel)]="a.effectiveFrom" required fluid /><label for="from">Effective from *</label></p-floatlabel></div>

                <div><p-floatlabel variant="on"><input pInputText id="aremarks" name="aremarks" [(ngModel)]="a.remarks" maxlength="500" fluid /><label for="aremarks">Remarks</label></p-floatlabel></div>

                <div class="flex justify-end gap-2">

                    <p-button type="button" label="Cancel" severity="secondary" [text]="true" (onClick)="assignDialog = false" />

                    <p-button type="submit" label="Assign" icon="pi pi-check" [loading]="saving()" [disabled]="assignForm.invalid || saving()" />

                </div>

            </form>

        </p-dialog>`

})

export class SchedulesPage {

    private api = inject(Api);

    private notify = inject(Notify);

    private session = inject(Session);

    private lookups = inject(Lookups);

    schedules = signal<Schedule[]>([]);

    assignments = signal<Assignment[]>([]);

    employees = signal<Option[]>([]);

    saving = signal(false);

    canWrite = computed(() => this.session.can('attendance.setup.write'));

    scheduleOptions = computed(() => this.schedules().filter((s) => s.isActive).map((s) => ({ label: s.name, value: s.id })));

    dialog = false;

    assignDialog = false;

    editing: Schedule | null = null;

    model = this.blank();

    a = { employeeId: null as number | null, workScheduleId: null as number | null, effectiveFrom: isoDate(), remarks: '' };

    time = time;

    date = date;



    constructor() {

        void this.load();

    }



    dayName(day: number) {

        return DAYS[day];

    }



    async load() {

        try {

            const [schedules, assignments] = await Promise.all([this.api.get<Schedule[]>('/api/attendance/schedules'), this.api.get<Assignment[]>('/api/attendance/employee-schedules')]);

            this.schedules.set(schedules);

            this.assignments.set(assignments);

        } catch (e) {

            this.notify.fail(e);

        }

    }



    open(schedule: Schedule | null) {

        this.editing = schedule;

        this.model = schedule

            ? { name: schedule.name, description: schedule.description ?? '', isDefault: schedule.isDefault, isActive: schedule.isActive, days: schedule.days.map((d) => ({ ...d, startTime: d.startTime.slice(0, 5), endTime: d.endTime.slice(0, 5) })) }

            : this.blank();

        this.dialog = true;

    }



    async save() {

        const body = {

            name: this.model.name,

            description: this.model.description || null,

            isDefault: this.model.isDefault,

            isActive: this.model.isActive,

            rowVersion: this.editing?.rowVersion ?? null,

            days: this.model.days.map((d) => ({ ...d, graceMinutes: Number(d.graceMinutes) || 0, startTime: d.startTime + ':00', endTime: d.endTime + ':00' }))

        };

        this.saving.set(true);

        try {

            const result = this.editing ? await this.api.put(`/api/attendance/schedules/${this.editing.id}`, body) : await this.api.post('/api/attendance/schedules', body);

            this.notify.ok(result.message);

            this.dialog = false;

            await this.load();

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.saving.set(false);

        }

    }



    async openAssign() {

        this.a = { employeeId: null, workScheduleId: null, effectiveFrom: isoDate(), remarks: '' };

        this.assignDialog = true;

        try {

            this.employees.set(await this.lookups.get('employees'));

        } catch (e) {

            this.notify.fail(e);

        }

    }



    async assign() {

        this.saving.set(true);

        try {

            this.notify.ok((await this.api.post('/api/attendance/employee-schedules', { ...this.a, remarks: this.a.remarks || null })).message);

            this.assignDialog = false;

            await this.load();

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.saving.set(false);

        }

    }



    async removeAssignment(row: Assignment) {

        try {

            this.notify.ok((await this.api.delete(`/api/attendance/employee-schedules/${row.id}`)).message);

            await this.load();

        } catch (e) {

            this.notify.fail(e);

        }

    }



    private blank() {

        return {

            name: '',

            description: '',

            isDefault: false,

            isActive: true,

            // Sunday to Thursday, the usual working week in Bangladesh.

            days: DAYS.map((_, dayOfWeek) => ({ dayOfWeek, isWorkingDay: dayOfWeek < 5, startTime: '09:00', endTime: '18:00', graceMinutes: 15 }))

        };

    }

}
