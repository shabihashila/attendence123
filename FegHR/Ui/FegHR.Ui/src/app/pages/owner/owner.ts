import { Component, inject, signal } from '@angular/core';

import { Router } from '@angular/router';

import { Api, Paged, Session } from '@/app/core/api';

import { Notify, PageHeader, UI, date, isoDate, severity } from '@/app/core/ui';

import { Option } from '../crud';



interface Company {

    parentTenantRefId: number | null;
    parentName: string | null;
    maxChildConcerns: number | null;
    childCount: number;
    rowVersion: string;

    id: number;

    slug: string;

    name: string;

    phone: string | null;

    email: string | null;

    status: string;

    origin: string;

    createdAt: string;

    isLicensed: boolean;

    licenseKind: string | null;

    planName: string | null;

    licenseEndsAt: string | null;

    isOwnerCompany: boolean;

}

interface LicenceRow {

    id: number;

    planName: string;

    kind: string;

    startsAt: string;

    endsAt: string | null;

    licenseKey: string | null;

    note: string | null;

    status: string;

    createdAt: string;

}

interface Policy {

    trialDays: number;

    trialPlanCode: string;

    registrationOpen: boolean;

    rowVersion: string;

}



/**

 * The product owner's view of every company: its licence, issuing and revoking licences,

 * suspending a company, and stepping into a company to help.

 */

@Component({

    standalone: true,

    imports: [...UI, PageHeader],

    template: `<app-page-header title="Companies" subtitle="Every company on the product, with its licence. Open a company to work inside it.">

            <p-floatlabel variant="on"><input pInputText id="flt15" type="search" [(ngModel)]="search" (keyup.enter)="reload()" /><label for="flt15">Search</label></p-floatlabel>

            <p-button icon="pi pi-search" severity="secondary" [outlined]="true" (onClick)="reload()" ariaLabel="Search" />

        </app-page-header>

        <div class="card">

            <p-table [value]="rows()" [loading]="loading()" [lazy]="true" [paginator]="true" [rows]="pageSize" [totalRecords]="total()" (onLazyLoad)="onLazy($event)" stripedRows [tableStyle]="{ 'min-width': '58rem' }">

                <ng-template #header><tr><th>Company</th><th>Concerns</th><th>Contact</th><th>Registered</th><th>Status</th><th>Licence</th><th style="width:25rem"></th></tr></ng-template>

                <ng-template #body let-c>

                    <tr>

                        <td><span class="font-medium">{{ c.name }}</span>@if (c.isOwnerCompany) { <p-tag value="Owner company" severity="contrast" styleClass="ml-2" /> }<div class="text-muted-color text-sm">{{ c.slug }}</div></td>

                        <td>
                            @if (c.parentTenantRefId !== null) {
                                <p-tag value="Child concern" severity="secondary" /><div class="text-sm mt-1">{{ c.parentName }}</div>
                            } @else {
                                <p-tag value="Main company" severity="info" /><div class="text-sm mt-1">{{ c.childCount }} / {{ c.maxChildConcerns === null ? 'Unlimited' : c.maxChildConcerns }} concerns</div>
                            }
                        </td>

                        <td>{{ c.email || '—' }}<div class="text-muted-color text-sm">{{ c.phone }}</div></td>

                        <td>{{ date(c.createdAt) }}<div class="text-muted-color text-sm">{{ c.origin === 'Registration' ? 'Self-registered' : 'Added by operator' }}</div></td>

                        <td><p-tag [value]="c.status" [severity]="severity(c.status)" /></td>

                        <td>

                            @if (c.isOwnerCompany) {

                                <span class="text-muted-color">No licence needed</span>

                            } @else if (c.isLicensed) {

                                <p-tag [value]="c.licenseKind === 'Trial' ? 'Free trial' : c.licenseKind" [severity]="severity(c.licenseKind)" />

                                <div class="text-muted-color text-sm mt-1">{{ c.planName }} · {{ c.licenseEndsAt ? 'until ' + date(c.licenseEndsAt) : 'no end date' }}</div>

                            } @else {

                                <p-tag value="Not licensed" severity="danger" />

                            }

                        </td>

                        <td class="text-right whitespace-nowrap">

                            <p-button label="Edit" icon="pi pi-pencil" size="small" [outlined]="true" (onClick)="editCompany(c)" />
                            @if (c.parentTenantRefId === null) {
                                <p-button label="Add concern" icon="pi pi-plus" size="small" [outlined]="true" styleClass="ml-2" (onClick)="addConcern(c)" [disabled]="c.status !== 'Active' || (c.maxChildConcerns !== null && c.childCount >= c.maxChildConcerns)" />
                            }

                            @if (!c.isOwnerCompany) { <p-button label="Licences" icon="pi pi-key" size="small" [outlined]="true" (onClick)="openLicences(c)" /> }

                            <p-button label="Open" icon="pi pi-sign-in" size="small" severity="secondary" [outlined]="true" styleClass="ml-2" (onClick)="enter(c)" [disabled]="c.status !== 'Active'" />

                            <p-button [icon]="c.status === 'Active' ? 'pi pi-ban' : 'pi pi-check'" size="small" [severity]="c.status === 'Active' ? 'danger' : 'success'" [text]="true" styleClass="ml-1" [disabled]="c.isOwnerCompany" (onClick)="setStatus(c, c.status === 'Active' ? 'Suspended' : 'Active')" [pTooltip]="c.status === 'Active' ? 'Suspend' : 'Reactivate'" [ariaLabel]="c.status === 'Active' ? 'Suspend' : 'Reactivate'" />

                        </td>

                    </tr>

                </ng-template>

                <ng-template #emptymessage><tr><td colspan="7" class="text-center text-muted-color py-8">No companies yet.</td></tr></ng-template>

            </p-table>

        </div>

        <p-dialog [(visible)]="companyDialog" [header]="creatingConcern ? 'Add child concern to ' + editingCompany?.name : 'Edit company'" [modal]="true" [style]="{ width: '36rem' }" [breakpoints]="{ '720px': '95vw' }">
            <form class="flex flex-col gap-4 pt-2" (ngSubmit)="saveCompany()" #companyForm="ngForm">
                <p-floatlabel variant="on"><input pInputText id="companyName" name="companyName" [(ngModel)]="companyName" required minlength="2" maxlength="200" fluid /><label for="companyName">Registered company name *</label></p-floatlabel>
                @if (creatingConcern) {
                    <p class="text-muted-color m-0">This concern will have its own users, employees, attendance, leave and trial licence. Open it after saving to create its staff accounts.</p>
                } @else {
                    <p-floatlabel variant="on"><p-select inputId="parentCompany" name="parentCompany" [options]="parentOptions()" optionLabel="label" optionValue="value" [(ngModel)]="parentCompanyId" [disabled]="!!editingCompany?.isOwnerCompany || (editingCompany?.childCount ?? 0) > 0" appendTo="body" fluid /><label for="parentCompany">Main company</label></p-floatlabel>
                    @if (parentCompanyId === null) {
                        <p-floatlabel variant="on"><input pInputText id="concernLimit" name="concernLimit" type="number" [(ngModel)]="concernLimit" min="0" max="2147483647" step="1" fluid /><label for="concernLimit">Maximum child concerns</label></p-floatlabel>
                        <small class="text-muted-color">Blank means unlimited; zero prevents additions. All attached concerns count, including suspended ones. The limit cannot be below the current count.</small>
                    }
                    <small class="text-muted-color">Choose Standalone main company to remove a parent. Changing the name keeps the company ID, slug and branding name.</small>
                }
                <div class="flex justify-end gap-2"><p-button label="Cancel" severity="secondary" [text]="true" (onClick)="companyDialog = false" /><p-button type="submit" label="Save" icon="pi pi-check" [loading]="saving()" [disabled]="companyForm.invalid || saving()" /></div>
            </form>
        </p-dialog>



        <p-dialog [(visible)]="dialog" [header]="'Licences · ' + (current?.name || '')" [modal]="true" [style]="{ width: '56rem' }" [breakpoints]="{ '960px': '95vw' }">

            <p-table [value]="licences()" [tableStyle]="{ 'min-width': '40rem' }">

                <ng-template #header><tr><th>Kind</th><th>Plan</th><th>From</th><th>Until</th><th>Note</th><th>Status</th><th style="width:5rem"></th></tr></ng-template>

                <ng-template #body let-l>

                    <tr>

                        <td><p-tag [value]="l.kind" [severity]="severity(l.kind)" /></td>

                        <td>{{ l.planName }}</td><td>{{ date(l.startsAt) }}</td><td>{{ l.endsAt ? date(l.endsAt) : 'No end date' }}</td>

                        <td>{{ l.note || '' }}@if (l.licenseKey) { <div class="text-muted-color text-sm">{{ l.licenseKey }}</div> }</td>

                        <td><p-tag [value]="l.status" [severity]="severity(l.status)" /></td>

                        <td>@if (l.status === 'Active') { <p-button label="Revoke" size="small" severity="danger" [text]="true" (onClick)="revoke(l)" /> }</td>

                    </tr>

                </ng-template>

                <ng-template #emptymessage><tr><td colspan="7" class="text-center text-muted-color py-6">No licence has been issued.</td></tr></ng-template>

            </p-table>

            <form class="grid grid-cols-12 gap-4 mt-6 pt-6 border-t border-surface" (ngSubmit)="issue()" #form="ngForm">

                <div class="col-span-12 font-semibold">Issue a licence</div>

                <div class="col-span-6 md:col-span-3"><p-floatlabel variant="on"><p-select [filter]="true" inputId="kind" name="kind" [options]="kinds" [(ngModel)]="n.kind" appendTo="body" fluid /><label for="kind">Kind</label></p-floatlabel></div>

                <div class="col-span-6 md:col-span-3"><p-floatlabel variant="on"><p-select [filter]="true" inputId="plan" name="plan" [options]="plans()" optionLabel="label" optionValue="value" [(ngModel)]="n.planCode" required appendTo="body" fluid /><label for="plan">Plan</label></p-floatlabel></div>

                <div class="col-span-6 md:col-span-3"><p-floatlabel variant="on"><input pInputText id="starts" name="starts" type="date" [(ngModel)]="n.startsAt" required fluid /><label for="starts">From</label></p-floatlabel></div>

                <div class="col-span-6 md:col-span-3"><p-floatlabel variant="on"><input pInputText id="ends" name="ends" type="date" [(ngModel)]="n.endsAt" [min]="n.startsAt" fluid /><label for="ends">Until</label></p-floatlabel><small class="text-muted-color">Blank = no end date.</small></div>

                <div class="col-span-12 md:col-span-4"><p-floatlabel variant="on"><input pInputText id="key" name="key" [(ngModel)]="n.licenseKey" maxlength="100" fluid /><label for="key">Licence key / reference</label></p-floatlabel></div>

                <div class="col-span-12 md:col-span-8"><p-floatlabel variant="on"><input pInputText id="note" name="note" [(ngModel)]="n.note" maxlength="500" fluid /><label for="note">Note</label></p-floatlabel></div>

                <div class="col-span-12 flex justify-end"><p-button type="submit" label="Issue licence" icon="pi pi-check" [loading]="saving()" [disabled]="form.invalid || saving()" /></div>

            </form>

        </p-dialog>`

})

export class OwnerCompaniesPage {

    private api = inject(Api);

    private notify = inject(Notify);

    private session = inject(Session);

    private router = inject(Router);

    rows = signal<Company[]>([]);

    total = signal(0);

    loading = signal(false);

    saving = signal(false);

    licences = signal<LicenceRow[]>([]);

    plans = signal<Option[]>([]);

    search = '';

    page = 1;

    pageSize = 20;

    dialog = false;

    current: Company | null = null;

    companyDialog = false;
    creatingConcern = false;
    editingCompany: Company | null = null;
    companyName = '';
    parentCompanyId: number | null = null;
    concernLimit: number | null = null;
    mainCompanies = signal<{ id: number; name: string }[]>([]);

    parentOptions() {
        const options: { label: string; value: number | null }[] = [{ label: 'Standalone main company', value: null }];
        options.push(...this.mainCompanies().filter((c) => c.id !== this.editingCompany?.id).map((c) => ({ label: c.name, value: c.id })));
        // Preserve an existing suspended parent while renaming its concern.
        if (this.editingCompany?.parentTenantRefId != null && !options.some((o) => o.value === this.editingCompany!.parentTenantRefId))
            options.push({ label: this.editingCompany.parentName || 'Current main company', value: this.editingCompany.parentTenantRefId });
        return options;
    }

    async editCompany(company: Company) {
        try {
            this.mainCompanies.set(await this.api.get<{ id: number; name: string }[]>('/api/owner/companies/main'));
            this.editingCompany = company;
            this.creatingConcern = false;
            this.companyName = company.name;
            this.parentCompanyId = company.parentTenantRefId;
            this.concernLimit = company.maxChildConcerns;
            this.companyDialog = true;
        } catch (e) { this.notify.fail(e); }
    }

    addConcern(company: Company) {
        this.editingCompany = company;
        this.creatingConcern = true;
        this.companyName = '';
        this.companyDialog = true;
    }

    async saveCompany() {
        if (!this.editingCompany) return;
        if (!this.creatingConcern && this.concernLimit !== null && (!Number.isInteger(this.concernLimit) || this.concernLimit < 0)) {
            this.notify.fail(new Error('Enter a whole-number limit of zero or more.'));
            return;
        }
        this.saving.set(true);
        try {
            const id = this.editingCompany.id;
            const result = this.creatingConcern
                ? await this.api.post(`/api/owner/companies/${id}/concerns`, { name: this.companyName.trim() })
                : await this.api.put(`/api/owner/companies/${id}`, { name: this.companyName.trim(), parentTenantRefId: this.parentCompanyId, maxChildConcerns: this.concernLimit, rowVersion: this.editingCompany.rowVersion });
            this.notify.ok(result.message);
            this.companyDialog = false;
            await Promise.all([this.load(), this.api.loadShell()]);
        } catch (e) { this.notify.fail(e); }
        finally { this.saving.set(false); }
    }

    kinds = ['Paid', 'Complimentary', 'Trial'];

    n = this.blank();

    date = date;

    severity = severity;



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

            const result = await this.api.get<Paged<Company>>('/api/owner/companies', { page: this.page, pageSize: this.pageSize, search: this.search });

            this.rows.set(result.items);

            this.total.set(result.totalCount);

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.loading.set(false);

        }

    }



    async openLicences(company: Company) {

        this.current = company;

        this.n = this.blank();

        this.licences.set([]);

        this.dialog = true;

        try {

            const [licences, plans] = await Promise.all([

                this.api.get<LicenceRow[]>(`/api/owner/companies/${company.id}/licences`),

                this.plans().length ? Promise.resolve(null) : this.api.get<{ code: string; name: string }[]>('/api/owner/plans')

            ]);

            this.licences.set(licences);

            if (plans) this.plans.set(plans.map((p) => ({ label: p.name, value: p.code })));

            this.n.planCode = (this.plans()[this.plans().length - 1]?.value as string) ?? '';

        } catch (e) {

            this.notify.fail(e);

        }

    }



    async issue() {

        if (!this.current) return;

        this.saving.set(true);

        try {

            const result = await this.api.post(`/api/owner/companies/${this.current.id}/licences`, {

                planCode: this.n.planCode,

                kind: this.n.kind,

                // The licence runs from the start of the first day to the end of the last, local time.

                startsAt: new Date(this.n.startsAt + 'T00:00:00').toISOString(),

                endsAt: this.n.endsAt ? new Date(this.n.endsAt + 'T23:59:59').toISOString() : null,

                licenseKey: this.n.licenseKey || null,

                note: this.n.note || null

            });

            this.notify.ok(result.message);

            await Promise.all([this.openLicences(this.current), this.load()]);

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.saving.set(false);

        }

    }



    async revoke(licence: LicenceRow) {

        if (!this.current) return;

        try {

            this.notify.ok((await this.api.delete(`/api/owner/companies/${this.current.id}/licences/${licence.id}`)).message);

            await Promise.all([this.openLicences(this.current), this.load()]);

        } catch (e) {

            this.notify.fail(e);

        }

    }



    async setStatus(company: Company, status: string) {

        try {

            this.notify.ok((await this.api.put(`/api/owner/companies/${company.id}/status`, { status })).message);

            await this.load();

        } catch (e) {

            this.notify.fail(e);

        }

    }



    /** Works inside the company through the product owner's own membership; the switch is logged. */

    async enter(company: Company) {

        try {

            await this.api.switchTenant(company.id);

            await this.router.navigate(['/app/dashboard']);

            this.notify.ok(`Now working in ${this.session.me()?.tenant?.name}.`);

        } catch (e) {

            this.notify.fail(e);

        }

    }



    private blank() {

        const end = new Date();

        end.setFullYear(end.getFullYear() + 1);

        return { kind: 'Paid', planCode: '', startsAt: isoDate(), endsAt: isoDate(end), licenseKey: '', note: '' };

    }

}



/** What a public registration receives: the length of the free trial, its plan, and whether sign-up is open. */

@Component({

    standalone: true,

    imports: [...UI, PageHeader],

    template: `<app-page-header title="Licence Policy" subtitle="Applies to companies that register from now on. Existing trials keep the length they started with." />

        @if (model(); as m) {

            <form class="card flex flex-col gap-5" style="max-width:40rem" (ngSubmit)="save()" #form="ngForm">

                <div class="flex items-center justify-between gap-4 rounded-border border border-surface p-4">

                    <label for="open"><span class="font-semibold block">Public registration is open</span><span class="text-muted-color">When off, the registration page tells visitors to contact you.</span></label>

                    <p-toggleswitch inputId="open" name="open" [(ngModel)]="m.registrationOpen" />

                </div>

                <div>

                    <p-floatlabel variant="on"><input pInputText id="days" name="days" type="number" min="1" max="3650" [(ngModel)]="m.trialDays" required fluid /><label for="days">Free trial length (days)</label></p-floatlabel>

                </div>

                <div>

                    <p-floatlabel variant="on"><p-select [filter]="true" inputId="plan" name="plan" [options]="plans()" optionLabel="label" optionValue="value" [(ngModel)]="m.trialPlanCode" required fluid /><label for="plan">Plan during the trial</label></p-floatlabel>

                </div>

                <div><p-button type="submit" label="Save policy" icon="pi pi-check" [loading]="saving()" [disabled]="form.invalid || saving()" /></div>

            </form>

        }`

})

export class LicencePolicyPage {

    private api = inject(Api);

    private notify = inject(Notify);

    model = signal<Policy | null>(null);

    plans = signal<Option[]>([]);

    saving = signal(false);



    constructor() {

        Promise.all([this.api.get<Policy>('/api/owner/licence-policy'), this.api.get<{ code: string; name: string }[]>('/api/owner/plans')])

            .then(([policy, plans]) => {

                this.model.set(policy);

                this.plans.set(plans.map((p) => ({ label: p.name, value: p.code })));

            })

            .catch((e) => this.notify.fail(e));

    }



    async save() {

        const m = this.model();

        if (!m) return;

        this.saving.set(true);

        try {

            const result = await this.api.put<Policy>('/api/owner/licence-policy', { ...m, trialDays: Number(m.trialDays) });

            this.model.set(result.data);

            this.notify.ok(result.message);

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.saving.set(false);

        }

    }

}
