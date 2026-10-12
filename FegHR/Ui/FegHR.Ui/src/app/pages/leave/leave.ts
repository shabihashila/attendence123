import { Component, computed, inject, signal } from '@angular/core';

import { Api, Paged, Session } from '@/app/core/api';

import { Notify, PageHeader, UI, date, dateTime, isoDate, label, severity } from '@/app/core/ui';

import { Lookups, Option } from '../crud';



interface Balance {

    leaveTypeId: number;

    code: string;

    name: string;

    isPaid: boolean;

    quota: number | null;

    adjustment: number;

    used: number;

    pending: number;

    remaining: number | null;

}

interface Application {

    id: number;

    employeeId: number;

    employeeCode: string;

    employeeName: string;

    leaveTypeId: number;

    leaveType: string;

    dayType: string;

    startDate: string;

    endDate: string;

    days: number;

    appliedOn: string;

    reason: string | null;

    addressDuringLeave: string | null;

    status: string;

    currentStep: string;

    level1At: string | null;

    level1Remarks: string | null;

    decidedAt: string | null;

    decisionRemarks: string | null;

    canDecide: boolean;

}

interface StatementRow {

    employeeId: number;

    code: string;

    fullName: string;

    department: string | null;

    leaveType: string;

    quota: number | null;

    adjustment: number;

    used: number;

    pending: number;

    remaining: number | null;

}



const DAY_TYPES = [

    { label: 'Full day(s)', value: 'Full' },

    { label: 'First half', value: 'HalfFirst' },

    { label: 'Second half', value: 'HalfSecond' }

];



function blankApplication() {

    return { employeeId: null as number | null, leaveTypeId: null as number | null, dayType: 'Full', startDate: isoDate(), endDate: isoDate(), reason: '', addressDuringLeave: '' };

}



/** Where a pending application is waiting. */

function stepLabel(a: Application): string {

    if (a.status !== 'Pending') return label(a.status);

    return a.currentStep === 'Manager' ? 'With line manager' : 'With HR';

}



const APPLY_FORM = `<form class="grid grid-cols-12 gap-4 pt-2" (ngSubmit)="apply()" #form="ngForm">

    @if (onBehalf) {

        <div class="col-span-12"><p-floatlabel variant="on"><p-select [filter]="true" inputId="employee" name="employee" [options]="employees()" optionLabel="label" optionValue="value" [(ngModel)]="a.employeeId" required appendTo="body" fluid /><label for="employee">Employee *</label></p-floatlabel></div>

    }

    <div class="col-span-12 md:col-span-6"><p-floatlabel variant="on"><p-select [filter]="true" inputId="type" name="type" [options]="types()" optionLabel="label" optionValue="value" [(ngModel)]="a.leaveTypeId" required appendTo="body" fluid /><label for="type">Leave type *</label></p-floatlabel></div>

    <div class="col-span-12 md:col-span-6"><p-floatlabel variant="on"><p-select [filter]="true" inputId="dayType" name="dayType" [options]="dayTypes" optionLabel="label" optionValue="value" [(ngModel)]="a.dayType" (onChange)="syncDates()" appendTo="body" fluid /><label for="dayType">Duration *</label></p-floatlabel></div>

    <div class="col-span-12 md:col-span-6"><p-floatlabel variant="on"><input pInputText id="start" name="start" type="date" [(ngModel)]="a.startDate" (change)="syncDates()" required fluid /><label for="start">From *</label></p-floatlabel></div>

    <div class="col-span-12 md:col-span-6"><p-floatlabel variant="on"><input pInputText id="end" name="end" type="date" [(ngModel)]="a.endDate" [min]="a.startDate" [disabled]="a.dayType !== 'Full'" required fluid /><label for="end">To *</label></p-floatlabel></div>

    <div class="col-span-12"><p-floatlabel variant="on"><textarea pTextarea id="reason" name="reason" [(ngModel)]="a.reason" rows="2" maxlength="1000" fluid></textarea><label for="reason">Reason</label></p-floatlabel></div>

    <div class="col-span-12"><p-floatlabel variant="on"><input pInputText id="address" name="address" [(ngModel)]="a.addressDuringLeave" maxlength="500" fluid /><label for="address">Address during leave</label></p-floatlabel></div>

    <div class="col-span-12 flex justify-end gap-2">

        <p-button type="button" label="Cancel" severity="secondary" [text]="true" (onClick)="dialog = false" />

        <p-button type="submit" label="Apply" icon="pi pi-send" [loading]="saving()" [disabled]="form.invalid || saving()" />

    </div>

</form>`;



/** The employee's own leave: balances, a new application and the history. */

@Component({

    standalone: true,

    imports: [...UI, PageHeader],

    template: `<app-page-header title="My Leave" subtitle="Your balances for the year and your applications.">

            <p-floatlabel variant="on"><input pInputText id="flt12" type="number" [(ngModel)]="year" (change)="load()" min="2000" max="2200" style="width:7rem" /><label for="flt12">Year</label></p-floatlabel>

            <p-button label="Apply for leave" icon="pi pi-plus" (onClick)="open()" [disabled]="!!notLinked()" />

        </app-page-header>

        @if (notLinked()) {

            <p-message severity="warn" styleClass="w-full" [text]="notLinked()" />

        }

        <div class="grid grid-cols-12 gap-6 mb-6">

            @for (b of balances(); track b.leaveTypeId) {

                <div class="col-span-12 sm:col-span-6 xl:col-span-3">

                    <div class="card !mb-0 h-full">

                        <div class="flex items-center justify-between mb-3"><span class="font-semibold">{{ b.name }}</span>@if (!b.isPaid) { <p-tag value="Unpaid" severity="secondary" /> }</div>

                        <div class="text-3xl font-semibold text-surface-900 dark:text-surface-0">{{ b.remaining ?? '∞' }} <span class="text-base font-normal text-muted-color">day(s) left</span></div>

                        <div class="text-muted-color text-sm mt-2">

                            @if (b.quota != null) { {{ b.quota }} a year }

                            @if (b.adjustment) { · {{ b.adjustment > 0 ? '+' : '' }}{{ b.adjustment }} adjusted }

                            · {{ b.used }} used @if (b.pending) { · {{ b.pending }} pending }

                        </div>

                    </div>

                </div>

            }

        </div>

        <div class="card">

            <p-table #dtMine [rowHover]="true" [showGridlines]="true" [globalFilterFields]="['leaveType', 'status', 'reason', 'startDate']" [value]="applications()" stripedRows [paginator]="applications().length > 15" [rows]="15" [tableStyle]="{ 'min-width': '46rem' }">

                <ng-template #caption>

                    <div class="flex flex-wrap justify-between items-center gap-2">

                        <p-button label="Clear" icon="pi pi-filter-slash" [outlined]="true" (onClick)="dtMine.clear(); dtMineq.value = ''" />

                        <p-iconfield iconPosition="left" class="ml-auto">

                            <p-inputicon><i class="pi pi-search"></i></p-inputicon>

                            <input pInputText #dtMineq type="text" (input)="dtMine.filterGlobal(dtMineq.value, 'contains')" placeholder="Search keyword" aria-label="Search keyword" />

                        </p-iconfield>

                    </div>

                </ng-template>

                <ng-template #header><tr><th pSortableColumn="leaveType"><div class="flex justify-between items-center gap-2">Type<span class="flex items-center"><p-sortIcon field="leaveType" /><p-columnFilter type="text" field="leaveType" display="menu" /></span></div></th><th pSortableColumn="startDate"><div class="flex justify-between items-center gap-2">Dates<span class="flex items-center"><p-sortIcon field="startDate" /><p-columnFilter type="text" field="startDate" display="menu" /></span></div></th><th pSortableColumn="days" class="text-right"><div class="flex justify-between items-center gap-2">Days<span class="flex items-center"><p-sortIcon field="days" /><p-columnFilter type="numeric" field="days" display="menu" /></span></div></th><th pSortableColumn="appliedOn"><div class="flex justify-between items-center gap-2">Applied<span class="flex items-center"><p-sortIcon field="appliedOn" /><p-columnFilter type="text" field="appliedOn" display="menu" /></span></div></th><th pSortableColumn="status"><div class="flex justify-between items-center gap-2">Status<span class="flex items-center"><p-sortIcon field="status" /><p-columnFilter type="text" field="status" display="menu" /></span></div></th><th>Remarks</th><th style="width:6rem"></th></tr></ng-template>

                <ng-template #body let-x>

                    <tr>

                        <td>{{ x.leaveType }}<div class="text-muted-color text-sm">{{ label(x.dayType) }}</div></td>

                        <td>{{ date(x.startDate) }}@if (x.endDate !== x.startDate) { – {{ date(x.endDate) }} }</td>

                        <td class="text-right">{{ x.days }}</td>

                        <td>{{ date(x.appliedOn) }}</td>

                        <td><p-tag [value]="step(x)" [severity]="severity(x.status)" /></td>

                        <td>{{ x.decisionRemarks || x.level1Remarks || x.reason || '' }}</td>

                        <td class="text-right">@if (x.status === 'Pending') { <p-button label="Cancel" size="small" severity="secondary" [text]="true" (onClick)="cancel(x)" /> }</td>

                    </tr>

                </ng-template>

                <ng-template #emptymessage><tr><td colspan="7" class="text-center text-muted-color py-8">No leave applications this year.</td></tr></ng-template>

            </p-table>

        </div>

        <p-dialog [(visible)]="dialog" header="Apply for leave" [modal]="true" [style]="{ width: '40rem' }" [breakpoints]="{ '720px': '95vw' }">${APPLY_FORM}</p-dialog>`

})

export class MyLeavePage {

    private api = inject(Api);

    private notify = inject(Notify);

    private lookups = inject(Lookups);

    balances = signal<Balance[]>([]);

    applications = signal<Application[]>([]);

    types = signal<Option[]>([]);

    employees = signal<Option[]>([]);

    notLinked = signal('');

    saving = signal(false);

    year = new Date().getFullYear();

    dialog = false;

    onBehalf = false;

    a = blankApplication();

    dayTypes = DAY_TYPES;

    date = date;

    label = label;

    severity = severity;

    step = stepLabel;



    constructor() {

        void this.load();

    }



    async load() {

        try {

            const [balances, applications] = await Promise.all([this.api.get<Balance[]>('/api/leave/my/balance', { year: this.year }), this.api.get<Application[]>('/api/leave/my/applications', { year: this.year })]);

            this.balances.set(balances);

            this.applications.set(applications);

            this.notLinked.set('');

        } catch (e) {

            if ((e as { status?: number }).status === 409) this.notLinked.set((e as Error).message);

            else this.notify.fail(e);

        }

    }



    async open() {

        this.a = blankApplication();

        this.dialog = true;

        try {

            this.types.set(await this.lookups.get('leaveTypes'));

        } catch (e) {

            this.notify.fail(e);

        }

    }



    syncDates() {

        if (this.a.dayType !== 'Full' || this.a.endDate < this.a.startDate) this.a.endDate = this.a.startDate;

    }



    async apply() {

        this.saving.set(true);

        try {

            const result = await this.api.post('/api/leave/my/applications', { ...this.a, reason: this.a.reason || null, addressDuringLeave: this.a.addressDuringLeave || null });

            this.notify.ok(result.message);

            this.dialog = false;

            await this.load();

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.saving.set(false);

        }

    }



    async cancel(application: Application) {

        try {

            this.notify.ok((await this.api.post(`/api/leave/my/applications/${application.id}/cancel`)).message);

            await this.load();

        } catch (e) {

            this.notify.fail(e);

        }

    }

}



/** Approvals: a line manager decides the first step for their reports, HR decides finally. */

@Component({

    standalone: true,

    imports: [...UI, PageHeader],

    template: `<app-page-header title="Leave Applications" subtitle="A line manager approves first; HR gives the final approval. An application with no line manager goes straight to HR.">

            <p-floatlabel variant="on"><p-select inputId="fltStatus" [options]="statuses" optionLabel="label" optionValue="value" [(ngModel)]="status" (onChange)="reload()" /><label for="fltStatus">Status</label></p-floatlabel>

            @if (session.can('leave.approve.l2')) {

                <p-button label="Apply on behalf" icon="pi pi-plus" severity="secondary" [outlined]="true" (onClick)="open()" />

            }

        </app-page-header>

        <div class="card">

            <p-table [value]="rows()" [loading]="loading()" [lazy]="true" [paginator]="true" [rows]="pageSize" [totalRecords]="total()" (onLazyLoad)="onLazy($event)" stripedRows [tableStyle]="{ 'min-width': '56rem' }">

                <ng-template #header><tr><th>Employee</th><th>Type</th><th>Dates</th><th class="text-right">Days</th><th>Reason</th><th>Status</th><th style="width:13rem"></th></tr></ng-template>

                <ng-template #body let-x>

                    <tr>

                        <td><span class="font-medium">{{ x.employeeName }}</span><div class="text-muted-color text-sm">{{ x.employeeCode }} · applied {{ date(x.appliedOn) }}</div></td>

                        <td>{{ x.leaveType }}<div class="text-muted-color text-sm">{{ label(x.dayType) }}</div></td>

                        <td>{{ date(x.startDate) }}@if (x.endDate !== x.startDate) { – {{ date(x.endDate) }} }</td>

                        <td class="text-right">{{ x.days }}</td>

                        <td>{{ x.reason || '' }}</td>

                        <td>

                            <p-tag [value]="step(x)" [severity]="severity(x.status)" />

                            @if (x.decidedAt) { <div class="text-muted-color text-sm mt-1">{{ dateTime(x.decidedAt) }}</div> }

                        </td>

                        <td class="text-right whitespace-nowrap">

                            @if (x.canDecide) {

                                <p-button label="Approve" icon="pi pi-check" size="small" severity="success" (onClick)="decide(x, true)" />

                                <p-button label="Reject" icon="pi pi-times" size="small" severity="danger" [outlined]="true" styleClass="ml-2" (onClick)="decide(x, false)" />

                            }

                        </td>

                    </tr>

                </ng-template>

                <ng-template #emptymessage><tr><td colspan="7" class="text-center text-muted-color py-8">No applications.</td></tr></ng-template>

            </p-table>

        </div>



        <p-dialog [(visible)]="decisionDialog" [header]="(approving ? 'Approve' : 'Reject') + ' leave · ' + (current?.employeeName || '')" [modal]="true" [style]="{ width: '34rem' }" [breakpoints]="{ '640px': '95vw' }">

            @if (current; as c) {

                <div class="flex flex-col gap-4 pt-2">

                    <p class="m-0">{{ c.leaveType }} · {{ date(c.startDate) }}@if (c.endDate !== c.startDate) { – {{ date(c.endDate) }} } · {{ c.days }} day(s)</p>

                    @if (balance().length) {

                        <p class="text-muted-color m-0">Balance: @for (b of balance(); track b.leaveTypeId) { <span class="mr-3">{{ b.code }} {{ b.remaining ?? '∞' }}</span> }</p>

                    }

                    <div><p-floatlabel variant="on"><textarea pTextarea id="remarks" [(ngModel)]="remarks" rows="2" maxlength="500" fluid></textarea><label for="remarks">Remarks</label></p-floatlabel></div>

                    <div class="flex justify-end gap-2">

                        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="decisionDialog = false" />

                        <p-button [label]="approving ? 'Approve' : 'Reject'" [severity]="approving ? 'success' : 'danger'" [loading]="saving()" (onClick)="confirmDecision()" />

                    </div>

                </div>

            }

        </p-dialog>

        <p-dialog [(visible)]="dialog" header="Apply for leave on behalf" [modal]="true" [style]="{ width: '40rem' }" [breakpoints]="{ '720px': '95vw' }">${APPLY_FORM}</p-dialog>`

})

export class LeaveApplicationsPage {

    private api = inject(Api);

    private notify = inject(Notify);

    private lookups = inject(Lookups);

    session = inject(Session);

    rows = signal<Application[]>([]);

    total = signal(0);

    loading = signal(false);

    saving = signal(false);

    balance = signal<Balance[]>([]);

    types = signal<Option[]>([]);

    employees = signal<Option[]>([]);

    status = 'Pending';

    page = 1;

    pageSize = 20;

    decisionDialog = false;

    dialog = false;

    onBehalf = true;

    approving = true;

    current: Application | null = null;

    remarks = '';

    a = blankApplication();

    dayTypes = DAY_TYPES;

    statuses = [

        { label: 'Pending', value: 'Pending' },

        { label: 'Approved', value: 'Approved' },

        { label: 'Rejected', value: 'Rejected' },

        { label: 'Cancelled', value: 'Cancelled' },

        { label: 'All', value: '' }

    ];

    date = date;

    dateTime = dateTime;

    label = label;

    severity = severity;

    step = stepLabel;



    onLazy(event: { first?: number | null; rows?: number | null }) {

        this.pageSize = event.rows ?? this.pageSize;

        this.page = Math.floor((event.first ?? 0) / this.pageSize) + 1;

        void this.load();

    }



    reload() {

        this.page = 1;

        return this.load();

    }



    async load() {

        this.loading.set(true);

        try {

            const result = await this.api.get<Paged<Application>>('/api/leave/applications', { status: this.status, page: this.page, pageSize: this.pageSize });

            this.rows.set(result.items);

            this.total.set(result.totalCount);

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.loading.set(false);

        }

    }



    async decide(application: Application, approve: boolean) {

        this.current = application;

        this.approving = approve;

        this.remarks = '';

        this.balance.set([]);

        this.decisionDialog = true;

        // The balance helps the approver; the server checks it again whatever is shown here.

        this.balance.set(await this.api.get<Balance[]>(`/api/leave/balance/${application.employeeId}`, { year: Number(application.startDate.slice(0, 4)) }).catch(() => []));

    }



    async confirmDecision() {

        if (!this.current) return;

        this.saving.set(true);

        try {

            const result = await this.api.post(`/api/leave/applications/${this.current.id}/decision`, { approve: this.approving, remarks: this.remarks || null });

            this.notify.ok(result.message);

            this.decisionDialog = false;

            await this.load();

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.saving.set(false);

        }

    }



    async open() {

        this.a = blankApplication();

        this.dialog = true;

        try {

            this.types.set(await this.lookups.get('leaveTypes'));

            this.employees.set(await this.lookups.get('employees'));

        } catch (e) {

            this.notify.fail(e);

        }

    }



    syncDates() {

        if (this.a.dayType !== 'Full' || this.a.endDate < this.a.startDate) this.a.endDate = this.a.startDate;

    }



    async apply() {

        this.saving.set(true);

        try {

            const result = await this.api.post('/api/leave/applications', { ...this.a, reason: this.a.reason || null, addressDuringLeave: this.a.addressDuringLeave || null });

            this.notify.ok(result.message);

            this.dialog = false;

            await this.reload();

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.saving.set(false);

        }

    }

}



/** Every employee's leave balance for a year, one line per leave type. */

@Component({

    standalone: true,

    imports: [...UI, PageHeader],

    template: `<app-page-header title="Leave Statement" subtitle="Quota, adjustments, days used and days left for each employee and leave type.">

            <p-floatlabel variant="on"><input pInputText id="flt13" type="number" [(ngModel)]="year" (change)="load()" min="2000" max="2200" style="width:7rem" /><label for="flt13">Year</label></p-floatlabel>

            <p-floatlabel variant="on"><p-select inputId="flt14" [filter]="true" [options]="departments()" optionLabel="label" optionValue="value" [(ngModel)]="departmentId" (onChange)="load()" [showClear]="true" /><label for="flt14">Department</label></p-floatlabel>

        </app-page-header>

        <div class="card">

            <p-table #dtStatement [showGridlines]="true" [globalFilterFields]="['code', 'fullName', 'department', 'leaveType']" [value]="rows()" [loading]="loading()" rowGroupMode="rowspan" groupRowsBy="code" sortField="code" sortMode="single" [tableStyle]="{ 'min-width': '50rem' }">

                <ng-template #caption>

                    <div class="flex flex-wrap justify-between items-center gap-2">

                        <p-button label="Clear" icon="pi pi-filter-slash" [outlined]="true" (onClick)="dtStatement.clear(); dtStatementq.value = ''" />

                        <p-iconfield iconPosition="left" class="ml-auto">

                            <p-inputicon><i class="pi pi-search"></i></p-inputicon>

                            <input pInputText #dtStatementq type="text" (input)="dtStatement.filterGlobal(dtStatementq.value, 'contains')" placeholder="Search keyword" aria-label="Search keyword" />

                        </p-iconfield>

                    </div>

                </ng-template>

                <ng-template #header><tr><th>Employee</th><th>Leave type</th><th class="text-right">Quota</th><th class="text-right">Adjusted</th><th class="text-right">Used</th><th class="text-right">Pending</th><th class="text-right">Remaining</th></tr></ng-template>

                <ng-template #body let-r let-rowgroup="rowgroup" let-rowspan="rowspan">

                    <tr>

                        @if (rowgroup) {

                            <td [attr.rowspan]="rowspan" class="align-top"><span class="font-medium">{{ r.fullName }}</span><div class="text-muted-color text-sm">{{ r.code }} · {{ r.department || '—' }}</div></td>

                        }

                        <td>{{ r.leaveType }}</td>

                        <td class="text-right">{{ r.quota ?? '—' }}</td>

                        <td class="text-right">{{ r.adjustment || '' }}</td>

                        <td class="text-right">{{ r.used }}</td>

                        <td class="text-right">{{ r.pending || '' }}</td>

                        <td class="text-right font-semibold">{{ r.remaining ?? '∞' }}</td>

                    </tr>

                </ng-template>

                <ng-template #emptymessage><tr><td colspan="7" class="text-center text-muted-color py-8">No employees match.</td></tr></ng-template>

            </p-table>

        </div>`

})

export class LeaveStatementPage {

    private api = inject(Api);

    private notify = inject(Notify);

    private lookups = inject(Lookups);

    rows = signal<StatementRow[]>([]);

    departments = signal<Option[]>([]);

    loading = signal(false);

    year = new Date().getFullYear();

    departmentId: number | null = null;

    count = computed(() => this.rows().length);



    constructor() {

        this.lookups

            .get('departments')

            .then((d) => this.departments.set(d))

            .catch(() => undefined);

        void this.load();

    }



    async load() {

        this.loading.set(true);

        try {

            this.rows.set(await this.api.get<StatementRow[]>('/api/leave/statement', { year: this.year, departmentId: this.departmentId }));

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.loading.set(false);

        }

    }

}
