import { HttpClient } from '@angular/common/http';

import { Component, inject, signal } from '@angular/core';

import { RouterModule } from '@angular/router';

import { firstValueFrom } from 'rxjs';

import { ButtonModule } from 'primeng/button';

import { Envelope, Session } from '@/app/core/api';

import { BrandMark, Branding } from '@/app/core/branding';



export interface RegistrationOffer {

    registrationOpen: boolean;

    trialDays: number;

    planName: string;

}



/** The public front page: what the product does and the way in. */

@Component({

    standalone: true,

    imports: [RouterModule, ButtonModule, BrandMark],

    template: `<div class="bg-surface-0 dark:bg-surface-950 min-h-screen flex flex-col">

        <header class="flex items-center justify-between px-6 lg:px-20 py-5">

            <a routerLink="/" class="text-surface-900 dark:text-surface-0"><app-brand /></a>

            <nav class="flex items-center gap-2">

                @if (session.refreshToken) {

                    <p-button label="Open the app" icon="pi pi-arrow-right" iconPos="right" routerLink="/app/dashboard" />

                } @else {

                    <p-button label="Sign in" [text]="true" routerLink="/login" />

                    @if (offer()?.registrationOpen !== false) {

                        <p-button label="Register your company" routerLink="/register" />

                    }

                }

            </nav>

        </header>



        <main class="flex-1">

            <section class="px-6 lg:px-20 pt-10 pb-16 grid grid-cols-12 gap-8 items-center">

                <div class="col-span-12 lg:col-span-7">

                    <p class="text-primary font-semibold uppercase tracking-widest text-sm mb-4">HR for growing companies</p>

                    <h1 class="text-4xl lg:text-6xl font-bold leading-tight text-surface-900 dark:text-surface-0 m-0">

                        Know who is at work,<br />from where, every day.

                    </h1>

                    <p class="text-xl text-muted-color mt-6 max-w-2xl leading-relaxed">

                        {{ branding.platform().tagline }} Employees check in from their own phone with their location and a photo;

                        you see attendance, leave and balances the moment they happen.

                    </p>

                    <div class="flex flex-wrap items-center gap-3 mt-8">

                        @if (offer()?.registrationOpen !== false) {

                            <p-button [label]="offer()?.trialDays ? 'Start your ' + offer()!.trialDays + '-day free trial' : 'Register your company'" size="large" routerLink="/register" />

                        }

                        <p-button label="Sign in" size="large" severity="secondary" [outlined]="true" routerLink="/login" />

                    </div>

                    @if (offer()?.registrationOpen === false) {

                        <p class="text-muted-color mt-4">New registrations are closed at the moment. Please contact us.</p>

                    } @else {

                        <p class="text-muted-color mt-4">No card needed. You become the administrator of your company’s workspace.</p>

                    }

                </div>

                <div class="col-span-12 lg:col-span-5">

                    <div class="card border border-surface shadow-lg !mb-0">

                        <div class="flex items-center justify-between mb-4">

                            <span class="font-semibold text-lg">Today</span>

                            <span class="text-sm text-muted-color">Head Office</span>

                        </div>

                        <div class="grid grid-cols-3 gap-3 text-center mb-5">

                            <div class="rounded-lg p-3 bg-green-50 dark:bg-green-400/10"><div class="text-2xl font-bold text-green-600">42</div><div class="text-sm text-muted-color">Present</div></div>

                            <div class="rounded-lg p-3 bg-amber-50 dark:bg-amber-400/10"><div class="text-2xl font-bold text-amber-600">3</div><div class="text-sm text-muted-color">Late</div></div>

                            <div class="rounded-lg p-3 bg-sky-50 dark:bg-sky-400/10"><div class="text-2xl font-bold text-sky-600">2</div><div class="text-sm text-muted-color">On leave</div></div>

                        </div>

                        <ul class="list-none p-0 m-0 flex flex-col gap-3">

                            <li class="flex items-center gap-3"><i class="pi pi-map-marker text-primary"></i><span>Checked in at Head Office · 9:02 AM</span></li>

                            <li class="flex items-center gap-3"><i class="pi pi-camera text-primary"></i><span>Photo captured with every check-in</span></li>

                            <li class="flex items-center gap-3"><i class="pi pi-flag text-amber-500"></i><span>1 check-in outside the geo-fence to review</span></li>

                        </ul>

                    </div>

                </div>

            </section>



            <section class="px-6 lg:px-20 py-14 bg-surface-50 dark:bg-surface-900">

                <h2 class="text-3xl font-semibold text-center text-surface-900 dark:text-surface-0 mt-0 mb-10">Everything for the working day</h2>

                <div class="grid grid-cols-12 gap-6">

                    @for (f of features; track f.title) {

                        <div class="col-span-12 md:col-span-6 lg:col-span-4">

                            <div class="card !mb-0 h-full">

                                <span class="inline-flex items-center justify-center rounded-xl bg-primary text-primary-contrast mb-4" style="width:3rem;height:3rem"><i class="{{ f.icon }} text-xl"></i></span>

                                <h3 class="text-xl font-semibold mt-0 mb-2">{{ f.title }}</h3>

                                <p class="text-muted-color leading-relaxed m-0">{{ f.text }}</p>

                            </div>

                        </div>

                    }

                </div>

            </section>

        </main>



        <footer class="px-6 lg:px-20 py-8 flex flex-wrap items-center justify-between gap-4 text-muted-color">

            <span>{{ branding.platform().footerText || branding.platform().brandName }}</span>

            <span class="flex flex-wrap gap-4">

                @if (branding.platform().supportEmail) {

                    <a class="text-muted-color" [href]="'mailto:' + branding.platform().supportEmail"><i class="pi pi-envelope mr-2"></i>{{ branding.platform().supportEmail }}</a>

                }

                @if (branding.platform().supportPhone) {

                    <a class="text-muted-color" [href]="'tel:' + branding.platform().supportPhone"><i class="pi pi-phone mr-2"></i>{{ branding.platform().supportPhone }}</a>

                }

            </span>

        </footer>

    </div>`

})

export class LandingPage {

    branding = inject(Branding);

    session = inject(Session);

    offer = signal<RegistrationOffer | null>(null);



    features = [

        { icon: 'pi pi-map-marker', title: 'Geo-fenced attendance', text: 'Draw a radius around each office or site. A check-in counts only inside it, or is sent to HR for review — your choice.' },

        { icon: 'pi pi-camera', title: 'Photo with every check-in', text: 'The employee’s camera takes a photo at the moment of check-in and check-out, stored with the time and place.' },

        { icon: 'pi pi-calendar-minus', title: 'Leave management', text: 'Leave types with yearly quotas, line-manager and HR approval steps, live balances and a leave statement.' },

        { icon: 'pi pi-clock', title: 'Shifts and lateness', text: 'Work schedules with grace minutes, week-offs and holidays decide who is present, late or absent.' },

        { icon: 'pi pi-sitemap', title: 'Your organization', text: 'Branches, departments, designations, employees and reporting lines, with a role for every kind of user.' },

        { icon: 'pi pi-palette', title: 'Your own branding', text: 'Your company’s name, logo and colours on the workspace your people use every day.' }

    ];



    constructor() {

        firstValueFrom(inject(HttpClient).get<Envelope<RegistrationOffer>>('/api/public/registration-offer'))

            .then((r) => this.offer.set(r.data))

            .catch(() => undefined);

    }

}
