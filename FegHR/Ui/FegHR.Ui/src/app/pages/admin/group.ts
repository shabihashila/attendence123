import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Router } from '@angular/router';
import { Api, Session } from '@/app/core/api';
import { Notify, PageHeader, UI, dateTime } from '@/app/core/ui';

interface GroupInfo { groupId: number; name: string; canManage: boolean; canBill:boolean; }
interface Member { userId: number; displayName: string; email: string; roleCode: string; isActive: boolean; }
interface Company { id: number; name: string; employees: number | null; users: number | null; attendanceDays: number | null; pendingLeave: number | null; licenseKind: string | null; licenseEndsAt: string | null; rowVersion:string; }
interface Audit { id: number; actor: string; action: string; detail: string; occurredAt: string; }
interface Settings { globalDefaults:string; groupDefaults: string; companyOverrides: string; globalRowVersion:string; groupRowVersion:string|null; companyRowVersion:string|null; effective: Record<string, unknown>; }
interface Billing {billingName:string;email:string;address:string|null;rowVersion:string|null;}

@Component({
    standalone: true, imports: [...UI, PageHeader],
    template: `<app-page-header [title]="settingsOnly ? 'Company Settings' : 'Group Console'" [subtitle]="group()?.name || 'Company context: ' + session.me()?.tenant?.name" />
        @if (!settingsOnly) {
            <div class="card"><h2>Group companies and reporting</h2><p>Switch into a company to manage its users, roles, modules, leave and branding. Company data stays separate.</p>
                @if(group()?.canManage){<form class="flex gap-3 mb-4" (ngSubmit)="createConcern()" #concernForm="ngForm"><input pInputText name="newConcernName" [(ngModel)]="newConcernName" placeholder="New child concern name" required minlength="2" maxlength="200" aria-label="New child concern name" /><p-button type="submit" label="Add child concern" [disabled]="concernForm.invalid || busy()" /></form>}
                <p-table [value]="companies()" [scrollable]="true"><ng-template #header><tr><th>Company</th><th>Employees</th><th>Users</th><th>Attendance days</th><th>Pending leave</th><th>Licence</th><th></th></tr></ng-template>
                <ng-template #body let-c><tr><td>@if(group()?.canManage){<input pInputText [(ngModel)]="c.name" maxlength="200" aria-label="Registered company name" /><p-button label="Save name" [text]="true" (onClick)="renameCompany(c)" />}@else{ {{c.name}} }</td><td>{{c.employees ?? '—'}}</td><td>{{c.users ?? '—'}}</td><td>{{c.attendanceDays ?? '—'}}</td><td>{{c.pendingLeave ?? '—'}}</td><td>{{c.licenseKind || 'No active licence'}}<div>{{c.licenseEndsAt ? dateTime(c.licenseEndsAt) : ''}}</div></td><td><p-button label="Open company" (onClick)="openCompany(c.id)" /></td></tr></ng-template></p-table>
            </div>
            @if (group()?.canManage) {
                <div class="card"><h2>Group access</h2><p>Create a login under Users first. Use its existing email here. Company-specific roles remain independent.</p>
                    <form class="flex flex-wrap gap-3 items-end" (ngSubmit)="saveMember()" #memberForm="ngForm">
                        <p-floatlabel variant="on"><input pInputText id="groupEmail" name="groupEmail" [(ngModel)]="email" type="email" email required /><label for="groupEmail">User email</label></p-floatlabel>
                        <p-select name="groupRole" [options]="roles" optionLabel="label" optionValue="value" [(ngModel)]="role" />
                        <div><p-checkbox name="groupActive" inputId="groupActive" [(ngModel)]="active" [binary]="true" /><label for="groupActive" class="ml-2">Active group access</label></div>
                        <p-button label="Save group access" type="submit" [disabled]="memberForm.invalid || busy()" /></form>
                    <p-table [value]="members()" styleClass="mt-4"><ng-template #header><tr><th>Name</th><th>Email</th><th>Group role</th><th>Active</th><th></th></tr></ng-template><ng-template #body let-m><tr><td>{{m.displayName}}</td><td>{{m.email}}</td><td>{{m.roleCode}}</td><td>{{m.isActive ? 'Yes' : 'No'}}</td><td><p-button label="Edit access" [text]="true" (onClick)="editMember(m)" /></td></tr></ng-template></p-table>
                </div>
            }
        }
        @if (settingsOnly || group()?.canManage) {
            @if(session.me()?.isPlatformAdmin) { <div class="card"><h2>Global defaults</h2><textarea pTextarea [(ngModel)]="globalJson" rows="5" class="w-full font-mono" aria-label="Global configuration JSON"></textarea><p-button label="Save global defaults" (onClick)="saveGlobal()" styleClass="mt-3" [loading]="busy()" /></div> }
            <div class="card"><h2>{{settingsOnly ? 'Company overrides' : 'Group defaults'}}</h2><p>JSON configuration keys inherit group defaults. Company overrides replace matching keys. Leave approvals, branding and time zone use their dedicated settings pages.</p>
                <textarea pTextarea [(ngModel)]="settingsJson" rows="8" class="w-full font-mono" maxlength="10000" aria-label="Configuration JSON"></textarea>
                <p-button label="Save settings" (onClick)="saveSettings()" [loading]="busy()" styleClass="mt-3" />
                <h3>Effective company configuration</h3><pre class="overflow-auto">{{effective()}}</pre>
            </div>
        }
        @if (!settingsOnly) {
            <div class="card"><h2>Group billing profile</h2><p>Billing contacts apply to this group. Licence issuance and plan pricing remain under the platform Owner.</p>
                <div class="flex flex-col gap-3"><p-floatlabel variant="on"><input pInputText id="billingName" [(ngModel)]="billing.billingName" [disabled]="!group()?.canBill" maxlength="200" fluid /><label for="billingName">Billing name</label></p-floatlabel><p-floatlabel variant="on"><input pInputText id="billingEmail" type="email" [(ngModel)]="billing.email" [disabled]="!group()?.canBill" maxlength="254" fluid /><label for="billingEmail">Billing email</label></p-floatlabel><p-floatlabel variant="on"><textarea pTextarea id="billingAddress" [(ngModel)]="billing.address" [disabled]="!group()?.canBill" maxlength="500" fluid></textarea><label for="billingAddress">Billing address</label></p-floatlabel>@if(group()?.canBill){<p-button label="Save billing profile" (onClick)="saveBilling()" [loading]="busy()" />}</div>
            </div>
            <div class="card"><h2>Group audit history</h2><p-table [value]="audit()"><ng-template #header><tr><th>When</th><th>Actor</th><th>Action</th><th>Details</th></tr></ng-template><ng-template #body let-a><tr><td>{{dateTime(a.occurredAt)}}</td><td>{{a.actor}}</td><td>{{a.action}}</td><td>{{a.detail}}</td></tr></ng-template></p-table></div>
        }`
})
export class GroupPage {
    private api = inject(Api); private notify = inject(Notify); private router=inject(Router); session = inject(Session);
    settingsOnly = !!inject(ActivatedRoute).snapshot.data['settingsOnly'];
    group = signal<GroupInfo | null>(null); companies = signal<Company[]>([]); members = signal<Member[]>([]); audit = signal<Audit[]>([]);
    busy = signal(false); effective = signal('{}'); settingsJson = '{}'; email = ''; role = 'group-admin'; active = true;
    globalJson='{}'; settings:Settings|null=null; billing:Billing={billingName:'',email:'',address:'',rowVersion:null};
    newConcernName='';
    roles = [{label:'Master Group Admin',value:'group-admin'},{label:'Group Auditor',value:'group-viewer'},{label:'Group Billing',value:'group-billing'}];
    dateTime = dateTime;
    constructor() { void this.load(); }
    async load() {
        try {
            if (!this.settingsOnly) {
                this.group.set(await this.api.get<GroupInfo>('/api/groups/current'));
                this.companies.set(await this.api.get<Company[]>('/api/groups/companies'));
                if (this.group()?.canManage) this.members.set(await this.api.get<Member[]>('/api/groups/members'));
                this.audit.set(await this.api.get<Audit[]>('/api/groups/audit').catch(() => []));
                this.billing=(await this.api.get<Billing|null>('/api/groups/billing')) || {billingName:'',email:'',address:'',rowVersion:null};
            }
            const settings = await this.api.get<Settings>('/api/groups/settings');
            this.settings=settings; this.globalJson=settings.globalDefaults;
            this.settingsJson = this.settingsOnly ? settings.companyOverrides : settings.groupDefaults;
            this.effective.set(JSON.stringify(settings.effective, null, 2));
        } catch (e) { this.notify.fail(e); }
    }
    editMember(m: Member) { this.email=m.email; this.role=m.roleCode; this.active=m.isActive; }
    async saveMember() {
        this.busy.set(true);
        try { await this.api.put('/api/groups/members',{email:this.email,roleCode:this.role,isActive:this.active}); this.notify.ok('Group access saved.'); await this.load(); await this.api.loadShell(); }
        catch(e) {this.notify.fail(e);} finally {this.busy.set(false);}
    }
    async saveSettings() {
        this.busy.set(true);
        try { await this.api.put('/api/groups/settings',{settingsJson:this.settingsJson,companyOverride:this.settingsOnly,globalOverride:false,rowVersion:this.settingsOnly ? this.settings?.companyRowVersion : this.settings?.groupRowVersion}); this.notify.ok('Settings saved.'); await this.load(); }
        catch(e) {this.notify.fail(e);} finally {this.busy.set(false);}
    }
    async openCompany(id:number) {try {await this.api.switchTenant(id); await this.router.navigateByUrl('/app/dashboard');} catch(e){this.notify.fail(e);} }
    async createConcern(){this.busy.set(true);try{await this.api.post('/api/groups/concerns',{name:this.newConcernName});this.newConcernName='';this.notify.ok('Child concern created.');await this.load();await this.api.loadShell();}catch(e){this.notify.fail(e);}finally{this.busy.set(false);}}
    async renameCompany(company:Company){this.busy.set(true);try{await this.api.put(`/api/groups/companies/${company.id}/name`,{name:company.name,rowVersion:company.rowVersion});this.notify.ok('Company name saved.');await this.load();await this.api.loadShell();}catch(e){this.notify.fail(e);}finally{this.busy.set(false);}}
    async saveGlobal(){this.busy.set(true);try{await this.api.put('/api/groups/settings',{settingsJson:this.globalJson,companyOverride:false,globalOverride:true,rowVersion:this.settings?.globalRowVersion});this.notify.ok('Global defaults saved.');await this.load();}catch(e){this.notify.fail(e);}finally{this.busy.set(false);}}
    async saveBilling(){this.busy.set(true);try{await this.api.put('/api/groups/billing',this.billing);this.notify.ok('Billing profile saved.');await this.load();}catch(e){this.notify.fail(e);}finally{this.busy.set(false);}}
}
