import { Component, inject, signal } from '@angular/core';

import { Api, Licence, Session } from '@/app/core/api';

import { Branding } from '@/app/core/branding';

import { Notify, PageHeader, UI, date, severity } from '@/app/core/ui';



/**

 * The company's licence. This page stays reachable when the licence has lapsed: it is where a

 * user lands when the server answers 402, and it says what happened and whom to contact.

 */

@Component({

    standalone: true,

    imports: [...UI, PageHeader],

    template: `<app-page-header title="Licence" subtitle="Your company’s right to use the product." />

        @if (licence(); as l) {

            <div class="card" style="max-width:44rem">

                @if (!l.isLicensed) {

                    <p-message severity="error" styleClass="w-full mb-5" [text]="l.tenantStatus !== 'Active' ? 'This company’s access is ' + l.tenantStatus.toLowerCase() + '. Attendance, leave and the other modules are locked until it is restored.' : 'Your licence has ended. Attendance, leave and the other modules are locked until it is renewed.'" />

                } @else if (l.kind === 'Owner') {

                    <p-message severity="success" styleClass="w-full mb-5" text="This is the owner company. It needs no licence and has every feature." />

                } @else if (l.kind === 'Trial') {

                    <p-message severity="info" styleClass="w-full mb-5" [text]="'You are on a free trial with ' + (l.daysLeft ?? 0) + ' day(s) left.'" />

                }

                <dl class="grid grid-cols-3 gap-y-4 m-0">

                    <dt class="text-muted-color">Status</dt><dd class="col-span-2 m-0"><p-tag [value]="l.isLicensed ? 'Active' : 'Not licensed'" [severity]="l.isLicensed ? 'success' : 'danger'" /></dd>

                    <dt class="text-muted-color">Kind</dt><dd class="col-span-2 m-0">@if (l.kind) { <p-tag [value]="l.kind === 'Trial' ? 'Free trial' : l.kind === 'Owner' ? 'Owner company' : l.kind" [severity]="severity(l.kind)" /> } @else { — }</dd>

                    <dt class="text-muted-color">Plan</dt><dd class="col-span-2 m-0">{{ l.planName || '—' }}</dd>

                    <dt class="text-muted-color">From</dt><dd class="col-span-2 m-0">{{ date(l.startsAt) }}</dd>

                    <dt class="text-muted-color">{{ l.isLicensed ? 'Until' : 'Ended' }}</dt><dd class="col-span-2 m-0">{{ l.endsAt ? date(l.endsAt) : l.isLicensed ? 'No end date' : '—' }}</dd>

                </dl>

                <p class="text-muted-color mt-6 mb-0">

                    To renew or change your plan, contact us@if (branding.platform().supportPhone) { on {{ branding.platform().supportPhone }} }@if (branding.platform().supportEmail) { at {{ branding.platform().supportEmail }} }.

                </p>

            </div>

        }`

})

export class LicencePage {

    private api = inject(Api);

    private notify = inject(Notify);

    private session = inject(Session);

    branding = inject(Branding);

    licence = signal<Licence | null>(null);

    date = date;

    severity = severity;



    constructor() {

        this.api

            .get<Licence>('/api/platform/licence')

            .then((l) => {

                this.licence.set(l);

                this.session.licence.set(l);

            })

            .catch((e) => this.notify.fail(e));

    }

}
