import { Component, EventEmitter, Input, OnChanges, OnDestroy, Output } from '@angular/core';

import { FormsModule } from '@angular/forms';

import { ButtonModule } from 'primeng/button';

import { EditorModule } from 'primeng/editor';

import { FloatLabelModule } from 'primeng/floatlabel';

import { InputTextModule } from 'primeng/inputtext';

import { SelectModule } from 'primeng/select';

import Quill from 'quill';

import { NameSegment } from './api';

import { FONTS, StyledName, isNeutral } from './ui';



// Inline styles instead of Quill's classes, so any font stack and any pixel size can be applied.

const fontStyle = Quill.import('attributors/style/font') as { whitelist?: string[] };

fontStyle.whitelist = undefined;

Quill.register(fontStyle as never, true);

const sizeStyle = Quill.import('attributors/style/size') as { whitelist?: string[] };

sizeStyle.whitelist = undefined;

Quill.register(sizeStyle as never, true);



const SWATCHES = ['#111827', '#FFFFFF', '#EF4444', '#F97316', '#F59E0B', '#1863DC', '#0F9F8F', '#0EA5E9', '#2563EB', '#7C3AED', '#DB2777'];



interface Range {

    index: number;

    length: number;

}



/**

 * A brand or company name edited like text in a word processor: select any letters, a whole

 * word or several words and give that selection its own colour, highlight, weight, slant,

 * underline, strike-through, font and size. As in DailyCommerce's formatted brand name, but

 * what is saved is a list of styled runs (data), never HTML.

 */

@Component({

    selector: 'app-name-style-editor',

    standalone: true,

    imports: [FormsModule, ButtonModule, EditorModule, FloatLabelModule, InputTextModule, SelectModule, StyledName],

    template: `<div class="rounded-border border border-surface p-4 flex items-center justify-center mb-4 bg-surface-50 dark:bg-surface-800" style="min-height:5rem;font-size:1.75rem;font-weight:600">

            <app-styled-name [runs]="parts" [fallback]="fallback" />

        </div>



        <p-editor [formats]="formats" [style]="{ height: '6.5rem', fontSize: '1.5rem' }" (onInit)="initialize($event)" (onTextChange)="changed()" (onSelectionChange)="remember($event)">

            <ng-template #header>

                <span class="ql-formats">

                    <button type="button" class="ql-bold" aria-label="Bold"></button>

                    <button type="button" class="ql-italic" aria-label="Italic"></button>

                    <button type="button" class="ql-underline" aria-label="Underline"></button>

                    <button type="button" class="ql-strike" aria-label="Strike through"></button>

                    <button type="button" class="ql-clean" aria-label="Clear formatting"></button>

                </span>

            </ng-template>

        </p-editor>



        <div class="rounded-border border border-surface border-t-0 p-4 grid grid-cols-12 gap-4">

            <div class="col-span-12 lg:col-span-6">

                <div class="font-medium mb-2">Text colour</div>

                <div class="flex flex-wrap items-center gap-2">

                    @for (c of swatches; track c) {

                        <button type="button" class="swatch" [style.background]="c" (click)="apply('color', c)" [attr.aria-label]="'Text colour ' + c"></button>

                    }

                    <input type="color" [(ngModel)]="color" [ngModelOptions]="{ standalone: true }" (change)="apply('color', color)" aria-label="Custom text colour" />

                    <input pInputText [(ngModel)]="color" [ngModelOptions]="{ standalone: true }" maxlength="7" style="width:6.5rem" class="font-mono" aria-label="Text colour hex" (keyup.enter)="apply('color', color)" />

                    <p-button label="Apply" size="small" [outlined]="true" (onClick)="apply('color', color)" [disabled]="!valid(color)" />

                </div>

            </div>

            <div class="col-span-12 lg:col-span-6">

                <div class="font-medium mb-2">Highlight</div>

                <div class="flex flex-wrap items-center gap-2">

                    @for (c of swatches; track c) {

                        <button type="button" class="swatch" [style.background]="c" (click)="apply('background', c)" [attr.aria-label]="'Highlight ' + c"></button>

                    }

                    <input type="color" [(ngModel)]="background" [ngModelOptions]="{ standalone: true }" (change)="apply('background', background)" aria-label="Custom highlight colour" />

                    <p-button label="None" size="small" severity="secondary" [outlined]="true" (onClick)="apply('background', false)" />

                </div>

            </div>

            <div class="col-span-12 md:col-span-6">

                <div class="font-medium mb-2">Font</div>

                <p-select [options]="fonts" optionLabel="label" optionValue="value" [(ngModel)]="font" [ngModelOptions]="{ standalone: true }" (onChange)="apply('font', font || false)" appendTo="body" fluid ariaLabel="Font" />

            </div>

            <div class="col-span-12 md:col-span-6">

                <div class="font-medium mb-2">Size (px)</div>

                <div class="flex items-center gap-2">

                    <p-floatlabel variant="on"><input pInputText id="flt1" type="number" min="8" max="72" [(ngModel)]="size" [ngModelOptions]="{ standalone: true }" style="width:6rem" (keyup.enter)="applySize()" /><label for="flt1">Font size in pixels</label></p-floatlabel>

                    <p-button label="Apply" size="small" [outlined]="true" (onClick)="applySize()" />

                    <p-button label="Default" size="small" severity="secondary" [outlined]="true" (onClick)="apply('size', false)" />

                </div>

            </div>

            <small class="col-span-12 text-muted-color">

                Select any part of the name — a letter, a word or several words — then choose a colour, highlight, font or size for just that part. {{ length() }}/{{ maxLength }} characters.

            </small>

        </div>`,

    styles: [

        `

            :host {

                display: block;

                min-width: 0;

            }

            .swatch {

                width: 1.6rem;

                height: 1.6rem;

                border-radius: 50%;

                border: 1px solid var(--surface-border);

                cursor: pointer;

                padding: 0;

            }

            .swatch:focus-visible {

                outline: 2px solid var(--primary-color);

                outline-offset: 2px;

            }

        `

    ]

})

export class NameStyleEditor implements OnChanges, OnDestroy {

    @Input() parts: NameSegment[] = [];

    @Input() fallback = '';

    @Input() maxLength = 80;

    @Output() partsChange = new EventEmitter<NameSegment[]>();



    formats = ['bold', 'italic', 'underline', 'strike', 'color', 'background', 'font', 'size'];

    swatches = SWATCHES;

    fonts = [{ label: 'Default', value: '' }, ...Object.keys(FONTS).map((key) => ({ label: key[0].toUpperCase() + key.slice(1), value: key }))];

    color = '#1863DC';

    background = '#FDE68A';

    font = '';

    size = 28;



    private quill?: Quill;

    private selection: Range | null = null;

    private rendered?: NameSegment[];



    ngOnChanges() {

        this.render();

    }



    ngOnDestroy() {

        this.quill?.root.removeEventListener('keydown', this.singleLine);

    }



    initialize(event: { editor: Quill }) {

        this.quill = event.editor;

        this.quill.root.setAttribute('aria-label', 'Name');

        this.quill.root.addEventListener('keydown', this.singleLine);

        this.render();

    }



    length() {

        return this.parts.reduce((n, p) => n + (p.text?.length ?? 0), 0);

    }



    valid(value: string) {

        return /^#[0-9a-f]{6}$/i.test(value);

    }



    remember(event: { range: Range | null }) {

        if (event.range) this.selection = { ...event.range };

    }



    applySize() {

        const px = Math.round(Number(this.size));

        if (px >= 8 && px <= 72) this.apply('size', px + 'px');

    }



    /** Formats the selected letters; with nothing selected, the whole name. */

    apply(format: string, value: string | false) {

        if (!this.quill) return;

        if ((format === 'color' || format === 'background') && typeof value === 'string' && !this.valid(value)) return;

        const total = Math.max(0, this.quill.getLength() - 1);

        const range = this.quill.getSelection() ?? this.selection;

        const index = range && range.length ? Math.min(range.index, total) : 0;

        const length = range && range.length ? Math.min(range.length, total - index) : total;

        if (!length) return;

        this.quill.formatText(index, length, format, format === 'font' && value ? FONTS[value] : value, 'user');

        this.quill.setSelection(index, length, 'silent');

        this.selection = { index, length };

    }



    /** Quill's content, read back as runs. */

    changed() {

        if (!this.quill) return;

        this.markNeutral();

        const parts: NameSegment[] = [];

        for (const op of this.quill.getContents().ops) {

            if (typeof op.insert !== 'string') continue;

            const text = op.insert.replace(/[\r\n\t]/g, '');

            if (!text) continue;

            const a = (op.attributes ?? {}) as Record<string, unknown>;

            const size = typeof a['size'] === 'string' ? parseInt(a['size'], 10) : NaN;

            parts.push({

                text,

                color: hex(a['color']),

                background: hex(a['background']),

                bold: a['bold'] === true,

                italic: a['italic'] === true,

                underline: a['underline'] === true,

                strike: a['strike'] === true,

                font: fontKey(a['font']),

                fontSize: size >= 8 && size <= 72 ? size : null

            });

        }

        this.parts = parts;

        this.rendered = parts;

        this.partsChange.emit(parts);

    }



    /** Loads runs into Quill, unless they are the ones it just produced. */

    private render() {

        if (!this.quill || this.rendered === this.parts) return;

        this.rendered = this.parts;

        this.quill.setContents(

            this.parts.map((p) => ({

                insert: p.text,

                attributes: {

                    color: p.color || false,

                    background: p.background || false,

                    bold: !!p.bold,

                    italic: !!p.italic,

                    underline: !!p.underline,

                    strike: !!p.strike,

                    font: p.font && FONTS[p.font] ? FONTS[p.font] : false,

                    size: p.fontSize ? p.fontSize + 'px' : false

                }

            })) as never,

            'silent'

        );

        this.markNeutral();

    }



    /** Inside the editor too, near-black and near-white letters follow the theme. */

    private markNeutral() {

        this.quill?.root.querySelectorAll<HTMLElement>('[style]').forEach((e) => e.classList.toggle('brand-neutral-text', !!e.style.color && isNeutral(e.style.color) && !e.closest('[style*="background-color"]')));

    }



    // A name is one line.

    private singleLine = (e: KeyboardEvent) => {

        if (e.key === 'Enter') e.preventDefault();

    };

}



/** Quill reports colours as hex or rgb(); the API takes #RRGGBB. */

function hex(value: unknown): string | null {

    if (typeof value !== 'string') return null;

    if (/^#[0-9a-f]{6}$/i.test(value)) return value.toUpperCase();

    const rgb = value.match(/^rgb\(\s*(\d+),\s*(\d+),\s*(\d+)\s*\)$/);

    return rgb ? '#' + rgb.slice(1).map((v) => Number(v).toString(16).padStart(2, '0')).join('').toUpperCase() : null;

}



/** The font key whose stack matches a font-family value (browsers reformat quotes and spaces). */

function fontKey(value: unknown): string | null {

    if (typeof value !== 'string') return null;

    const flat = (s: string) => s.replace(/["'\s]/g, '').toLowerCase();

    return Object.keys(FONTS).find((key) => flat(FONTS[key]) === flat(value)) ?? null;

}
