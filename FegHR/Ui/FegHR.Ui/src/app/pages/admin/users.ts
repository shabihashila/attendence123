import { Component, computed, inject, signal } from '@angular/core';

import { MultiSelectModule } from 'primeng/multiselect';

import { PasswordModule } from 'primeng/password';

import { Api, Session } from '@/app/core/api';

import { Notify, PageHeader, UI } from '@/app/core/ui';



interface Member {

    tenantUserId: number;

    userId: number;

    displayName: string;

    email: string | null;

    phone: string | null;

    isActive: boolean;

    roleIds: number[];

    branchIds: number[];

    isProductOwner: boolean;

}

interface Role {

    id: number;

    code: string;

    name: string;

    isSystem: boolean;

    isActive: boolean;

    permissionCodes: string[];

    rowVersion: string;

}

interface Permission {

    code: string;

    name: string;

    module: string;

}



const MODULES: Record<string, string> = { platform: 'Company', iam: 'Users and roles', org: 'Organization', attendance: 'Attendance', leave: 'Leave' };



/** Sign-ins of the company and the roles each one holds. */

@Component({

    standalone: true,

    imports: [...UI, MultiSelectModule, PasswordModule, PageHeader],

    template: `<app-page-header title="Users" subtitle="People who can sign in to this company. Link a user to an employee record under Organization → Employees.">

            @if (canWrite()) {

                <p-button label="New user" icon="pi pi-plus" (onClick)="open(null)" />

            }

        </app-page-header>

        <div class="card">

            <p-table #dtUsers [rowHover]="true" [showGridlines]="true" [globalFilterFields]="['displayName', 'email', 'phone']" [value]="members()" stripedRows [paginator]="members().length > 20" [rows]="20" [tableStyle]="{ 'min-width': '44rem' }">

                <ng-template #caption>

                    <div class="flex flex-wrap justify-between items-center gap-2">

                        <p-button label="Clear" icon="pi pi-filter-slash" [outlined]="true" (onClick)="dtUsers.clear(); dtUsersq.value = ''" />

                        <p-iconfield iconPosition="left" class="ml-auto">

                            <p-inputicon><i class="pi pi-search"></i></p-inputicon>

                            <input pInputText #dtUsersq type="text" (input)="dtUsers.filterGlobal(dtUsersq.value, 'contains')" placeholder="Search keyword" aria-label="Search keyword" />

                        </p-iconfield>

                    </div>

                </ng-template>

                <ng-template #header><tr><th pSortableColumn="displayName"><div class="flex justify-between items-center gap-2">Name<span class="flex items-center"><p-sortIcon field="displayName" /><p-columnFilter type="text" field="displayName" display="menu" /></span></div></th><th pSortableColumn="email"><div class="flex justify-between items-center gap-2">Sign-in<span class="flex items-center"><p-sortIcon field="email" /><p-columnFilter type="text" field="email" display="menu" /></span></div></th><th>Roles</th><th pSortableColumn="isActive"><div class="flex justify-between items-center gap-2">Active<span class="flex items-center"><p-sortIcon field="isActive" /><p-columnFilter type="boolean" field="isActive" display="menu" /></span></div></th><th style="width:4rem"></th></tr></ng-template>

                <ng-template #body let-m>

                    <tr>

                        <td><span class="font-medium">{{ m.displayName }}</span>@if (m.isProductOwner) { <p-tag value="Product owner" severity="contrast" styleClass="ml-2" /> }</td>

                        <td>{{ m.email || m.phone }}</td>

                        <td>@for (id of m.roleIds; track id) { <p-tag [value]="roleName(id)" severity="secondary" styleClass="mr-1" /> }</td>

                        <td><i class="pi" [ngClass]="m.isActive ? 'pi-check-circle text-green-500' : 'pi-times-circle text-muted-color'"></i></td>

                        <td>

                            @if (canWrite() && !m.isProductOwner) {

                                <p-button icon="pi pi-pencil" [rounded]="true" [text]="true" (onClick)="open(m)" ariaLabel="Edit user" />

                            }

                        </td>

                    </tr>

                </ng-template>

            </p-table>

        </div>



        <p-dialog [(visible)]="dialog" [header]="editing ? 'Edit ' + editing.displayName : 'New user'" [modal]="true" [style]="{ width: '40rem' }" [breakpoints]="{ '720px': '95vw' }">

            <form class="grid grid-cols-12 gap-4 pt-2" (ngSubmit)="save()" #form="ngForm">

                @if (!editing) {

                    <div class="col-span-12"><p-floatlabel variant="on"><input pInputText id="displayName" name="displayName" [(ngModel)]="m.displayName" required minlength="2" maxlength="150" fluid /><label for="displayName">Name *</label></p-floatlabel></div>

                    <div class="col-span-12 md:col-span-6"><p-floatlabel variant="on"><input pInputText id="email" name="email" type="email" [(ngModel)]="m.email" email maxlength="254" fluid /><label for="email">Email</label></p-floatlabel></div>

                    <div class="col-span-12 md:col-span-6"><p-floatlabel variant="on"><input pInputText id="phone" name="phone" [(ngModel)]="m.phone" maxlength="30" fluid /><label for="phone">Mobile number</label></p-floatlabel></div>

                    <div class="col-span-12">

                        <p-floatlabel variant="on"><p-password inputId="password" name="password" [(ngModel)]="m.password" [feedback]="false" [toggleMask]="true" autocomplete="new-password" fluid /><label for="password">Password</label></p-floatlabel>

                        <small class="text-muted-color">At least 8 characters with a letter and a digit. Not needed when the email or number already has an account with another company.</small>

                    </div>

                }

                <div class="col-span-12"><p-floatlabel variant="on"><p-multiselect inputId="roles" name="roles" [options]="roleOptions()" optionLabel="label" optionValue="value" [(ngModel)]="m.roleIds" required display="chip" appendTo="body" fluid /><label for="roles">Roles *</label></p-floatlabel></div>

                @if (editing) {

                    <div class="col-span-12 flex items-center gap-2"><p-checkbox inputId="isActive" name="isActive" [binary]="true" [(ngModel)]="m.isActive" /><label for="isActive">Active (can sign in to this company)</label></div>

                }

                <div class="col-span-12 flex justify-end gap-2">

                    <p-button type="button" label="Cancel" severity="secondary" [text]="true" (onClick)="dialog = false" />

                    <p-button type="submit" label="Save" icon="pi pi-check" [loading]="saving()" [disabled]="form.invalid || saving()" />

                </div>

            </form>

        </p-dialog>`

})

export class UsersPage {

    private api = inject(Api);

    private notify = inject(Notify);

    private session = inject(Session);

    members = signal<Member[]>([]);

    roles = signal<Role[]>([]);

    saving = signal(false);

    canWrite = computed(() => this.session.can('iam.users.write'));

    roleOptions = computed(() => this.roles().filter((r) => r.isActive).map((r) => ({ label: r.name, value: r.id })));

    dialog = false;

    editing: Member | null = null;

    m = this.blank();



    constructor() {

        void this.load();

    }



    roleName(id: number) {

        return this.roles().find((r) => r.id === id)?.name ?? '';

    }



    async load() {

        try {

            const [members, roles] = await Promise.all([this.api.get<Member[]>('/api/iam/users'), this.session.can('iam.roles.read') ? this.api.get<Role[]>('/api/iam/roles') : Promise.resolve([])]);

            this.members.set(members);

            this.roles.set(roles);

        } catch (e) {

            this.notify.fail(e);

        }

    }



    open(member: Member | null) {

        this.editing = member;

        this.m = member ? { ...this.blank(), displayName: member.displayName, roleIds: [...member.roleIds], isActive: member.isActive } : this.blank();

        this.dialog = true;

    }



    async save() {

        this.saving.set(true);

        try {

            const result = this.editing

                ? await this.api.put(`/api/iam/users/${this.editing.tenantUserId}`, { roleIds: this.m.roleIds, branchIds: this.editing.branchIds, isActive: this.m.isActive })

                : await this.api.post('/api/iam/users', { displayName: this.m.displayName, email: this.m.email || null, phone: this.m.phone || null, password: this.m.password || null, roleIds: this.m.roleIds, branchIds: [] });

            this.notify.ok(result.message ?? 'User saved.');

            this.dialog = false;

            await this.load();

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.saving.set(false);

        }

    }



    private blank() {

        return { displayName: '', email: '', phone: '', password: '', roleIds: [] as number[], isActive: true };

    }

}



/** Roles and what each may do. The Company Admin role always holds every permission. */

@Component({

    standalone: true,

    imports: [...UI, PageHeader],

    template: `<app-page-header title="Roles" subtitle="A role is a set of permissions. Give a user one or more roles under Users.">

            @if (canWrite()) {

                <p-button label="New role" icon="pi pi-plus" (onClick)="open(null)" />

            }

        </app-page-header>

        <div class="card">

            <p-table #dtRoles [rowHover]="true" [showGridlines]="true" [globalFilterFields]="['name', 'code']" [value]="roles()" stripedRows [tableStyle]="{ 'min-width': '40rem' }">

                <ng-template #caption>

                    <div class="flex flex-wrap justify-between items-center gap-2">

                        <p-button label="Clear" icon="pi pi-filter-slash" [outlined]="true" (onClick)="dtRoles.clear(); dtRolesq.value = ''" />

                        <p-iconfield iconPosition="left" class="ml-auto">

                            <p-inputicon><i class="pi pi-search"></i></p-inputicon>

                            <input pInputText #dtRolesq type="text" (input)="dtRoles.filterGlobal(dtRolesq.value, 'contains')" placeholder="Search keyword" aria-label="Search keyword" />

                        </p-iconfield>

                    </div>

                </ng-template>

                <ng-template #header><tr><th pSortableColumn="name"><div class="flex justify-between items-center gap-2">Role<span class="flex items-center"><p-sortIcon field="name" /><p-columnFilter type="text" field="name" display="menu" /></span></div></th><th pSortableColumn="code"><div class="flex justify-between items-center gap-2">Code<span class="flex items-center"><p-sortIcon field="code" /><p-columnFilter type="text" field="code" display="menu" /></span></div></th><th>Permissions</th><th pSortableColumn="isActive"><div class="flex justify-between items-center gap-2">Active<span class="flex items-center"><p-sortIcon field="isActive" /><p-columnFilter type="boolean" field="isActive" display="menu" /></span></div></th><th style="width:7rem"></th></tr></ng-template>

                <ng-template #body let-r>

                    <tr>

                        <td><span class="font-medium">{{ r.name }}</span>@if (r.isSystem) { <p-tag value="Built in" severity="secondary" styleClass="ml-2" /> }</td>

                        <td>{{ r.code }}</td>

                        <td>{{ r.code === 'admin' ? 'Everything' : r.permissionCodes.length }}</td>

                        <td><i class="pi" [ngClass]="r.isActive ? 'pi-check-circle text-green-500' : 'pi-times-circle text-muted-color'"></i></td>

                        <td class="text-right whitespace-nowrap">

                            @if (canWrite()) {

                                <p-button icon="pi pi-pencil" [rounded]="true" [text]="true" (onClick)="open(r)" ariaLabel="Edit role" />

                                @if (!r.isSystem) {

                                    <p-button icon="pi pi-trash" severity="danger" [rounded]="true" [text]="true" (onClick)="remove(r)" ariaLabel="Delete role" />

                                }

                            }

                        </td>

                    </tr>

                </ng-template>

            </p-table>

        </div>



        <p-dialog [(visible)]="dialog" [header]="editing ? 'Edit ' + editing.name : 'New role'" [modal]="true" [style]="{ width: '50rem' }" [breakpoints]="{ '960px': '95vw' }">

            <form class="flex flex-col gap-4 pt-2" (ngSubmit)="save()" #form="ngForm">

                <div class="grid grid-cols-12 gap-4">

                    <div class="col-span-12 md:col-span-6"><p-floatlabel variant="on"><input pInputText id="name" name="name" [(ngModel)]="m.name" required minlength="2" maxlength="100" fluid /><label for="name">Name *</label></p-floatlabel></div>

                    <div class="col-span-12 md:col-span-6">

                        <p-floatlabel variant="on"><input pInputText id="code" name="code" [(ngModel)]="m.code" required pattern="[a-z][a-z0-9-]*" maxlength="50" [disabled]="!!editing" fluid /><label for="code">Code *</label></p-floatlabel>

                        <small class="text-muted-color">Lowercase letters, digits and dashes.</small>

                    </div>

                </div>

                @if (editing?.code === 'admin') {

                    <p-message severity="info" styleClass="w-full" text="The Company Admin role always holds every permission." />

                } @else {

                    <div class="grid grid-cols-12 gap-6">

                        @for (g of groups(); track g.module) {

                            <div class="col-span-12 md:col-span-6">

                                <div class="font-semibold mb-2">{{ g.title }}</div>

                                @for (p of g.items; track p.code) {

                                    <div class="flex items-center gap-2 mb-2">

                                        <p-checkbox [inputId]="p.code" [name]="p.code" [binary]="true" [ngModel]="m.codes.has(p.code)" (ngModelChange)="toggle(p.code, $event)" />

                                        <label [for]="p.code">{{ p.name }}</label>

                                    </div>

                                }

                            </div>

                        }

                    </div>

                }

                <div class="flex items-center gap-2"><p-checkbox inputId="roleActive" name="roleActive" [binary]="true" [(ngModel)]="m.isActive" /><label for="roleActive">Active</label></div>

                <div class="flex justify-end gap-2">

                    <p-button type="button" label="Cancel" severity="secondary" [text]="true" (onClick)="dialog = false" />

                    <p-button type="submit" label="Save" icon="pi pi-check" [loading]="saving()" [disabled]="form.invalid || saving()" />

                </div>

            </form>

        </p-dialog>`

})

export class RolesPage {

    private api = inject(Api);

    private notify = inject(Notify);

    private session = inject(Session);

    roles = signal<Role[]>([]);

    permissions = signal<Permission[]>([]);

    saving = signal(false);

    canWrite = computed(() => this.session.can('iam.roles.write'));

    groups = computed(() => {

        const all = this.permissions();

        return [...new Set(all.map((p) => p.module))].map((module) => ({ module, title: MODULES[module] ?? module, items: all.filter((p) => p.module === module) }));

    });

    dialog = false;

    editing: Role | null = null;

    m = { name: '', code: '', isActive: true, codes: new Set<string>() };



    constructor() {

        void this.load();

    }



    async load() {

        try {

            this.roles.set(await this.api.get<Role[]>('/api/iam/roles'));

            if (this.canWrite()) this.permissions.set(await this.api.get<Permission[]>('/api/iam/permissions'));

        } catch (e) {

            this.notify.fail(e);

        }

    }



    open(role: Role | null) {

        this.editing = role;

        this.m = role ? { name: role.name, code: role.code, isActive: role.isActive, codes: new Set(role.permissionCodes) } : { name: '', code: '', isActive: true, codes: new Set<string>() };

        this.dialog = true;

    }



    toggle(code: string, on: boolean) {

        on ? this.m.codes.add(code) : this.m.codes.delete(code);

    }



    async save() {

        const body = { code: this.m.code, name: this.m.name, isActive: this.m.isActive, permissionCodes: [...this.m.codes], rowVersion: this.editing?.rowVersion ?? null };

        this.saving.set(true);

        try {

            const result = this.editing ? await this.api.put(`/api/iam/roles/${this.editing.id}`, body) : await this.api.post('/api/iam/roles', body);

            this.notify.ok(result.message ?? 'Role saved.');

            this.dialog = false;

            await this.load();

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.saving.set(false);

        }

    }



    async remove(role: Role) {

        try {

            this.notify.ok((await this.api.delete(`/api/iam/roles/${role.id}`)).message ?? 'Role deleted.');

            await this.load();

        } catch (e) {

            this.notify.fail(e);

        }

    }

}
