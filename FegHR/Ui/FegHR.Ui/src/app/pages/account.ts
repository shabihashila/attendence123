import { Component, computed, inject, signal } from '@angular/core';

import { PasswordModule } from 'primeng/password';

import { Api, Session, TenantOption } from '@/app/core/api';

import { Notify, PageHeader, UI } from '@/app/core/ui';



/** The signed-in person's own account: who they are here and their password. */

@Component({

    standalone: true,

    imports: [...UI, PasswordModule, PageHeader],

    template: `<app-page-header title="My account" subtitle="Your sign-in for this company." />

        <div class="grid grid-cols-12 gap-6">

            <div class="col-span-12 lg:col-span-5">

                <div class="card !mb-0">

                    <h2 class="text-xl font-semibold mt-0 mb-4">Profile</h2>

                    <dl class="grid grid-cols-3 gap-y-3 m-0">

                        <dt class="text-muted-color">Name</dt><dd class="col-span-2 m-0 font-medium">{{ session.me()?.user?.displayName }}</dd>

                        <dt class="text-muted-color">Email</dt><dd class="col-span-2 m-0">{{ session.me()?.user?.email || '—' }}</dd>

                        <dt class="text-muted-color">Mobile</dt><dd class="col-span-2 m-0">{{ session.me()?.user?.phone || '—' }}</dd>

                        <dt class="text-muted-color">Company</dt><dd class="col-span-2 m-0">{{ session.me()?.tenant?.name }}</dd>

                        <dt class="text-muted-color">Time zone</dt><dd class="col-span-2 m-0">{{ session.me()?.tenant?.timeZoneId }}</dd>

                    </dl>

                </div>

            </div>

            <div class="col-span-12 lg:col-span-7">

                <form class="card !mb-0 flex flex-col gap-4" (ngSubmit)="change()" #form="ngForm">

                    <h2 class="text-xl font-semibold m-0">Change password</h2>

                    <div>

                        <p-floatlabel variant="on"><p-password inputId="current" name="current" [(ngModel)]="current" [feedback]="false" [toggleMask]="true" required autocomplete="current-password" fluid /><label for="current">Current password</label></p-floatlabel>

                    </div>

                    <div>

                        <p-floatlabel variant="on"><p-password inputId="next" name="next" [(ngModel)]="next" [feedback]="false" [toggleMask]="true" required [minlength]="8" autocomplete="new-password" fluid /><label for="next">New password</label></p-floatlabel>

                        <small class="text-muted-color">At least 8 characters with a letter and a digit.</small>

                    </div>

                    <div>

                        <p-button type="submit" label="Change password" icon="pi pi-key" [loading]="saving()" [disabled]="form.invalid || saving()" />

                    </div>

                </form>

            </div>

            <div class="col-span-12">
                <form class="card flex flex-col gap-4" (ngSubmit)="saveDefaults()">
                    <h2 class="text-xl font-semibold m-0">Default company</h2>
                    <p class="m-0 text-muted-color">Start here when you sign in. You can still switch companies from the top bar without changing this default.</p>
                    <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                        <div><label for="defaultMainCompany" class="block mb-2">Default main company</label>
                            <p-select inputId="defaultMainCompany" name="defaultMainCompany" [options]="defaultMains()" optionLabel="name" optionValue="id" [ngModel]="defaultMainId()" (ngModelChange)="chooseDefaultMain($event)" appendTo="body" fluid /></div>
                        @if (defaultChildren().length) {
                            <div><label for="defaultChildCompany" class="block mb-2">Default child company</label>
                                <p-select inputId="defaultChildCompany" name="defaultChildCompany" [options]="defaultChildren()" optionLabel="name" optionValue="tenantId" [ngModel]="defaultChildId()" (ngModelChange)="defaultChildId.set($event)" appendTo="body" fluid /></div>
                        }
                    </div>
                    <small class="text-muted-color">Only your active company access is listed. A main-company name can group accessible children without granting access to the main company's records.</small>
                    <div><p-button type="submit" label="Save default company" icon="pi pi-check" [loading]="savingDefaults()" [disabled]="!defaultMainId() || savingDefaults()" /></div>
                </form>
            </div>
        </div>`

})

export class AccountPage {

    private api = inject(Api);

    private notify = inject(Notify);

    session = inject(Session);

    saving = signal(false);

    current = '';

    next = '';



    defaultOptions = signal<TenantOption[]>([]);
    defaultMainId = signal<number | null>(null);
    defaultChildId = signal<number | null>(null);
    savingDefaults = signal(false);
    defaultMains = computed(() => {
        const roots = new Map<number, { id: number; name: string }>();
        for (const t of this.defaultOptions()) {
            const id = t.parentTenantRefId ?? t.tenantId;
            roots.set(id, { id, name: t.parentTenantRefId ? (t.parentName || 'Main company') : t.name });
        }
        return [...roots.values()].sort((a, b) => a.name.localeCompare(b.name));
    });
    defaultChildren = computed(() => this.defaultOptions().filter(t => t.parentTenantRefId === this.defaultMainId()));

    constructor() { void this.loadDefaults(); }

    async loadDefaults() {
        try {
            const options = await this.api.get<TenantOption[]>('/api/iam/session/default-company');
            this.defaultOptions.set(options);
            const selected = options.find(t => t.isDefault);
            this.defaultMainId.set(selected ? (selected.parentTenantRefId ?? selected.tenantId) : null);
            this.defaultChildId.set(selected?.parentTenantRefId ? selected.tenantId : null);
        } catch (e) { this.notify.fail(e); }
    }

    chooseDefaultMain(id: number) {
        this.defaultMainId.set(id);
        const children = this.defaultChildren();
        this.defaultChildId.set((children.find(t => t.isDefault) ?? children[0])?.tenantId ?? null);
    }

    async saveDefaults() {
        const tenantId = this.defaultChildId() ?? this.defaultMainId();
        if (!tenantId) return;
        this.savingDefaults.set(true);
        try {
            const result = await this.api.put('/api/iam/session/default-company', { tenantId });
            this.notify.ok(result.message);
            await this.loadDefaults();
            await this.api.loadShell();
        } catch (e) { this.notify.fail(e); }
        finally { this.savingDefaults.set(false); }
    }

    async change() {

        this.saving.set(true);

        try {

            const result = await this.api.post('/api/iam/me/change-password', { currentPassword: this.current, newPassword: this.next });

            this.notify.ok(result.message);

            this.current = this.next = '';

            // The change signs every session of this account out, including this one's refresh token.

            await this.api.signOut();

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.saving.set(false);

        }

    }

}
