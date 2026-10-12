import { Component, inject, signal } from '@angular/core';

import { Api } from '@/app/core/api';

import { Notify, PageHeader, UI } from '@/app/core/ui';



interface Settings {

    requireSelfie: boolean;

    requireLocation: boolean;

    geofenceMode: string;

    maxAccuracyMeters: number;

    debounceSeconds: number;

    minInToOutMinutes: number;

    rowVersion: string | null;

}



/** The company's rules for a check-in: is a photo needed, is a position needed, and what happens outside the geo-fence. */

@Component({

    standalone: true,

    imports: [...UI, PageHeader],

    template: `<app-page-header title="Attendance Rules" subtitle="What an employee must provide to check in, and how strictly the geo-fence is applied." />

        @if (model(); as m) {

            <form class="card flex flex-col gap-6" style="max-width:52rem" (ngSubmit)="save()">

                <div>

                    <h2 class="text-lg font-semibold mt-0 mb-3">Geo-fence</h2>

                    <div class="flex flex-col gap-3">

                        @for (o of modes; track o.value) {

                            <label class="flex items-start gap-3 rounded-border border border-surface p-4 cursor-pointer" [class.border-primary]="m.geofenceMode === o.value">

                                <input type="radio" name="mode" class="mt-1" [value]="o.value" [(ngModel)]="m.geofenceMode" />

                                <span><span class="font-semibold block">{{ o.label }}</span><span class="text-muted-color">{{ o.text }}</span></span>

                            </label>

                        }

                    </div>

                </div>

                <div class="grid grid-cols-12 gap-6">

                    <div class="col-span-12 md:col-span-6 flex items-center justify-between gap-4 rounded-border border border-surface p-4">

                        <label for="requireLocation"><span class="font-semibold block">Location is required</span><span class="text-muted-color">A check-in without a GPS position is refused.</span></label>

                        <p-toggleswitch inputId="requireLocation" name="requireLocation" [(ngModel)]="m.requireLocation" />

                    </div>

                    <div class="col-span-12 md:col-span-6 flex items-center justify-between gap-4 rounded-border border border-surface p-4">

                        <label for="requireSelfie"><span class="font-semibold block">Photo is required</span><span class="text-muted-color">The employee takes a photo with every check-in.</span></label>

                        <p-toggleswitch inputId="requireSelfie" name="requireSelfie" [(ngModel)]="m.requireSelfie" />

                    </div>

                    <div class="col-span-12 md:col-span-4">

                        <p-floatlabel variant="on"><input pInputText id="accuracy" name="accuracy" type="number" min="10" max="5000" [(ngModel)]="m.maxAccuracyMeters" required fluid /><label for="accuracy">Worst acceptable accuracy (m)</label></p-floatlabel>

                        <small class="text-muted-color">A coarser fix is flagged for review.</small>

                    </div>

                    <div class="col-span-12 md:col-span-4">

                        <p-floatlabel variant="on"><input pInputText id="gap" name="gap" type="number" min="0" max="240" [(ngModel)]="m.minInToOutMinutes" required fluid /><label for="gap">Minutes before a check-out</label></p-floatlabel>

                        <small class="text-muted-color">A second tap sooner than this stays a check-in.</small>

                    </div>

                    <div class="col-span-12 md:col-span-4">

                        <p-floatlabel variant="on"><input pInputText id="debounce" name="debounce" type="number" min="0" max="600" [(ngModel)]="m.debounceSeconds" required fluid /><label for="debounce">Ignore repeats within (s)</label></p-floatlabel>

                        <small class="text-muted-color">Double taps and retries count once.</small>

                    </div>

                </div>

                <div><p-button type="submit" label="Save rules" icon="pi pi-check" [loading]="saving()" /></div>

            </form>

        }`

})

export class AttendanceSettingsPage {

    private api = inject(Api);

    private notify = inject(Notify);

    model = signal<Settings | null>(null);

    saving = signal(false);



    modes = [

        { value: 'Enforce', label: 'Enforce', text: 'A check-in outside every work location is refused. The employee is told how far away they are.' },

        { value: 'Flag', label: 'Flag for review', text: 'The check-in is recorded and sent to Punch Review for HR to accept or reject.' },

        { value: 'Off', label: 'Off', text: 'The position is recorded with the check-in but not judged.' }

    ];



    constructor() {

        this.api

            .get<Settings>('/api/attendance/settings')

            .then((s) => this.model.set(s))

            .catch((e) => this.notify.fail(e));

    }



    async save() {

        const m = this.model();

        if (!m) return;

        this.saving.set(true);

        try {

            const result = await this.api.put<Settings>('/api/attendance/settings', { ...m, maxAccuracyMeters: Number(m.maxAccuracyMeters), debounceSeconds: Number(m.debounceSeconds), minInToOutMinutes: Number(m.minInToOutMinutes) });

            this.model.set(result.data);

            this.notify.ok(result.message);

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.saving.set(false);

        }

    }

}
