import { HttpClient } from '@angular/common/http';

import { Component, inject, signal } from '@angular/core';

import { FormsModule } from '@angular/forms';

import { ActivatedRoute, Router, RouterModule } from '@angular/router';

import { firstValueFrom } from 'rxjs';

import { ButtonModule } from 'primeng/button';

import { FloatLabelModule } from 'primeng/floatlabel';

import { InputTextModule } from 'primeng/inputtext';

import { MessageModule } from 'primeng/message';

import { PasswordModule } from 'primeng/password';

import { Api, AuthSession, Envelope, LoginResponse, Session, TenantOption } from '@/app/core/api';

import { BrandMark, Branding } from '@/app/core/branding';

import { describe } from '@/app/core/ui';

import { RegistrationOffer } from './landing';



/** Sign-in and public company registration share one card, in the Sakai login layout. */

@Component({

    standalone: true,

    imports: [FormsModule, RouterModule, ButtonModule, FloatLabelModule, InputTextModule, PasswordModule, MessageModule, BrandMark],

    template: `<div class="bg-surface-50 dark:bg-surface-950 flex items-center justify-center min-h-screen overflow-hidden py-10 px-4">

        <div class="w-full" [style.max-width]="register ? '46rem' : '30rem'">

            <div style="border-radius:56px;padding:0.3rem;background:linear-gradient(180deg,var(--primary-color) 10%,rgba(33,150,243,0) 30%)">

                <div class="w-full bg-surface-0 dark:bg-surface-900 py-12 px-6 sm:px-12" style="border-radius:53px">

                    <div class="text-center mb-8">

                        <a routerLink="/" class="text-surface-900 dark:text-surface-0 inline-block mb-6"><app-brand /></a>

                        <div class="text-surface-900 dark:text-surface-0 text-3xl font-medium mb-3">

                            {{ companies().length ? 'Choose your company' : register ? 'Register your company' : 'Welcome back' }}

                        </div>

                        <span class="text-muted-color font-medium">

                            @if (companies().length) {

                                Your account belongs to more than one company.

                            } @else if (register) {

                                @if (offer()?.trialDays) {

                                    Start with a {{ offer()!.trialDays }}-day free trial of the {{ offer()!.planName }} plan. You will be the company administrator.

                                } @else {

                                    You will be the administrator of your company’s workspace.

                                }

                            } @else {

                                Sign in to continue

                            }

                        </span>

                    </div>



                    @if (error()) {

                        <p-message severity="error" styleClass="mb-6 w-full" [text]="error()" />

                    }



                    @if (companies().length) {

                        <div class="flex flex-col gap-3">

                            @for (c of companies(); track c.tenantId) {

                                <p-button [label]="c.name" icon="pi pi-building" severity="secondary" [outlined]="true" styleClass="w-full justify-start" [disabled]="busy()" (onClick)="choose(c)" />

                            }

                            <p-button label="Use another account" [text]="true" (onClick)="companies.set([])" />

                        </div>

                    } @else if (register) {

                        @if (offer()?.registrationOpen === false) {

                            <p-message severity="warn" styleClass="w-full" text="New registrations are closed at the moment. Please contact us." />

                        } @else {

                            <form class="grid grid-cols-12 gap-x-4 gap-y-5" (ngSubmit)="submitRegistration()" #form="ngForm">

                                <div class="col-span-12 font-semibold text-surface-900 dark:text-surface-0">Company</div>

                                <div class="col-span-12 md:col-span-6">

                                    <p-floatlabel variant="on"><input pInputText id="companyName" name="companyName" [(ngModel)]="r.companyName" required minlength="2" maxlength="200" autocomplete="organization" fluid /><label for="companyName">Company name *</label></p-floatlabel>

                                </div>

                                <div class="col-span-12 md:col-span-6">

                                    <p-floatlabel variant="on"><input pInputText id="companyPhone" name="companyPhone" [(ngModel)]="r.companyPhone" maxlength="30" autocomplete="tel" fluid /><label for="companyPhone">Company phone</label></p-floatlabel>

                                </div>

                                <div class="col-span-12">

                                    <p-floatlabel variant="on"><input pInputText id="companyAddress" name="companyAddress" [(ngModel)]="r.companyAddress" maxlength="500" autocomplete="street-address" fluid /><label for="companyAddress">Address</label></p-floatlabel>

                                </div>

                                <div class="col-span-12 font-semibold text-surface-900 dark:text-surface-0 mt-2">Your account (company administrator)</div>

                                <div class="col-span-12 md:col-span-6">

                                    <p-floatlabel variant="on"><input pInputText id="fullName" name="fullName" [(ngModel)]="r.fullName" required minlength="2" maxlength="150" autocomplete="name" fluid /><label for="fullName">Your name *</label></p-floatlabel>

                                </div>

                                <div class="col-span-12 md:col-span-6">

                                    <p-floatlabel variant="on"><input pInputText id="phone" name="phone" [(ngModel)]="r.phone" maxlength="30" autocomplete="tel-national" fluid /><label for="phone">Mobile number</label></p-floatlabel>

                                </div>

                                <div class="col-span-12 md:col-span-6">

                                    <p-floatlabel variant="on"><input pInputText id="email" name="email" type="email" [(ngModel)]="r.email" required email maxlength="254" autocomplete="email" fluid /><label for="email">Email *</label></p-floatlabel>

                                </div>

                                <div class="col-span-12 md:col-span-6">

                                    <p-floatlabel variant="on"><p-password inputId="password" name="password" [(ngModel)]="r.password" [toggleMask]="true" [feedback]="false" required [minlength]="8" autocomplete="new-password" fluid /><label for="password">Password *</label></p-floatlabel>

                                    <small class="text-muted-color">At least 8 characters with a letter and a digit.</small>

                                </div>

                                <div class="col-span-12">

                                    <p-button type="submit" label="Create my company" styleClass="w-full" [loading]="busy()" [disabled]="form.invalid || busy()" />

                                </div>

                            </form>

                        }

                        <p class="text-center text-muted-color mt-6 mb-0">Already registered? <a routerLink="/login" class="text-primary font-medium">Sign in</a></p>

                    } @else {

                        <form (ngSubmit)="submitLogin()" #form="ngForm">

                            <p-floatlabel variant="on" class="block mb-8"><input pInputText id="login" name="login" type="text" class="w-full" [(ngModel)]="login" required autocomplete="username" /><label for="login">Email or mobile number</label></p-floatlabel>



                            <p-floatlabel variant="on" class="block mb-8"><p-password inputId="loginPassword" name="loginPassword" [(ngModel)]="password" [toggleMask]="true" [fluid]="true" [feedback]="false" required autocomplete="current-password" /><label for="loginPassword">Password</label></p-floatlabel>



                            <p-button type="submit" label="Sign in" styleClass="w-full" [loading]="busy()" [disabled]="form.invalid || busy()" />

                        </form>

                        @if (offer()?.registrationOpen !== false) {

                            <p class="text-center text-muted-color mt-6 mb-0">New here? <a routerLink="/register" class="text-primary font-medium">Register your company</a></p>

                        }

                    }

                </div>

            </div>

        </div>

    </div>`

})

export class AuthPage {

    private api = inject(Api);

    private session = inject(Session);

    private router = inject(Router);

    private route = inject(ActivatedRoute);

    private http = inject(HttpClient);

    branding = inject(Branding);



    register = !!this.route.snapshot.data['register'];

    busy = signal(false);

    error = signal('');

    offer = signal<RegistrationOffer | null>(null);

    companies = signal<TenantOption[]>([]);

    private selectionToken = '';



    login = '';

    password = '';

    r = { companyName: '', companyPhone: '', companyAddress: '', fullName: '', phone: '', email: '', password: '' };



    constructor() {

        firstValueFrom(this.http.get<Envelope<RegistrationOffer>>('/api/public/registration-offer'))

            .then((x) => this.offer.set(x.data))

            .catch(() => undefined);

    }



    async submitLogin() {

        await this.run(async () => {

            const result = (await this.api.post<LoginResponse>('/api/iam/auth/login', { login: this.login.trim(), password: this.password })).data;

            if (result.requiresTenantSelection) {

                this.selectionToken = result.selectionToken ?? '';

                this.companies.set(result.tenants ?? []);

                return;

            }

            await this.enter(result.session!);

        });

    }



    async choose(company: TenantOption) {

        await this.run(async () => {

            const result = await this.api.post<AuthSession>('/api/iam/auth/select-tenant', { selectionToken: this.selectionToken, tenantId: company.tenantId });

            await this.enter(result.data);

        });

    }



    async submitRegistration() {

        await this.run(async () => {

            const body = Object.fromEntries(Object.entries(this.r).map(([k, v]) => [k, k === 'password' ? v : v.trim() || null]));

            const result = await this.api.post<{ session: AuthSession }>('/api/iam/auth/register', body);

            await this.enter(result.data.session);

        });

    }



    private async enter(session: AuthSession) {

        this.session.clear();

        this.session.accept(session);

        const next = this.route.snapshot.queryParamMap.get('next');

        await this.router.navigateByUrl(next && next.startsWith('/app') ? next : '/app/dashboard');

    }



    private async run(action: () => Promise<void>) {

        if (this.busy()) return;

        this.busy.set(true);

        this.error.set('');

        try {

            await action();

        } catch (e) {

            this.error.set(describe(e));

        } finally {

            this.busy.set(false);

        }

    }

}
