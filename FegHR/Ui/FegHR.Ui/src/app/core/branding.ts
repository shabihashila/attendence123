import { HttpClient } from '@angular/common/http';

import { Component, Injectable, effect, inject, signal } from '@angular/core';

import { palette, updatePrimaryPalette } from '@primeuix/themes';

import { firstValueFrom, timeout } from 'rxjs';

import { Envelope, NameSegment, Session } from './api';

import { StyledName } from './ui';

import { LayoutService } from '@/app/layout/service/layout.service';



/** How the product presents itself before anyone signs in. Edited by the product owner. */

export interface PlatformBranding {

    brandName: string;

    nameStyle: NameSegment[] | null;

    tagline: string | null;

    primaryColor: string;

    accentColor: string;

    supportEmail: string | null;

    supportPhone: string | null;

    footerText: string | null;

    logoUrl: string | null;

    faviconUrl: string | null;

    rowVersion: string;

}



const BUNDLED: PlatformBranding = {

    brandName: 'FegHR',

    nameStyle: null,

    tagline: 'People, attendance and leave in one place.',

    primaryColor: '#1863DC',

    accentColor: '#0F9F8F',

    supportEmail: null,

    supportPhone: null,

    footerText: null,

    logoUrl: null,

    faviconUrl: null,

    rowVersion: ''

};



/**

 * Dynamic branding. The public pages use the product branding; once signed in, the shell takes

 * its colour from the company's own branding. The page title and favicon follow the product.

 */

@Injectable({ providedIn: 'root' })

export class Branding {

    private http = inject(HttpClient);

    private session = inject(Session);

    private layout = inject(LayoutService);

    platform = signal<PlatformBranding>(BUNDLED);



    constructor() {

        effect(() => {

            const product = this.platform();

            const company = this.session.branding();

            document.title = company ? `${company.name} · ${product.brandName}` : product.brandName;

            let icon = document.querySelector<HTMLLinkElement>('link[rel="icon"]');

            if (!icon) {

                icon = document.createElement('link');

                icon.rel = 'icon';

                document.head.appendChild(icon);

            }

            icon.href = product.faviconUrl || '/feg-technova-logo.png';

            icon.removeAttribute('type');

        });



        // The branding colour is the primary colour unless the user picked a palette of their own

        // in the theme panel. Reading the layout config here re-applies it after every theme change

        // (preset, dark or light, palette), each of which rebuilds the theme and would drop it.

        effect(() => {

            const layout = this.layout.layoutConfig();

            const colour = this.session.branding()?.primaryColor || this.platform().primaryColor;

            if (!/^#[0-9a-f]{6}$/i.test(colour)) return;

            document.documentElement.style.setProperty('--brand-color', colour);

            if (layout.primary === 'brand') updatePrimaryPalette(palette(colour) as never);

        });

    }



    /** Runs at start-up. The bundled identity keeps the public pages usable when the API is down. */

    async load(): Promise<void> {

        try {

            const result = await firstValueFrom(this.http.get<Envelope<PlatformBranding>>('/api/public/platform-branding').pipe(timeout(5000)));

            this.platform.set(result.data);

        } catch {

            /* keep the bundled branding */

        }

    }



}



/** The product's logo and styled name. */

@Component({

    selector: 'app-brand',

    standalone: true,

    imports: [StyledName],

    template: `<img [src]="branding.platform().logoUrl || '/feg-technova-logo.png'" alt="" [style.height.px]="size" [style.width.px]="size" style="object-fit:contain" />

        <app-styled-name [runs]="branding.platform().nameStyle" [fallback]="branding.platform().brandName" />`,

    host: { class: 'inline-flex items-center gap-2 font-semibold', '[style.font-size.rem]': 'size / 22' }

})

export class BrandMark {

    branding = inject(Branding);

    size = 32;

}
