import { Component, inject, signal } from '@angular/core';

import { Api, Paged } from '@/app/core/api';

import { Notify, PageHeader, UI, date, isoDate, label, severity, time } from '@/app/core/ui';



interface ReviewRow {

    id: number;

    employeeId: number;

    employeeCode: string;

    employeeName: string;

    punchedAt: string;

    logDate: string;

    localTime: string;

    direction: string;

    source: string;

    locationStatus: string;

    latitude: number | null;

    longitude: number | null;

    accuracyMeters: number | null;

    distanceMeters: number | null;

    locationName: string | null;

    ipAddress: string | null;

    deviceId: string | null;

    platform: string | null;

    hasPhoto: boolean;

    flags: string[];

    isReviewed: boolean;

    reviewNote: string | null;

    remarks: string | null;

}



/**

 * Check-ins that need a human: outside every geo-fence, without a confirmed location, or

 * flagged as suspicious. HR looks at the photo and the place and records a verdict.

 */

@Component({

    standalone: true,

    imports: [...UI, PageHeader],

    template: `<app-page-header title="Punch Review" subtitle="Check-ins outside a work location or with something unusual about them.">

            <p-floatlabel variant="on"><input pInputText id="flt10" type="date" [(ngModel)]="from" (change)="reload()" /><label for="flt10">From</label></p-floatlabel>

            <p-floatlabel variant="on"><input pInputText id="flt11" type="date" [(ngModel)]="to" (change)="reload()" /><label for="flt11">To</label></p-floatlabel>

            <div class="flex items-center gap-2">

                <p-toggleswitch inputId="reviewed" [(ngModel)]="includeReviewed" (onChange)="reload()" />

                <label for="reviewed">Show reviewed</label>

            </div>

        </app-page-header>

        <div class="card">

            <p-table [value]="rows()" [loading]="loading()" [lazy]="true" [paginator]="true" [rows]="pageSize" [totalRecords]="total()" (onLazyLoad)="onLazy($event)" stripedRows [tableStyle]="{ 'min-width': '56rem' }">

                <ng-template #header>

                    <tr><th>When</th><th>Employee</th><th>Direction</th><th>Verdict</th><th>Where</th><th>Flags</th><th style="width:9rem"></th></tr>

                </ng-template>

                <ng-template #body let-r>

                    <tr>

                        <td>{{ date(r.logDate) }}<div class="text-muted-color text-sm">{{ time(r.localTime) }}</div></td>

                        <td><span class="font-medium">{{ r.employeeName }}</span><div class="text-muted-color text-sm">{{ r.employeeCode }}</div></td>

                        <td>{{ r.direction === 'In' ? 'Check-in' : 'Check-out' }}</td>

                        <td>

                            <p-tag [value]="label(r.locationStatus)" [severity]="severity(r.locationStatus)" />

                            @if (r.isReviewed) { <i class="pi pi-check-circle text-green-500 ml-2" [pTooltip]="r.reviewNote || 'Reviewed'"></i> }

                        </td>

                        <td>

                            @if (r.distanceMeters != null) {

                                {{ distance(r.distanceMeters) }} from {{ r.locationName || 'the nearest location' }}

                            } @else {

                                {{ r.locationName || 'No position' }}

                            }

                        </td>

                        <td>

                            @for (f of r.flags; track f) {

                                <p-tag [value]="label(f)" severity="secondary" styleClass="mr-1 mb-1" />

                            }

                        </td>

                        <td class="text-right whitespace-nowrap">

                            <p-button label="Review" icon="pi pi-search" size="small" [outlined]="true" (onClick)="open(r)" />

                        </td>

                    </tr>

                </ng-template>

                <ng-template #emptymessage><tr><td colspan="7" class="text-center text-muted-color py-8">Nothing to review. Every check-in was at a work location.</td></tr></ng-template>

            </p-table>

        </div>



        <p-dialog [(visible)]="dialog" [header]="current ? current.employeeName + ' · ' + date(current.logDate) + ' ' + time(current.localTime) : ''" [modal]="true" [style]="{ width: '52rem' }" [breakpoints]="{ '960px': '95vw' }">

            @if (current; as c) {

                <div class="grid grid-cols-12 gap-6 pt-2">

                    <div class="col-span-12 md:col-span-5">

                        @if (photoUrl()) {

                            <img [src]="photoUrl()" alt="Check-in photo" class="w-full rounded-border" />

                        } @else {

                            <div class="rounded-border border border-surface flex items-center justify-center text-muted-color" style="aspect-ratio:4/3">{{ c.hasPhoto ? 'Loading photo…' : 'No photo was taken' }}</div>

                        }

                    </div>

                    <div class="col-span-12 md:col-span-7">

                        <dl class="grid grid-cols-3 gap-y-2 m-0">

                            <dt class="text-muted-color">Verdict</dt><dd class="col-span-2 m-0"><p-tag [value]="label(c.locationStatus)" [severity]="severity(c.locationStatus)" /></dd>

                            <dt class="text-muted-color">Position</dt>

                            <dd class="col-span-2 m-0">

                                @if (c.latitude != null) {

                                    <a class="text-primary" target="_blank" rel="noopener" [href]="'https://www.openstreetmap.org/?mlat=' + c.latitude + '&mlon=' + c.longitude + '#map=17/' + c.latitude + '/' + c.longitude">{{ c.latitude }}, {{ c.longitude }}</a>

                                    @if (c.accuracyMeters != null) { <span class="text-muted-color"> (±{{ round(c.accuracyMeters) }} m)</span> }

                                } @else { Not sent }

                            </dd>

                            <dt class="text-muted-color">Distance</dt><dd class="col-span-2 m-0">{{ c.distanceMeters != null ? distance(c.distanceMeters) + ' from ' + (c.locationName || 'the nearest location') : '—' }}</dd>

                            <dt class="text-muted-color">Network</dt><dd class="col-span-2 m-0">{{ c.ipAddress || '—' }}</dd>

                            <dt class="text-muted-color">Device</dt><dd class="col-span-2 m-0">{{ c.platform || '—' }}</dd>

                            <dt class="text-muted-color">Flags</dt><dd class="col-span-2 m-0">{{ flagText(c) }}</dd>

                            @if (c.remarks) { <dt class="text-muted-color">Remarks</dt><dd class="col-span-2 m-0">{{ c.remarks }}</dd> }

                        </dl>

                        <div class="mt-5">

                            <p-floatlabel variant="on"><p-select [filter]="true" inputId="verdict" [options]="verdicts" optionLabel="label" optionValue="value" [(ngModel)]="newStatus" appendTo="body" fluid /><label for="verdict">Your verdict</label></p-floatlabel>

                        </div>

                        <div class="mt-4">

                            <p-floatlabel variant="on"><textarea pTextarea id="note" [(ngModel)]="note" rows="2" maxlength="500" fluid></textarea><label for="note">Note</label></p-floatlabel>

                        </div>

                    </div>

                    <div class="col-span-12 flex justify-end gap-2">

                        <p-button label="Cancel" severity="secondary" [text]="true" (onClick)="dialog = false" />

                        <p-button label="Save review" icon="pi pi-check" [loading]="saving()" (onClick)="save()" />

                    </div>

                </div>

            }

        </p-dialog>`

})

export class PunchReviewPage {

    private api = inject(Api);

    private notify = inject(Notify);

    rows = signal<ReviewRow[]>([]);

    total = signal(0);

    loading = signal(false);

    saving = signal(false);

    photoUrl = signal('');

    from = isoDate(new Date(Date.now() - 30 * 86400000));

    to = isoDate();

    includeReviewed = false;

    page = 1;

    pageSize = 20;

    dialog = false;

    current: ReviewRow | null = null;

    newStatus = '';

    note = '';



    verdicts = [

        { label: 'Accept: at a work location', value: 'InOffice' },

        { label: 'Accept: approved remote work', value: 'Remote' },

        { label: 'Keep as outside the geo-fence', value: 'OutsideGeofence' },

        { label: 'Location not confirmed', value: 'Unverified' },

        { label: 'Suspicious', value: 'Suspicious' }

    ];

    date = date;

    time = time;

    label = label;

    severity = severity;

    round = Math.round;



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

            const result = await this.api.get<Paged<ReviewRow>>('/api/attendance/review', { from: this.from, to: this.to, includeReviewed: this.includeReviewed, page: this.page, pageSize: this.pageSize });

            this.rows.set(result.items);

            this.total.set(result.totalCount);

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.loading.set(false);

        }

    }



    async open(row: ReviewRow) {

        this.current = row;

        this.newStatus = row.locationStatus;

        this.note = row.reviewNote ?? '';

        this.photoUrl.set('');

        this.dialog = true;

        if (!row.hasPhoto) return;

        try {

            this.photoUrl.set((await this.api.get<{ url: string }>(`/api/attendance/punches/${row.id}/photo`)).url);

        } catch (e) {

            this.notify.fail(e);

        }

    }



    async save() {

        if (!this.current) return;

        this.saving.set(true);

        try {

            const result = await this.api.put(`/api/attendance/review/${this.current.id}`, { newLocationStatus: this.newStatus, note: this.note || null });

            this.notify.ok(result.message);

            this.dialog = false;

            await this.load();

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.saving.set(false);

        }

    }



    distance(value: number): string {

        return value < 1000 ? `${Math.round(value)} m` : `${(value / 1000).toFixed(1)} km`;

    }



    flagText(row: ReviewRow): string {

        return row.flags.length ? row.flags.map((f) => label(f)).join(', ') : 'None';

    }

}
