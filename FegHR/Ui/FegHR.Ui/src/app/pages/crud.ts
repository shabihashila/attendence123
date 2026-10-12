import { Component, Injectable, computed, inject, signal } from '@angular/core';

import { ActivatedRoute } from '@angular/router';

import { ConfirmationService } from 'primeng/api';

import { Api, Paged, Session } from '@/app/core/api';

import { Notify, PageHeader, UI, date, label, severity, time } from '@/app/core/ui';



export interface Option {

    label: string;

    value: unknown;

}

export type LookupName = 'departments' | 'designations' | 'branches' | 'employees' | 'leaveTypes' | 'unlinkedUsers' | 'schedules';



export interface CrudField {

    key: string;

    label: string;

    type: 'text' | 'email' | 'number' | 'checkbox' | 'select' | 'date' | 'time' | 'textarea';

    required?: boolean;

    hint?: string;

    default?: unknown;

    options?: Option[];

    lookup?: LookupName;

    /** For a select whose current value may not be in the lookup: the row property holding its label. */

    currentLabelKey?: string;

    min?: number;

    max?: number;

    step?: number;

    maxLength?: number;

    wide?: boolean;

}

export interface CrudColumn {

    key: string;

    label: string;

    kind?: 'text' | 'date' | 'time' | 'bool' | 'tag' | 'label';

}

/** Describes a list-and-form page over one REST resource: GET list, POST, PUT /{id}, DELETE /{id}. */

export interface CrudConfig {

    title: string;

    subtitle: string;

    endpoint: string;

    writePermission: string;

    columns: CrudColumn[];

    fields: CrudField[];

    /** The list endpoint returns a page and accepts page, pageSize and search. */

    paged?: boolean;

    canEdit?: boolean;

    canDelete?: boolean;

    noun: string;

    /** Adds a button that fills `latitude` and `longitude` from the browser's location. */

    locate?: boolean;

    listParams?: Record<string, unknown>;

}



/** Picker contents, fetched when a form opens so they are never stale. */

@Injectable({ providedIn: 'root' })

export class Lookups {

    private api = inject(Api);



    async get(name: LookupName): Promise<Option[]> {

        switch (name) {

            case 'leaveTypes':

                return (await this.api.get<{ id: number; name: string }[]>('/api/leave/types')).map((x) => ({ label: x.name, value: x.id }));

            case 'unlinkedUsers':

                return (await this.api.get<{ id: number; name: string }[]>('/api/org/employees/unlinked-users')).map((x) => ({ label: x.name, value: x.id }));

            case 'schedules':

                return (await this.api.get<{ id: number; name: string; isActive: boolean }[]>('/api/attendance/schedules')).filter((x) => x.isActive).map((x) => ({ label: x.name, value: x.id }));

            default: {

                const all = await this.api.get<Record<string, { id: number; name: string }[]>>('/api/org/lookups');

                return (all[name] ?? []).map((x) => ({ label: x.name, value: x.id }));

            }

        }

    }

}



type Row = Record<string, unknown>;



@Component({

    standalone: true,

    imports: [...UI, PageHeader],

    template: `<app-page-header [title]="config.title" [subtitle]="config.subtitle">

            @if (canWrite()) {

                <p-button [label]="'New ' + config.noun" icon="pi pi-plus" (onClick)="open(null)" />

            }

        </app-page-header>



        <div class="card">

            <p-table

                #dt

                [value]="rows()"

                [loading]="loading()"

                [paginator]="true"

                [rows]="20"

                [rowsPerPageOptions]="[10, 20, 50]"

                [rowHover]="true"

                [showGridlines]="true"

                [globalFilterFields]="fields"

                stripedRows

                [tableStyle]="{ 'min-width': '40rem' }"

            >

                <ng-template #caption>

                    <div class="flex flex-wrap justify-between items-center gap-2">

                        <p-button label="Clear" icon="pi pi-filter-slash" [outlined]="true" (onClick)="dt.clear(); q.value = ''" />

                        <p-iconfield iconPosition="left" class="ml-auto">

                            <p-inputicon><i class="pi pi-search"></i></p-inputicon>

                            <input pInputText #q type="text" (input)="dt.filterGlobal(q.value, 'contains')" placeholder="Search keyword" aria-label="Search keyword" />

                        </p-iconfield>

                    </div>

                </ng-template>

                <ng-template #header>

                    <tr>

                        @for (c of config.columns; track c.key) {

                            <th [pSortableColumn]="c.key">

                                <div class="flex justify-between items-center gap-2">

                                    {{ c.label }}

                                    <span class="flex items-center">

                                        <p-sortIcon [field]="c.key" />

                                        <p-columnFilter [type]="c.kind === 'bool' ? 'boolean' : 'text'" [field]="c.key" display="menu" />

                                    </span>

                                </div>

                            </th>

                        }

                        <th style="width:7rem"></th>

                    </tr>

                </ng-template>

                <ng-template #body let-row>

                    <tr>

                        @for (c of config.columns; track c.key) {

                            <td>

                                @switch (c.kind) {

                                    @case ('bool') {

                                        <i class="pi" [ngClass]="row[c.key] ? 'pi-check-circle text-green-500' : 'pi-times-circle text-muted-color'"></i>

                                    }

                                    @case ('tag') {

                                        <p-tag [value]="text(row[c.key])" [severity]="severity(row[c.key])" />

                                    }

                                    @default {

                                        {{ cell(row, c) }}

                                    }

                                }

                            </td>

                        }

                        <td class="text-right whitespace-nowrap">

                            @if (canWrite() && config.canEdit !== false) {

                                <p-button icon="pi pi-pencil" [rounded]="true" [text]="true" (onClick)="open(row)" ariaLabel="Edit" />

                            }

                            @if (canWrite() && config.canDelete) {

                                <p-button icon="pi pi-trash" severity="danger" [rounded]="true" [text]="true" (onClick)="remove(row)" ariaLabel="Delete" />

                            }

                        </td>

                    </tr>

                </ng-template>

                <ng-template #emptymessage>

                    <tr>

                        <td [attr.colspan]="config.columns.length + 1" class="text-center text-muted-color py-8">Nothing here yet.</td>

                    </tr>

                </ng-template>

            </p-table>

        </div>



        <p-dialog [(visible)]="dialog" [header]="(editing ? 'Edit ' : 'New ') + config.noun" [modal]="true" [style]="{ width: '46rem' }" [breakpoints]="{ '960px': '95vw' }">

            <form class="grid grid-cols-12 gap-4 pt-2" (ngSubmit)="save()" #form="ngForm">

                @for (f of config.fields; track f.key) {

                    <div class="col-span-12" [ngClass]="f.wide || f.type === 'textarea' ? '' : 'md:col-span-6'">

                        @if (f.type === 'checkbox') {

                            <div class="flex items-center gap-2 mt-6">

                                <p-checkbox [inputId]="f.key" [binary]="true" [(ngModel)]="model[f.key]" [name]="f.key" />

                                <label [for]="f.key">{{ f.label }}</label>

                            </div>

                        } @else {

                            <p-floatlabel variant="on">

                                @switch (f.type) {

                                    @case ('select') {

                                        <p-select

                                            [inputId]="f.key"

                                            [options]="options()[f.key] || []"

                                            optionLabel="label"

                                            optionValue="value"

                                            [(ngModel)]="model[f.key]"

                                            [name]="f.key"

                                            [required]="!!f.required"

                                            [showClear]="!f.required"

                                            [filter]="true"

                                            appendTo="body"

                                            fluid

                                        />

                                    }

                                    @case ('textarea') {

                                        <textarea pTextarea [id]="f.key" [(ngModel)]="model[f.key]" [name]="f.key" [required]="!!f.required" [attr.maxlength]="f.maxLength || null" rows="3" fluid></textarea>

                                    }

                                    @default {

                                        <input

                                            pInputText

                                            [id]="f.key"

                                            [type]="f.type"

                                            [class.p-inputwrapper-filled]="f.type === 'date' || f.type === 'time'"

                                            [(ngModel)]="model[f.key]"

                                            [name]="f.key"

                                            [required]="!!f.required"

                                            [attr.min]="f.min ?? null"

                                            [attr.max]="f.max ?? null"

                                            [attr.step]="f.step ?? (f.type === 'number' ? 'any' : null)"

                                            [attr.maxlength]="f.maxLength || null"

                                            fluid

                                        />

                                    }

                                }

                                <label [for]="f.key">{{ f.label }}{{ f.required ? ' *' : '' }}</label>

                            </p-floatlabel>

                            @if (f.hint) {

                                <small class="text-muted-color">{{ f.hint }}</small>

                            }

                        }

                    </div>

                }

                @if (config.locate) {

                    <div class="col-span-12">

                        <p-button type="button" label="Use my current location" icon="pi pi-map-marker" severity="secondary" [outlined]="true" [loading]="locating()" (onClick)="locate()" />

                        @if (model['latitude'] && model['longitude']) {

                            <a class="ml-4 text-primary" target="_blank" rel="noopener" [href]="'https://www.openstreetmap.org/?mlat=' + model['latitude'] + '&mlon=' + model['longitude'] + '#map=18/' + model['latitude'] + '/' + model['longitude']">View on map</a>

                        }

                    </div>

                }

                <div class="col-span-12 flex justify-end gap-2 pt-2">

                    <p-button type="button" label="Cancel" severity="secondary" [text]="true" (onClick)="dialog = false" />

                    <p-button type="submit" label="Save" icon="pi pi-check" [loading]="saving()" [disabled]="form.invalid || saving()" />

                </div>

            </form>

        </p-dialog>`

})

export class CrudPage {

    private api = inject(Api);

    private notify = inject(Notify);

    private session = inject(Session);

    private lookups = inject(Lookups);

    private confirm = inject(ConfirmationService);

    config = inject(ActivatedRoute).snapshot.data['crud'] as CrudConfig;



    rows = signal<Row[]>([]);

    loading = signal(false);

    saving = signal(false);

    locating = signal(false);

    options = signal<Record<string, Option[]>>({});

    canWrite = computed(() => this.session.can(this.config.writePermission));

    severity = severity;



    /** Every column takes part in the keyword search. */

    fields = this.config.columns.map((c) => c.key);

    dialog = false;

    editing: Row | null = null;

    model: Row = {};



    constructor() {

        void this.load();

    }



    async load() {

        this.loading.set(true);

        try {

            if (this.config.paged) {

                // The table sorts and filters in the browser, so a paged resource is read to the end.

                const all: Row[] = [];

                for (let page = 1; page <= 50; page++) {

                    const result = await this.api.get<Paged<Row>>(this.config.endpoint, { ...this.config.listParams, page, pageSize: 200 });

                    all.push(...result.items);

                    if (all.length >= result.totalCount || !result.items.length) break;

                }

                this.rows.set(all);

            } else {

                this.rows.set(await this.api.get<Row[]>(this.config.endpoint, this.config.listParams));

            }

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.loading.set(false);

        }

    }



    async open(row: Row | null) {

        this.editing = row;

        this.model = {};

        for (const f of this.config.fields) {

            const value = row ? row[f.key] : (f.default ?? (f.type === 'checkbox' ? false : null));

            this.model[f.key] = f.type === 'time' && typeof value === 'string' ? value.slice(0, 5) : value;

        }

        this.dialog = true;

        try {

            const loaded: Record<string, Option[]> = {};

            for (const f of this.config.fields.filter((x) => x.type === 'select')) {

                const list = f.options ?? (f.lookup ? await this.lookups.get(f.lookup) : []);

                const current = row?.[f.key];

                // The row's own value stays selectable even when the lookup no longer offers it.

                loaded[f.key] = f.currentLabelKey && current != null && !list.some((o) => o.value === current) ? [{ label: String(row?.[f.currentLabelKey] ?? current), value: current }, ...list] : list;

            }

            this.options.set(loaded);

        } catch (e) {

            this.notify.fail(e);

        }

    }



    async save() {

        const body: Row = { rowVersion: this.editing?.['rowVersion'] ?? null };

        for (const f of this.config.fields) {

            let value = this.model[f.key];

            if (typeof value === 'string') value = value.trim() === '' ? null : value.trim();

            if (f.type === 'number' && value != null) value = Number(value);

            if (f.type === 'time' && typeof value === 'string' && value.length === 5) value += ':00';

            if (f.type === 'checkbox') value = !!value;

            body[f.key] = value;

        }

        this.saving.set(true);

        try {

            const result = this.editing ? await this.api.put(`${this.config.endpoint}/${this.editing['id']}`, body) : await this.api.post(this.config.endpoint, body);

            this.notify.ok(result.message);

            this.dialog = false;

            await this.load();

        } catch (e) {

            this.notify.fail(e);

        } finally {

            this.saving.set(false);

        }

    }



    remove(row: Row) {

        this.confirm.confirm({

            header: `Delete ${this.config.noun}`,

            message: 'This cannot be undone. Continue?',

            icon: 'pi pi-exclamation-triangle',

            acceptButtonProps: { label: 'Delete', severity: 'danger' },

            rejectButtonProps: { label: 'Cancel', severity: 'secondary', text: true },

            accept: async () => {

                try {

                    this.notify.ok((await this.api.delete(`${this.config.endpoint}/${row['id']}`)).message);

                    await this.load();

                } catch (e) {

                    this.notify.fail(e);

                }

            }

        });

    }



    locate() {

        if (!navigator.geolocation) return this.notify.info('This browser cannot report a location.');

        this.locating.set(true);

        navigator.geolocation.getCurrentPosition(

            (position) => {

                this.model['latitude'] = Number(position.coords.latitude.toFixed(6));

                this.model['longitude'] = Number(position.coords.longitude.toFixed(6));

                this.locating.set(false);

                this.notify.info(`Location read with about ${Math.round(position.coords.accuracy)} m accuracy.`);

            },

            () => {

                this.locating.set(false);

                this.notify.info('Location access was denied or is unavailable.');

            },

            { enableHighAccuracy: true, timeout: 15000 }

        );

    }



    text(value: unknown): string {

        return label(value == null ? null : String(value));

    }



    cell(row: Row, column: CrudColumn): string {

        const value = row[column.key];

        if (value == null || value === '') return '—';

        switch (column.kind) {

            case 'date':

                return date(String(value));

            case 'time':

                return time(String(value));

            case 'label':

                return label(String(value));

            default:

                return String(value);

        }

    }

}
