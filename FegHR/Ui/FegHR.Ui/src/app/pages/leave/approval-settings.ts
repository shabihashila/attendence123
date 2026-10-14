import { Component, inject, signal } from '@angular/core';
import { Api } from '@/app/core/api';
import { Notify, PageHeader, UI } from '@/app/core/ui';
interface Policy { supervisorLevels:number; finalApproverUserId:number|null; isEnabled:boolean; rowVersion:string|null; }
@Component({ standalone:true, imports:[...UI,PageHeader], template:`<app-page-header title="Leave Approval Workflow" subtitle="Configure supervisor levels followed by a Final Approver. Existing applications keep their submitted route." />
    <form class="card flex flex-col gap-4" (ngSubmit)="save()" #form="ngForm">
        <div><p-checkbox name="enabled" inputId="enabled" [(ngModel)]="policy.isEnabled" [binary]="true" /><label for="enabled" class="ml-2">Enable sequential approvals for new applications</label></div>
        <p-floatlabel variant="on"><p-select inputId="levels" name="levels" [options]="levels" optionLabel="label" optionValue="value" [(ngModel)]="policy.supervisorLevels" /><label for="levels">Supervisor levels</label></p-floatlabel>
        <p>Each level follows Organization → Employees → Reports to: L1 Direct Supervisor, L2 Second-Level Supervisor, L3 Third-Level Supervisor, L4 Fourth-Level Supervisor, L5 Fifth-Level Supervisor.</p>
        <p-floatlabel variant="on"><p-select inputId="finalApprover" name="finalApprover" [options]="members()" optionLabel="label" optionValue="value" [(ngModel)]="policy.finalApproverUserId" [required]="policy.isEnabled" [filter]="true" appendTo="body" fluid /><label for="finalApprover">Final Approver</label></p-floatlabel>
        <p>Link every supervisor to an active login and grant the corresponding leave.approve.l1–l5 permission under Roles. Grant leave.approve.final to the Final Approver. Missing or circular supervisor chains block submission. Approvers cannot decide their own leave.</p>
        <p-button type="submit" label="Save approval workflow" [loading]="busy()" [disabled]="form.invalid || busy()" />
    </form>`})
export class ApprovalSettingsPage {
    private api=inject(Api); private notify=inject(Notify); busy=signal(false); members=signal<{label:string;value:number}[]>([]);
    policy:Policy={supervisorLevels:1,finalApproverUserId:null,isEnabled:false,rowVersion:null};
    levels=Array.from({length:6},(_,value)=>({label:value===0?'Final Approver only':`L1–L${value}, then Final Approver`,value}));
    constructor(){void this.load();}
    async load(){try{this.policy=await this.api.get<Policy>('/api/leave/approval-policy'); const members=await this.api.get<{userId:number;displayName:string;email:string;isActive:boolean}[]>('/api/iam/users');this.members.set(members.filter(m=>m.isActive).map(m=>({label:`${m.displayName} (${m.email})`,value:m.userId})));}catch(e){this.notify.fail(e);}}
    async save(){this.busy.set(true);try{await this.api.put('/api/leave/approval-policy',this.policy);this.notify.ok('Approval workflow saved.');await this.load();}catch(e){this.notify.fail(e);}finally{this.busy.set(false);}}
}
