import { Component, ElementRef, OnDestroy, ViewChild, computed, inject, signal } from '@angular/core';

import { Api } from '@/app/core/api';

import { Notify, PageHeader, UI, date, isoDate, label, minutes, severity, time } from '@/app/core/ui';



interface Fence {

    id: number;

    name: string;

    locationType: string;

    latitude: number | null;

    longitude: number | null;

    radiusMeters: number;

}

interface PunchDto {

    id: number;

    punchedAt: string;

    localTime: string;

    direction: string;

    locationStatus: string;

    locationName: string | null;

    distanceMeters: number | null;

    flags: string[];

    hasPhoto: boolean;

    needsReview: boolean;

}

interface Today {

    date: string;

    employeeName: string;

    employeeCode: string;

    nextDirection: string;

    inTime: string | null;

    outTime: string | null;

    isWorkingDay: boolean;

    shiftStart: string | null;

    shiftEnd: string | null;

    scheduleName: string | null;

    holiday: string | null;

    rules: { requireSelfie: boolean; requireLocation: boolean; geofenceMode: string; maxAccuracyMeters: number };

    locations: Fence[];

    punches: PunchDto[];

}

interface DayRow {

    logDate: string;

    inTime: string | null;

    outTime: string | null;

    workedMinutes: number | null;

    status: string;

    note: string | null;

}

interface Fix {

    latitude: number;

    longitude: number;

    accuracy: number;

}



const DEVICE_KEY = 'feghr.device';



/**

 * Self-service attendance. The browser supplies two observations, the position and a photo

 * taken now with the camera; the server decides everything else (the time, check-in or

 * check-out, and whether the position is inside a work location).

 */

@Component({

    standalone: true,

    imports: [...UI, PageHeader],

    template: `<app-page-header title="My Attendance" [subtitle]="today() ? today()!.employeeName + ' · ' + today()!.employeeCode + ' · ' + date(today()!.date) : ''" />



        @if (notLinked()) {

            <p-message severity="warn" styleClass="w-full" [text]="notLinked()" />

        }



        @if (today(); as t) {

            <div class="grid grid-cols-12 gap-6">

                <div class="col-span-12 xl:col-span-7">

                    <div class="card !mb-0">

                        <div class="flex flex-wrap items-center justify-between gap-3 mb-5">

                            <div>

                                <h2 class="text-xl font-semibold m-0">{{ t.nextDirection === 'In' ? 'Check in' : 'Check out' }}</h2>

                                <span class="text-muted-color">

                                    @if (t.holiday) {

                                        Holiday: {{ t.holiday }}

                                    } @else if (!t.isWorkingDay) {

                                        Week off today

                                    } @else {

                                        {{ t.scheduleName || 'Shift' }} · {{ time(t.shiftStart) }} – {{ time(t.shiftEnd) }}

                                    }

                                </span>

                            </div>

                            <div class="flex gap-6 text-center">

                                <div><div class="text-muted-color text-sm">In</div><div class="text-xl font-semibold">{{ time(t.inTime) }}</div></div>

                                <div><div class="text-muted-color text-sm">Out</div><div class="text-xl font-semibold">{{ time(t.outTime) }}</div></div>

                            </div>

                        </div>



                        <!-- 1. Position -->

                        <div class="rounded-border border border-surface p-4 mb-4">

                            <div class="flex flex-wrap items-center justify-between gap-3">

                                <div class="flex items-center gap-3">

                                    <i class="pi pi-map-marker text-2xl" [ngClass]="place().ok ? 'text-green-500' : 'text-amber-500'"></i>

                                    <div>

                                        <div class="font-semibold">{{ place().title }}</div>

                                        <div class="text-muted-color text-sm">{{ place().detail }}</div>

                                    </div>

                                </div>

                                <p-button label="Refresh location" icon="pi pi-refresh" severity="secondary" [outlined]="true" size="small" [loading]="locating()" (onClick)="locate()" />

                            </div>

                        </div>



                        <!-- 2. Photo -->

                        <div class="rounded-border border border-surface p-4 mb-4">

                            <div class="flex flex-wrap items-center justify-between gap-3 mb-3">

                                <div class="flex items-center gap-3">

                                    <i class="pi pi-camera text-2xl" [ngClass]="photoUrl() ? 'text-green-500' : 'text-amber-500'"></i>

                                    <div>

                                        <div class="font-semibold">{{ photoUrl() ? 'Photo ready' : t.rules.requireSelfie ? 'Take a photo of yourself' : 'Photo (optional)' }}</div>

                                        <div class="text-muted-color text-sm">The photo is stored with this check-in so HR can confirm it was you.</div>

                                    </div>

                                </div>

                                <div class="flex gap-2">

                                    @if (photoUrl()) {

                                        <p-button label="Retake" icon="pi pi-refresh" severity="secondary" [outlined]="true" size="small" (onClick)="startCamera()" />

                                    } @else if (!cameraOn()) {

                                        <p-button label="Open camera" icon="pi pi-video" size="small" (onClick)="startCamera()" />

                                    }

                                </div>

                            </div>

                            <div class="flex justify-center">

                                <video #video autoplay playsinline muted class="rounded-border bg-surface-900" [hidden]="!cameraOn()" style="width:100%;max-width:24rem;aspect-ratio:4/3;object-fit:cover;transform:scaleX(-1)"></video>

                                @if (photoUrl() && !cameraOn()) {

                                    <img [src]="photoUrl()" alt="Your photo" class="rounded-border" style="width:100%;max-width:24rem;aspect-ratio:4/3;object-fit:cover" />

                                }

                            </div>

                            @if (cameraOn()) {

                                <div class="flex justify-center mt-3">

                                    <p-button label="Take photo" icon="pi pi-camera" (onClick)="capture()" />

                                </div>

                            }

                            @if (cameraError()) {

                                <p-message severity="warn" styleClass="w-full mt-3" [text]="cameraError()" />

                                <label class="p-button p-button-secondary p-button-outlined mt-3 cursor-pointer inline-flex">

                                    <i class="pi pi-upload mr-2"></i>Use the device camera app

                                    <input type="file" accept="image/*" capture="user" hidden (change)="pick($event)" />

                                </label>

                            }

                        </div>



                        <p-button

                            [label]="t.nextDirection === 'In' ? 'Check in now' : 'Check out now'"

                            [icon]="t.nextDirection === 'In' ? 'pi pi-sign-in' : 'pi pi-sign-out'"

                            size="large"

                            styleClass="w-full"

                            [loading]="punching()"

                            [disabled]="!ready() || punching()"

                            (onClick)="punch()"

                        />

                        @if (blocker()) {

                            <p class="text-muted-color text-sm text-center mt-3 mb-0">{{ blocker() }}</p>

                        }

                    </div>

                </div>



                <div class="col-span-12 xl:col-span-5">

                    <div class="card !mb-0 h-full">

                        <h2 class="text-xl font-semibold mt-0 mb-4">Today’s check-ins</h2>

                        @if (!t.punches.length) {

                            <p class="text-muted-color">Nothing recorded yet today.</p>

                        }

                        <ul class="list-none p-0 m-0 flex flex-col gap-4">

                            @for (p of t.punches; track p.id) {

                                <li class="flex items-start justify-between gap-3">

                                    <div class="flex items-start gap-3">

                                        <span class="inline-flex items-center justify-center rounded-full bg-primary text-primary-contrast" style="width:2.25rem;height:2.25rem"><i class="pi" [ngClass]="p.direction === 'In' ? 'pi-sign-in' : 'pi-sign-out'"></i></span>

                                        <div>

                                            <div class="font-semibold">{{ p.direction === 'In' ? 'Checked in' : 'Checked out' }} · {{ time(p.localTime) }}</div>

                                            <div class="text-muted-color text-sm">{{ p.locationName || label(p.locationStatus) }}@if (p.distanceMeters != null) { · {{ distance(p.distanceMeters) }} away }</div>

                                            <p-tag [value]="label(p.locationStatus)" [severity]="severity(p.locationStatus)" styleClass="mt-1" />

                                            @if (p.needsReview) {

                                                <p-tag value="HR will review" severity="warn" styleClass="mt-1 ml-1" />

                                            }

                                        </div>

                                    </div>

                                    @if (p.hasPhoto) {

                                        <p-button icon="pi pi-image" [rounded]="true" [text]="true" (onClick)="showPhoto(p.id)" pTooltip="View photo" ariaLabel="View photo" />

                                    }

                                </li>

                            }

                        </ul>

                    </div>

                </div>



                <div class="col-span-12">

                    <div class="card !mb-0">

                        <div class="flex flex-wrap items-center justify-between gap-3 mb-4">

                            <h2 class="text-xl font-semibold m-0">My attendance</h2>

                            <p-floatlabel variant="on"><input pInputText id="flt3" type="month" [(ngModel)]="month" (change)="loadDays()" /><label for="flt3">Month</label></p-floatlabel>

                        </div>

                        <p-table #dtDays [rowHover]="true" [showGridlines]="true" [globalFilterFields]="['logDate', 'status', 'note']" [value]="days()" stripedRows [tableStyle]="{ 'min-width': '36rem' }">

                            <ng-template #caption>

                    <div class="flex flex-wrap justify-between items-center gap-2">

                        <p-button label="Clear" icon="pi pi-filter-slash" [outlined]="true" (onClick)="dtDays.clear(); dtDaysq.value = ''" />

                        <p-iconfield iconPosition="left" class="ml-auto">

                            <p-inputicon><i class="pi pi-search"></i></p-inputicon>

                            <input pInputText #dtDaysq type="text" (input)="dtDays.filterGlobal(dtDaysq.value, 'contains')" placeholder="Search keyword" aria-label="Search keyword" />

                        </p-iconfield>

                    </div>

                </ng-template>

                <ng-template #header>

                                <tr><th pSortableColumn="logDate"><div class="flex justify-between items-center gap-2">Date<span class="flex items-center"><p-sortIcon field="logDate" /><p-columnFilter type="text" field="logDate" display="menu" /></span></div></th><th pSortableColumn="inTime"><div class="flex justify-between items-center gap-2">In<span class="flex items-center"><p-sortIcon field="inTime" /><p-columnFilter type="text" field="inTime" display="menu" /></span></div></th><th pSortableColumn="outTime"><div class="flex justify-between items-center gap-2">Out<span class="flex items-center"><p-sortIcon field="outTime" /><p-columnFilter type="text" field="outTime" display="menu" /></span></div></th><th pSortableColumn="workedMinutes"><div class="flex justify-between items-center gap-2">Worked<span class="flex items-center"><p-sortIcon field="workedMinutes" /><p-columnFilter type="numeric" field="workedMinutes" display="menu" /></span></div></th><th pSortableColumn="status"><div class="flex justify-between items-center gap-2">Status<span class="flex items-center"><p-sortIcon field="status" /><p-columnFilter type="text" field="status" display="menu" /></span></div></th><th pSortableColumn="note"><div class="flex justify-between items-center gap-2">Note<span class="flex items-center"><p-sortIcon field="note" /><p-columnFilter type="text" field="note" display="menu" /></span></div></th></tr>

                            </ng-template>

                            <ng-template #body let-d>

                                <tr>

                                    <td>{{ date(d.logDate) }}</td>

                                    <td>{{ time(d.inTime) }}</td>

                                    <td>{{ time(d.outTime) }}</td>

                                    <td>{{ minutes(d.workedMinutes) }}</td>

                                    <td><p-tag [value]="label(d.status)" [severity]="severity(d.status)" /></td>

                                    <td>{{ d.note || '' }}</td>

                                </tr>

                            </ng-template>

                        </p-table>

                    </div>

                </div>

            </div>

        }



        <p-dialog [(visible)]="photoDialog" header="Check-in photo" [modal]="true" [style]="{ width: '30rem' }" [breakpoints]="{ '640px': '95vw' }">

            @if (viewUrl()) {

                <img [src]="viewUrl()" alt="Check-in photo" class="w-full rounded-border" />

            }

        </p-dialog>`

})

export class MyAttendancePage implements OnDestroy {

    private api = inject(Api);

    private notify = inject(Notify);

    @ViewChild('video') private video?: ElementRef<HTMLVideoElement>;



    today = signal<Today | null>(null);

    days = signal<DayRow[]>([]);

    notLinked = signal('');

    fix = signal<Fix | null>(null);

    locationError = signal('');

    locating = signal(false);

    cameraOn = signal(false);

    cameraError = signal('');

    photoUrl = signal('');

    punching = signal(false);

    viewUrl = signal('');

    photoDialog = false;

    month = isoDate().slice(0, 7);



    private stream: MediaStream | null = null;

    private photo: Blob | null = null;



    date = date;

    time = time;

    label = label;

    severity = severity;

    minutes = minutes;



    /** Where the position falls among the company's work locations, for the employee's own guidance. */

    place = computed(() => {

        const fix = this.fix();

        const t = this.today();

        if (!fix) return { ok: false, title: this.locating() ? 'Finding your location…' : 'Location not available', detail: this.locationError() || 'Allow location access for this site, then refresh.' };

        const fences = (t?.locations ?? []).filter((f) => f.latitude != null && f.longitude != null);

        const accuracy = `Accuracy about ${Math.round(fix.accuracy)} m`;

        if (!fences.length) return { ok: true, title: 'Location found', detail: `${accuracy}. No work location is set up yet, so HR will confirm this check-in.` };

        const nearest = fences.map((f) => ({ f, d: metres(fix.latitude, fix.longitude, f.latitude!, f.longitude!) })).sort((a, b) => a.d - b.d)[0];

        const inside = fences.map((f) => ({ f, d: metres(fix.latitude, fix.longitude, f.latitude!, f.longitude!) })).filter((x) => x.d <= x.f.radiusMeters).sort((a, b) => a.d - b.d)[0];

        if (inside) return { ok: true, title: `You are at ${inside.f.name}`, detail: `${this.distance(inside.d)} from its centre. ${accuracy}.` };

        return {

            ok: t?.rules.geofenceMode !== 'Enforce',

            title: `${this.distance(nearest.d)} from ${nearest.f.name}`,

            detail: t?.rules.geofenceMode === 'Enforce' ? `Attendance can only be recorded within ${nearest.f.radiusMeters} m of a work location. ${accuracy}.` : `Outside every work location, so HR will review this check-in. ${accuracy}.`

        };

    });



    blocker = computed(() => {

        const t = this.today();

        if (!t) return '';

        if (t.rules.requireLocation && !this.fix()) return 'Waiting for your location.';

        if (t.rules.requireSelfie && !this.photoUrl()) return 'Take a photo to continue.';

        return '';

    });

    ready = computed(() => !!this.today() && !this.blocker());



    constructor() {

        void this.load();

        this.locate();

        void this.loadDays();

    }



    ngOnDestroy() {

        this.stopCamera();

        if (this.photoUrl()) URL.revokeObjectURL(this.photoUrl());

    }



    async load() {

        try {

            this.today.set(await this.api.get<Today>('/api/attendance/my/today'));

            this.notLinked.set('');

        } catch (e) {

            const status = (e as { status?: number }).status;

            if (status === 409) this.notLinked.set((e as Error).message);

            else this.notify.fail(e);

        }

    }



    async loadDays() {

        const [year, month] = this.month.split('-').map(Number);

        if (!year || !month) return;

        const last = new Date(year, month, 0).getDate();

        try {

            this.days.set(await this.api.get<DayRow[]>('/api/attendance/my/days', { from: `${this.month}-01`, to: `${this.month}-${String(last).padStart(2, '0')}` }));

        } catch {

            /* The punch card above already reports why. */

        }

    }



    locate() {

        if (!navigator.geolocation) {

            this.locationError.set('This browser cannot report a location.');

            return;

        }

        this.locating.set(true);

        this.locationError.set('');

        navigator.geolocation.getCurrentPosition(

            (p) => {

                this.fix.set({ latitude: p.coords.latitude, longitude: p.coords.longitude, accuracy: p.coords.accuracy });

                this.locating.set(false);

            },

            (error) => {

                this.fix.set(null);

                this.locating.set(false);

                this.locationError.set(error.code === error.PERMISSION_DENIED ? 'Location access is blocked for this site. Allow it in the browser, then refresh.' : 'Your location could not be found. Move near a window or turn on GPS, then refresh.');

            },

            { enableHighAccuracy: true, timeout: 20000, maximumAge: 0 }

        );

    }



    async startCamera() {

        this.cameraError.set('');

        this.clearPhoto();

        try {

            this.stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user', width: { ideal: 960 }, height: { ideal: 720 } }, audio: false });

            this.cameraOn.set(true);

            // The video element is in the template already (hidden), so it can take the stream at once.

            const element = this.video?.nativeElement;

            if (element) {

                element.srcObject = this.stream;

                await element.play().catch(() => undefined);

            }

        } catch {

            this.cameraOn.set(false);

            this.cameraError.set('The camera could not be opened. Allow camera access for this site, or use the device camera app.');

        }

    }



    capture() {

        const element = this.video?.nativeElement;

        if (!element || !element.videoWidth) return;

        // 800 px on the long side keeps the upload small on a weak connection.

        const scale = Math.min(1, 800 / Math.max(element.videoWidth, element.videoHeight));

        const canvas = document.createElement('canvas');

        canvas.width = Math.round(element.videoWidth * scale);

        canvas.height = Math.round(element.videoHeight * scale);

        canvas.getContext('2d')!.drawImage(element, 0, 0, canvas.width, canvas.height);

        canvas.toBlob((blob) => blob && this.usePhoto(blob), 'image/jpeg', 0.82);

    }



    /** Fallback when the live camera is unavailable: the phone's own camera app. */

    pick(event: Event) {

        const input = event.target as HTMLInputElement;

        const file = input.files?.[0];

        input.value = '';

        if (!file) return;

        const image = new Image();

        image.onload = () => {

            const scale = Math.min(1, 800 / Math.max(image.width, image.height));

            const canvas = document.createElement('canvas');

            canvas.width = Math.round(image.width * scale);

            canvas.height = Math.round(image.height * scale);

            canvas.getContext('2d')!.drawImage(image, 0, 0, canvas.width, canvas.height);

            URL.revokeObjectURL(image.src);

            canvas.toBlob((blob) => blob && this.usePhoto(blob), 'image/jpeg', 0.82);

        };

        image.src = URL.createObjectURL(file);

    }



    async punch() {

        const t = this.today();

        if (!t || this.punching()) return;

        const form = new FormData();

        const fix = this.fix();

        if (fix) {

            form.append('latitude', fix.latitude.toFixed(6));

            form.append('longitude', fix.longitude.toFixed(6));

            form.append('accuracyMeters', String(Math.round(fix.accuracy)));

        }

        form.append('deviceId', deviceId());

        form.append('deviceLabel', navigator.userAgent.slice(0, 150));

        form.append('platform', 'Web');

        form.append('clientPunchedAt', new Date().toISOString());

        if (this.photo) form.append('photo', this.photo, 'check-in.jpg');



        this.punching.set(true);

        try {

            const result = await this.api.upload<{ punch: PunchDto; wasDuplicate: boolean; message: string }>('/api/attendance/my/punch', form);

            result.data.punch.needsReview ? this.notify.info(result.data.message) : this.notify.ok(result.data.message);

            this.clearPhoto();

            await Promise.all([this.load(), this.loadDays()]);

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.punching.set(false);

        }

    }



    async showPhoto(punchId: number) {

        this.viewUrl.set('');

        this.photoDialog = true;

        try {

            this.viewUrl.set((await this.api.get<{ url: string }>(`/api/attendance/punches/${punchId}/photo`)).url);

        } catch (e) {

            this.photoDialog = false;

            this.notify.fail(e);

        }

    }



    distance(value: number): string {

        return value < 1000 ? `${Math.round(value)} m` : `${(value / 1000).toFixed(1)} km`;

    }



    private usePhoto(blob: Blob) {

        this.clearPhoto();

        this.photo = blob;

        this.photoUrl.set(URL.createObjectURL(blob));

        this.stopCamera();

    }



    private clearPhoto() {

        if (this.photoUrl()) URL.revokeObjectURL(this.photoUrl());

        this.photo = null;

        this.photoUrl.set('');

    }



    private stopCamera() {

        this.stream?.getTracks().forEach((track) => track.stop());

        this.stream = null;

        this.cameraOn.set(false);

    }

}



/** Great-circle distance in metres (haversine). The server's verdict uses SQL Server geography. */

function metres(lat1: number, lon1: number, lat2: number, lon2: number): number {

    const rad = Math.PI / 180;

    const a = Math.sin(((lat2 - lat1) * rad) / 2) ** 2 + Math.cos(lat1 * rad) * Math.cos(lat2 * rad) * Math.sin(((lon2 - lon1) * rad) / 2) ** 2;

    return 6371000 * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));

}



/** A stable id for this browser, so HR can tell a familiar device from a new one. */

function deviceId(): string {

    try {

        let id = localStorage.getItem(DEVICE_KEY);

        if (!id) {

            id = crypto.randomUUID();

            localStorage.setItem(DEVICE_KEY, id);

        }

        return id;

    } catch {

        return '';

    }

}
