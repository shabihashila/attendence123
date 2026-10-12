import { provideHttpClient, withFetch } from '@angular/common/http';

import { ApplicationConfig, inject, provideAppInitializer, provideZonelessChangeDetection } from '@angular/core';

import { provideRouter, withEnabledBlockingInitialNavigation, withInMemoryScrolling } from '@angular/router';

import Aura from '@primeuix/themes/aura';

import { ConfirmationService, MessageService } from 'primeng/api';

import { providePrimeNG } from 'primeng/config';

import { Branding } from './app/core/branding';

import { LayoutService } from './app/layout/service/layout.service';

import { appRoutes } from './app.routes';



export const appConfig: ApplicationConfig = {

    providers: [

        // Product branding first: the landing and sign-in pages render with it.

        // The saved theme (dark or light) applies on the public pages too, before anything renders.

        provideAppInitializer(() => void inject(LayoutService)),

        provideAppInitializer(() => inject(Branding).load()),

        provideRouter(appRoutes, withInMemoryScrolling({ anchorScrolling: 'enabled', scrollPositionRestoration: 'enabled' }), withEnabledBlockingInitialNavigation()),

        provideHttpClient(withFetch()),

        provideZonelessChangeDetection(),

        providePrimeNG({ theme: { preset: Aura, options: { darkModeSelector: '.app-dark' } } }),

        MessageService,

        ConfirmationService

    ]

};
