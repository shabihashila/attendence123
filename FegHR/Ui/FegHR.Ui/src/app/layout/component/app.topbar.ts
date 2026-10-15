import { Component, computed, inject, signal } from '@angular/core';

import { Router, RouterModule } from '@angular/router';

import { CommonModule } from '@angular/common';

import { FormsModule } from '@angular/forms';

import { SelectModule } from 'primeng/select';

import { StyleClassModule } from 'primeng/styleclass';

import { TagModule } from 'primeng/tag';

import { AppConfigurator } from './app.configurator';

import { LayoutService } from '@/app/layout/service/layout.service';

import { Api, Session } from '@/app/core/api';

import { Notify, StyledName } from '@/app/core/ui';



@Component({

    selector: 'app-topbar',

    standalone: true,

    imports: [RouterModule, CommonModule, FormsModule, StyleClassModule, SelectModule, TagModule, AppConfigurator, StyledName],

    template: ` <div class="layout-topbar">

        <div class="layout-topbar-logo-container">

            <button class="layout-menu-button layout-topbar-action" (click)="layoutService.onMenuToggle()" aria-label="Toggle menu">

                <i class="pi pi-bars"></i>

            </button>

            <a class="layout-topbar-logo" routerLink="/app/dashboard">

                <img [src]="session.branding()?.logoUrl || '/feg-technova-logo.png'" alt="" style="height:2.25rem;width:2.25rem;object-fit:contain" />

                <app-styled-name [runs]="session.branding()?.nameStyle" [fallback]="session.branding()?.name || 'FegHR'" />

            </a>

        </div>



        <div class="layout-topbar-actions">

            @if (licenceNote(); as note) {

                <a routerLink="/app/admin/licence" class="hidden md:inline-flex"><p-tag [severity]="note.severity" [value]="note.text" /></a>

            }

            <div class="company-switchers">
                <div class="company-switcher">
                    <label for="mainCompany">Main company</label>
                    <p-select inputId="mainCompany" [options]="mainCompanies()" optionLabel="name" optionValue="id"
                        [ngModel]="mainCompanyId()" (ngModelChange)="switchMainCompany($event)"
                        [filter]="mainCompanies().length > 8" filterBy="name" [disabled]="switching()" appendTo="body" />
                </div>
                @if (hasConcerns()) {
                    <div class="company-switcher">
                        <label for="workingCompany">Company / child concern</label>
                        <p-select inputId="workingCompany" [options]="workingCompanies()" optionLabel="name" optionValue="tenantId"
                            [ngModel]="session.me()?.tenant?.id" (ngModelChange)="switchCompany($event)"
                            [filter]="workingCompanies().length > 8" filterBy="name" [disabled]="switching()" appendTo="body" />
                    </div>
                }
            </div>

            <div class="layout-config-menu">

                <button type="button" class="layout-topbar-action" (click)="toggleDarkMode()" aria-label="Toggle dark mode">

                    <i [ngClass]="{ 'pi ': true, 'pi-moon': layoutService.isDarkTheme(), 'pi-sun': !layoutService.isDarkTheme() }"></i>

                </button>

                <div class="relative">

                    <button

                        class="layout-topbar-action layout-topbar-action-highlight"

                        pStyleClass="@next"

                        enterFromClass="hidden"

                        enterActiveClass="animate-scalein"

                        leaveToClass="hidden"

                        leaveActiveClass="animate-fadeout"

                        [hideOnOutsideClick]="true"

                        aria-label="Theme"

                    >

                        <i class="pi pi-palette"></i>

                    </button>

                    <app-configurator />

                </div>

            </div>



            <button class="layout-topbar-menu-button layout-topbar-action" pStyleClass="@next" enterFromClass="hidden" enterActiveClass="animate-scalein" leaveToClass="hidden" leaveActiveClass="animate-fadeout" [hideOnOutsideClick]="true">

                <i class="pi pi-ellipsis-v"></i>

            </button>



            <div class="layout-topbar-menu hidden lg:block">

                <div class="layout-topbar-menu-content">

                    <a routerLink="/app/account" class="layout-topbar-action" [title]="session.me()?.user?.displayName || ''">

                        <i class="pi pi-user"></i>

                        <span>{{ session.me()?.user?.displayName }}</span>

                    </a>

                    <button type="button" class="layout-topbar-action" (click)="signOut()" title="Sign out">

                        <i class="pi pi-sign-out"></i>

                        <span>Sign out</span>

                    </button>

                </div>

            </div>

        </div>

    </div>`

})

export class AppTopbar {

    layoutService = inject(LayoutService);

    session = inject(Session);

    private api = inject(Api);

    private notify = inject(Notify);

    private router = inject(Router);



    switching = signal(false);
    mainCompanyId = computed(() => {
        const current = this.session.tenants().find(t => t.tenantId === this.session.me()?.tenant?.id);
        return current?.parentTenantRefId ?? current?.tenantId ?? null;
    });
    mainCompanies = computed(() => {
        const companies = new Map<number, { id: number; name: string }>();
        for (const t of this.session.tenants()) {
            const id = t.parentTenantRefId ?? t.tenantId;
            companies.set(id, { id, name: t.parentTenantRefId ? (t.parentName || 'Main company') : t.name });
        }
        return [...companies.values()].sort((a, b) => a.name.localeCompare(b.name));
    });
    workingCompanies = computed(() => this.session.tenants().filter(t =>
        t.tenantId === this.mainCompanyId() || t.parentTenantRefId === this.mainCompanyId()));
    hasConcerns = computed(() => this.workingCompanies().some(t => t.parentTenantRefId != null || t.hasChildConcerns));

    async switchMainCompany(id: number) {
        if (id === this.mainCompanyId()) return;
        const options = this.session.tenants();
        const target = options.find(t => t.isDefault && (t.tenantId === id || t.parentTenantRefId === id))
            ?? options.find(t => t.parentTenantRefId === id) ?? options.find(t => t.tenantId === id);
        if (target) await this.switchCompany(target.tenantId);
    }

    /** A reminder while the trial or licence runs out, and a warning once it has. */

    licenceNote = computed(() => {

        const licence = this.session.licence();

        if (!licence) return null;

        if (!licence.isLicensed) return { severity: 'danger' as const, text: 'Licence expired' };

        if (licence.kind === 'Trial') return { severity: 'info' as const, text: `Free trial: ${licence.daysLeft ?? 0} day(s) left` };

        if (licence.daysLeft != null && licence.daysLeft <= 14) return { severity: 'warn' as const, text: `Licence ends in ${licence.daysLeft} day(s)` };

        return null;

    });



    toggleDarkMode() {

        this.layoutService.layoutConfig.update((state) => ({

            ...state,

            darkTheme: !state.darkTheme

        }));

    }



    async switchCompany(tenantId: number) {

        if (this.switching() || tenantId === this.session.me()?.tenant?.id) return;

        this.switching.set(true);

        try {

            await this.api.switchTenant(tenantId);

            // Every page reads the company it was opened for, so land on the dashboard again.

            await this.router.navigateByUrl('/', { skipLocationChange: true });

            await this.router.navigate(['/app/dashboard']);

            this.notify.ok(`Now working in ${this.session.me()?.tenant?.name}.`);

        } catch (e) {

            this.notify.fail(e);

        } finally {
            this.switching.set(false);
        }

    }



    signOut() {

        void this.api.signOut();

    }

}
