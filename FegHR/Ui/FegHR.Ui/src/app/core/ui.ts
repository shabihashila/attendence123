import { CommonModule } from '@angular/common';

import { Component, Directive, Injectable, Input, inject } from '@angular/core';

import { FormsModule } from '@angular/forms';

import { MessageService } from 'primeng/api';

import { ButtonModule } from 'primeng/button';

import { CheckboxModule } from 'primeng/checkbox';

import { DialogModule } from 'primeng/dialog';

import { FloatLabelModule } from 'primeng/floatlabel';

import { IconFieldModule } from 'primeng/iconfield';

import { InputIconModule } from 'primeng/inputicon';

import { InputNumberModule } from 'primeng/inputnumber';

import { InputTextModule } from 'primeng/inputtext';

import { MessageModule } from 'primeng/message';

import { SelectModule } from 'primeng/select';

import { TableModule } from 'primeng/table';

import { TagModule } from 'primeng/tag';

import { TextareaModule } from 'primeng/textarea';

import { ToggleSwitchModule } from 'primeng/toggleswitch';

import { TooltipModule } from 'primeng/tooltip';

import { ApiError, NameSegment } from './api';



/**

 * A date, time or month input always shows something (its format), so its floating label must

 * stay up, exactly as if the field were filled.

 */

@Directive({ selector: 'input[type=date],input[type=time],input[type=month]', standalone: true, host: { class: 'p-inputwrapper-filled' } })

export class AlwaysFilled {}



/** The PrimeNG pieces nearly every page uses. */

export const UI = [

    AlwaysFilled,

    FloatLabelModule,

    IconFieldModule,

    InputIconModule,

    CommonModule,

    FormsModule,

    ButtonModule,

    CheckboxModule,

    DialogModule,

    InputNumberModule,

    InputTextModule,

    MessageModule,

    SelectModule,

    TableModule,

    TagModule,

    TextareaModule,

    ToggleSwitchModule,

    TooltipModule

] as const;



/** Toasts, with the server's own wording for failures. */

@Injectable({ providedIn: 'root' })

export class Notify {

    private messages = inject(MessageService);



    ok(text: string | null | undefined) {

        this.messages.add({ severity: 'success', summary: 'Done', detail: text || 'Saved.', life: 3500 });

    }

    info(text: string) {

        this.messages.add({ severity: 'info', summary: 'Note', detail: text, life: 5000 });

    }

    fail(error: unknown) {

        const status = error instanceof ApiError ? error.status : 0;

        // A refusal the user can act on (422, 409, 400) is a warning; anything else is an error.

        this.messages.add({

            severity: status >= 400 && status < 500 && status !== 401 ? 'warn' : 'error',

            summary: status === 403 ? 'Not allowed' : 'Could not complete',

            detail: describe(error),

            life: 7000

        });

    }

}



export function describe(error: unknown): string {

    if (error instanceof ApiError) {

        const fields = Object.values(error.errors ?? {}).flat();

        return fields.length ? `${error.message} ${fields.join(' ')}` : error.message;

    }

    return error instanceof Error ? error.message : 'Something went wrong. Please try again.';

}



/** "09:05:00" or "09:05" from the API, shown as 9:05 AM. */

export function time(value: string | null | undefined): string {

    if (!value) return '—';

    const [h, m] = value.split(':').map(Number);

    if (Number.isNaN(h) || Number.isNaN(m)) return value;

    return `${h % 12 === 0 ? 12 : h % 12}:${String(m).padStart(2, '0')} ${h < 12 ? 'AM' : 'PM'}`;

}



/** "2026-10-12" or an ISO date-time, shown as 12 Oct 2026. */

export function date(value: string | null | undefined): string {

    if (!value) return '—';

    const [y, m, d] = value.slice(0, 10).split('-').map(Number);

    return new Date(y, m - 1, d).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });

}



export function dateTime(value: string | null | undefined): string {

    return value ? new Date(value).toLocaleString('en-GB', { day: '2-digit', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit', hour12: true }) : '—';

}



export function minutes(value: number | null | undefined): string {

    return value == null ? '—' : `${Math.floor(value / 60)}h ${String(value % 60).padStart(2, '0')}m`;

}



/** Today in the browser's zone as yyyy-MM-dd, for date inputs. */

export function isoDate(d = new Date()): string {

    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

}



type Severity = 'success' | 'info' | 'warn' | 'danger' | 'secondary' | 'contrast';

const SEVERITIES: Record<string, Severity> = {

    Present: 'success', Late: 'warn', Absent: 'danger', Leave: 'info', Holiday: 'secondary', WeekOff: 'secondary', Upcoming: 'secondary',

    InOffice: 'success', Remote: 'info', OutsideGeofence: 'warn', Unverified: 'warn', Suspicious: 'danger',

    Pending: 'warn', Approved: 'success', Rejected: 'danger', Cancelled: 'secondary',

    Active: 'success', Suspended: 'warn', Closed: 'danger', Revoked: 'danger', Trial: 'info', Paid: 'success', Complimentary: 'contrast', Owner: 'contrast'

};

export function severity(status: string | null | undefined): Severity {

    return SEVERITIES[status ?? ''] ?? 'secondary';

}



const LABELS: Record<string, string> = {

    WeekOff: 'Week off', InOffice: 'At work location', OutsideGeofence: 'Outside geofence', RemoteAllowed: 'Remote allowed',

    HalfFirst: 'First half', HalfSecond: 'Second half', Full: 'Full day',

    OUTSIDE_ALL_FENCES: 'Outside every work location', NO_LOCATION: 'No location sent', NO_LOCATION_CONFIGURED: 'No work location configured',

    ACCURACY_TOO_COARSE: 'Location too imprecise', MOCK_LOCATION: 'Simulated location', UNKNOWN_DEVICE: 'Unknown device',

    UNAPPROVED_DEVICE: 'Unapproved device', IMPOSSIBLE_TRAVEL: 'Impossible travel', IP_MATCHED: 'Matched by office network'

};

export function label(code: string | null | undefined): string {

    return code ? (LABELS[code] ?? code) : '—';

}



/** Font keys of a styled name run, mapped to stacks the app ships with. */

export const FONTS: Record<string, string> = {

    sans: 'Inter, "Segoe UI", system-ui, sans-serif',

    display: '"Trebuchet MS", "Segoe UI Black", Impact, sans-serif',

    serif: 'Georgia, "Times New Roman", serif',

    script: '"Segoe Script", "Brush Script MT", cursive',

    mono: 'Consolas, "Courier New", monospace'

};



/** True for a grey that is nearly black or nearly white (#RRGGBB or rgb()). */

export function isNeutral(value: string): boolean {

    const hex = value.match(/^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i);

    const rgb = value.match(/^rgb\(\s*(\d+),\s*(\d+),\s*(\d+)\s*\)$/);

    const parts = hex ? hex.slice(1).map((v) => parseInt(v, 16)) : rgb ? rgb.slice(1).map(Number) : null;

    if (!parts) return false;

    const [r, g, b] = parts;

    return Math.max(r, g, b) - Math.min(r, g, b) <= 24 && (Math.max(r, g, b) <= 72 || Math.min(r, g, b) >= 216);

}



/** A brand or company name with per-run styling. The runs are data and are bound, never rendered as HTML. */

@Component({

    selector: 'app-styled-name',

    standalone: true,

    imports: [CommonModule],

    template: `@if (runs?.length) {

            @for (run of runs; track $index) {

                <span

                    [style.color]="colour(run)"

                    [style.background-color]="run.background || null"

                    [style.font-weight]="run.bold ? 700 : null"

                    [style.font-style]="run.italic ? 'italic' : null"

                    [style.text-decoration]="decoration(run)"

                    [style.font-family]="family(run)"

                    [style.font-size.px]="run.fontSize || null"

                    >{{ run.text }}</span

                >

            }

        } @else {

            <span>{{ fallback }}</span>

        }`,

    styles: [':host{white-space:pre-wrap;overflow-wrap:anywhere}']

})

export class StyledName {

    @Input() runs: NameSegment[] | null | undefined;

    @Input() fallback = '';



    /**

     * Near-black and near-white text follows the theme, so a name written in black stays

     * readable in dark mode (and white in light mode). Accent colours and highlighted text

     * are left exactly as chosen.

     */

    colour(run: NameSegment) {

        if (!run.color) return null;

        return !run.background && isNeutral(run.color) ? 'var(--brand-neutral-color)' : run.color;

    }

    family(run: NameSegment) {

        return run.font ? (FONTS[run.font] ?? null) : null;

    }

    decoration(run: NameSegment) {

        return [run.underline ? 'underline' : '', run.strike ? 'line-through' : ''].filter(Boolean).join(' ') || null;

    }

}



/** Page title block used at the top of every page. */

@Component({

    selector: 'app-page-header',

    standalone: true,

    template: `<div class="flex flex-wrap items-start justify-between gap-4 mb-6">

        <div>

            <h1 class="text-2xl font-semibold m-0 text-surface-900 dark:text-surface-0">{{ title }}</h1>

            @if (subtitle) {

                <p class="text-muted-color mt-1 mb-0">{{ subtitle }}</p>

            }

        </div>

        <div class="flex flex-wrap items-center gap-2"><ng-content></ng-content></div>

    </div>`

})

export class PageHeader {

    @Input() title = '';

    @Input() subtitle = '';

}
