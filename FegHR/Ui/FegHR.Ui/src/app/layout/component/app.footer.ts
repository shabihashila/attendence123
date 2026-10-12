import { Component, inject } from '@angular/core';

import { Branding } from '@/app/core/branding';



@Component({

    standalone: true,

    selector: 'app-footer',

    template: `<div class="layout-footer">{{ branding.platform().footerText || branding.platform().brandName + ' · ' + (branding.platform().tagline || '') }}</div>`

})

export class AppFooter {

    branding = inject(Branding);

}
