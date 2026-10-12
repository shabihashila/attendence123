import { Component, inject, signal } from '@angular/core';

import { Api, CompanyBranding, NameSegment, Session } from '@/app/core/api';

import { Branding, PlatformBranding } from '@/app/core/branding';

import { NameStyleEditor } from '@/app/core/name-editor';

import { Notify, PageHeader, UI } from '@/app/core/ui';



/** The parts to send: empty parts dropped, sizes as numbers, blank colours as null. */

function cleanRuns(parts: NameSegment[]): NameSegment[] {

    return parts

        .filter((p) => p.text)

        .map((p) => ({ text: p.text, color: p.color || null, background: p.background || null, bold: !!p.bold, italic: !!p.italic, underline: !!p.underline, strike: !!p.strike, font: p.font || null, fontSize: p.fontSize ? Number(p.fontSize) : null }));

}



function readImage(event: Event): File | null {

    const input = event.target as HTMLInputElement;

    const file = input.files?.[0] ?? null;

    input.value = '';

    return file;

}



/** The company's own identity inside the application: its name, logo, colours and contact lines. */

@Component({

    standalone: true,

    imports: [...UI, PageHeader, NameStyleEditor],

    template: `<app-page-header title="Company Branding" subtitle="How your company appears to your people in the top bar, with your own colours." />

        @if (model(); as m) {

            <form class="grid grid-cols-12 gap-6" (ngSubmit)="save()" #form="ngForm">

                <div class="col-span-12 xl:col-span-8">

                    <div class="card !mb-0">

                        <h2 class="text-lg font-semibold mt-0 mb-4">Company name</h2>

                        @defer (on immediate) { <app-name-style-editor [(parts)]="parts" [fallback]="m.name" [maxLength]="80" /> }

                        <div class="grid grid-cols-12 gap-4 mt-6">

                            <div class="col-span-12"><p-floatlabel variant="on"><input pInputText id="tagline" name="tagline" [(ngModel)]="m.tagline" maxlength="160" fluid /><label for="tagline">Tagline</label></p-floatlabel></div>

                            <div class="col-span-12 md:col-span-6"><p-floatlabel variant="on"><input pInputText id="phone" name="phone" [(ngModel)]="m.phone" maxlength="30" fluid /><label for="phone">Phone</label></p-floatlabel></div>

                            <div class="col-span-12 md:col-span-6"><p-floatlabel variant="on"><input pInputText id="email" name="email" type="email" [(ngModel)]="m.email" email maxlength="254" fluid /><label for="email">Email</label></p-floatlabel></div>

                            <div class="col-span-12"><p-floatlabel variant="on"><input pInputText id="address" name="address" [(ngModel)]="m.address" maxlength="300" fluid /><label for="address">Address</label></p-floatlabel></div>

                        </div>

                    </div>

                </div>

                <div class="col-span-12 xl:col-span-4">

                    <div class="card">

                        <h2 class="text-lg font-semibold mt-0 mb-4">Logo</h2>

                        <div class="flex items-center gap-4">

                            <img [src]="m.logoUrl || '/feg-technova-logo.png'" alt="Logo" class="rounded-border border border-surface" style="width:5rem;height:5rem;object-fit:contain" />

                            <div class="flex flex-col gap-2">

                                <label class="p-button p-button-outlined p-button-sm cursor-pointer"><i class="pi pi-upload mr-2"></i>Upload<input type="file" accept="image/png,image/jpeg,image/webp" hidden (change)="uploadLogo($event)" /></label>

                                @if (m.logoUrl) { <p-button type="button" label="Remove" severity="danger" [text]="true" size="small" (onClick)="removeLogo()" /> }

                            </div>

                        </div>

                        <small class="text-muted-color block mt-3">PNG, JPEG or WebP up to 2 MB.</small>

                    </div>

                    <div class="card !mb-0">

                        <h2 class="text-lg font-semibold mt-0 mb-4">Colours</h2>

                        <div class="flex items-center justify-between mb-3"><label for="primary">Primary</label><span class="flex items-center gap-2"><input id="primary" type="color" name="primary" [(ngModel)]="m.primaryColor" /><code>{{ m.primaryColor }}</code></span></div>

                        <div class="flex items-center justify-between"><label for="accent">Accent</label><span class="flex items-center gap-2"><input id="accent" type="color" name="accent" [(ngModel)]="m.accentColor" /><code>{{ m.accentColor }}</code></span></div>

                    </div>

                </div>

                <div class="col-span-12"><p-button type="submit" label="Save branding" icon="pi pi-check" [loading]="saving()" [disabled]="form.invalid || saving()" /></div>

            </form>

        }`

})

export class CompanyBrandingPage {

    private api = inject(Api);

    private notify = inject(Notify);

    private session = inject(Session);

    model = signal<CompanyBranding | null>(null);

    saving = signal(false);

    parts: NameSegment[] = [];



    constructor() {

        this.api

            .get<CompanyBranding>('/api/platform/branding')

            .then((b) => this.use(b))

            .catch((e) => this.notify.fail(e));

    }



    async save() {

        const m = this.model();

        if (!m) return;

        const runs = cleanRuns(this.parts);

        this.saving.set(true);

        try {

            const result = await this.api.put<CompanyBranding>('/api/platform/branding', {

                displayName: runs.map((r) => r.text).join('').trim() || m.name,

                nameStyle: runs,

                tagline: m.tagline || null,

                primaryColor: m.primaryColor,

                accentColor: m.accentColor,

                phone: m.phone || null,

                email: m.email || null,

                address: m.address || null,

                facebookUrl: null,

                instagramUrl: null,

                rowVersion: m.rowVersion

            });

            this.use(result.data);

            this.notify.ok(result.message);

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.saving.set(false);

        }

    }



    async uploadLogo(event: Event) {

        const file = readImage(event);

        if (!file) return;

        const form = new FormData();

        form.append('file', file);

        try {

            const result = await this.api.upload<CompanyBranding>('/api/platform/branding/logo', form);

            this.use(result.data, true);

            this.notify.ok(result.message);

        } catch (e) {

            this.notify.fail(e);

        }

    }



    async removeLogo() {

        try {

            const result = await this.api.delete<CompanyBranding>('/api/platform/branding/logo');

            this.use(result.data, true);

            this.notify.ok(result.message);

        } catch (e) {

            this.notify.fail(e);

        }

    }



    /** `imageOnly` keeps unsaved edits of the text fields when only the logo changed. */

    private use(b: CompanyBranding, imageOnly = false) {

        const current = this.model();

        this.model.set(imageOnly && current ? { ...current, logoUrl: b.logoUrl, rowVersion: b.rowVersion } : { ...b });

        if (!imageOnly) this.parts = b.nameStyle?.length ? b.nameStyle.map((r) => ({ ...r })) : [{ text: b.displayName || b.name, bold: true }];

        this.session.branding.set(b);

    }

}



/** The product's identity on the public pages. Product owner only. */

@Component({

    standalone: true,

    imports: [...UI, PageHeader, NameStyleEditor],

    template: `<app-page-header title="Product Branding" subtitle="The name, logo, favicon and colours shown on the landing, registration and sign-in pages." />

        @if (model(); as m) {

            <form class="grid grid-cols-12 gap-6" (ngSubmit)="save()" #form="ngForm">

                <div class="col-span-12 xl:col-span-8">

                    <div class="card !mb-0">

                        <h2 class="text-lg font-semibold mt-0 mb-4">Brand name</h2>

                        @defer (on immediate) { <app-name-style-editor [(parts)]="parts" [fallback]="m.brandName" [maxLength]="60" /> }

                        <div class="grid grid-cols-12 gap-4 mt-6">

                            <div class="col-span-12"><p-floatlabel variant="on"><input pInputText id="tagline" name="tagline" [(ngModel)]="m.tagline" maxlength="200" fluid /><label for="tagline">Tagline</label></p-floatlabel></div>

                            <div class="col-span-12 md:col-span-6"><p-floatlabel variant="on"><input pInputText id="supportEmail" name="supportEmail" type="email" [(ngModel)]="m.supportEmail" email maxlength="254" fluid /><label for="supportEmail">Support email</label></p-floatlabel></div>

                            <div class="col-span-12 md:col-span-6"><p-floatlabel variant="on"><input pInputText id="supportPhone" name="supportPhone" [(ngModel)]="m.supportPhone" maxlength="30" fluid /><label for="supportPhone">Support phone</label></p-floatlabel></div>

                            <div class="col-span-12"><p-floatlabel variant="on"><input pInputText id="footer" name="footer" [(ngModel)]="m.footerText" maxlength="300" fluid /><label for="footer">Footer text</label></p-floatlabel></div>

                        </div>

                    </div>

                </div>

                <div class="col-span-12 xl:col-span-4">

                    <div class="card">

                        <h2 class="text-lg font-semibold mt-0 mb-4">Logo and favicon</h2>

                        @for (slot of slots; track slot.key) {

                            <div class="flex items-center gap-4 mb-4">

                                <img [src]="(slot.key === 'logo' ? m.logoUrl : m.faviconUrl) || '/feg-technova-logo.png'" [alt]="slot.label" class="rounded-border border border-surface" [style.width.rem]="slot.size" [style.height.rem]="slot.size" style="object-fit:contain" />

                                <div class="flex flex-col gap-1">

                                    <span class="font-medium">{{ slot.label }}</span>

                                    <span class="flex gap-2">

                                        <label class="p-button p-button-outlined p-button-sm cursor-pointer"><i class="pi pi-upload mr-2"></i>Upload<input type="file" [accept]="slot.accept" hidden (change)="upload(slot.key, $event)" /></label>

                                        @if (slot.key === 'logo' ? m.logoUrl : m.faviconUrl) { <p-button type="button" label="Remove" severity="danger" [text]="true" size="small" (onClick)="removeImage(slot.key)" /> }

                                    </span>

                                    <small class="text-muted-color">{{ slot.hint }}</small>

                                </div>

                            </div>

                        }

                    </div>

                    <div class="card !mb-0">

                        <h2 class="text-lg font-semibold mt-0 mb-4">Colours</h2>

                        <div class="flex items-center justify-between mb-3"><label for="primary">Primary</label><span class="flex items-center gap-2"><input id="primary" type="color" name="primary" [(ngModel)]="m.primaryColor" /><code>{{ m.primaryColor }}</code></span></div>

                        <div class="flex items-center justify-between"><label for="accent">Accent</label><span class="flex items-center gap-2"><input id="accent" type="color" name="accent" [(ngModel)]="m.accentColor" /><code>{{ m.accentColor }}</code></span></div>

                    </div>

                </div>

                <div class="col-span-12"><p-button type="submit" label="Save branding" icon="pi pi-check" [loading]="saving()" [disabled]="form.invalid || saving()" /></div>

            </form>

        }`

})

export class ProductBrandingPage {

    private api = inject(Api);

    private notify = inject(Notify);

    private branding = inject(Branding);

    model = signal<PlatformBranding | null>(null);

    saving = signal(false);

    parts: NameSegment[] = [];

    slots = [

        { key: 'logo' as const, label: 'Logo', size: 5, accept: 'image/png,image/jpeg,image/webp', hint: 'PNG, JPEG or WebP up to 1 MB.' },

        { key: 'favicon' as const, label: 'Favicon', size: 2.5, accept: 'image/png,image/webp,image/x-icon,.ico', hint: 'PNG, ICO or WebP up to 256 KB.' }

    ];



    constructor() {

        this.api

            .get<PlatformBranding>('/api/owner/branding')

            .then((b) => this.use(b))

            .catch((e) => this.notify.fail(e));

    }



    async save() {

        const m = this.model();

        if (!m) return;

        const runs = cleanRuns(this.parts);

        this.saving.set(true);

        try {

            const result = await this.api.put<PlatformBranding>('/api/owner/branding', {

                brandName: runs.map((r) => r.text).join('').trim() || m.brandName,

                nameStyle: runs,

                tagline: m.tagline || null,

                primaryColor: m.primaryColor,

                accentColor: m.accentColor,

                supportEmail: m.supportEmail || null,

                supportPhone: m.supportPhone || null,

                footerText: m.footerText || null,

                rowVersion: m.rowVersion

            });

            this.use(result.data);

            this.notify.ok(result.message);

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.saving.set(false);

        }

    }



    async upload(slot: 'logo' | 'favicon', event: Event) {

        const file = readImage(event);

        if (!file) return;

        const form = new FormData();

        form.append('file', file);

        try {

            const result = await this.api.upload<PlatformBranding>(`/api/owner/branding/${slot}`, form);

            this.use(result.data, true);

            this.notify.ok(result.message);

        } catch (e) {

            this.notify.fail(e);

        }

    }



    async removeImage(slot: 'logo' | 'favicon') {

        try {

            const result = await this.api.delete<PlatformBranding>(`/api/owner/branding/${slot}`);

            this.use(result.data, true);

            this.notify.ok(result.message);

        } catch (e) {

            this.notify.fail(e);

        }

    }



    private use(b: PlatformBranding, imageOnly = false) {

        const current = this.model();

        this.model.set(imageOnly && current ? { ...current, logoUrl: b.logoUrl, faviconUrl: b.faviconUrl, rowVersion: b.rowVersion } : { ...b });

        if (!imageOnly) this.parts = b.nameStyle?.length ? b.nameStyle.map((r) => ({ ...r })) : [{ text: b.brandName, bold: true }];

        this.branding.platform.set(b);

    }

}
