import { HttpClient, HttpErrorResponse, HttpHeaders, HttpParams } from '@angular/common/http';

import { Injectable, computed, inject, signal } from '@angular/core';

import { CanActivateFn, Router } from '@angular/router';

import { firstValueFrom, timeout } from 'rxjs';



/** The API's response envelope (same shape as DailyCommerce). */

export interface Envelope<T> {

    success: boolean;

    message: string | null;

    data: T;

    errors: Record<string, string[]> | null;

}

export interface Paged<T> {

    items: T[];

    totalCount: number;

    page: number;

    pageSize: number;

}

export interface TenantOption {

    tenantId: number;

    slug: string;

    name: string;

}

export interface AuthSession {

    accessToken: string;

    accessTokenExpiresAt: string;

    refreshToken: string;

    refreshTokenExpiresAt: string;

    tenant: TenantOption;

}

export interface LoginResponse {

    requiresTenantSelection: boolean;

    session: AuthSession | null;

    selectionToken: string | null;

    tenants: TenantOption[] | null;

}

export interface Me {

    user: { id: number; displayName: string; email: string | null; phone: string | null };

    tenant: { id: number; slug: string; name: string; currencyCode: string; timeZoneId: string } | null;

    permissions: string[];

    branches: { id: number; code: string; name: string; isActive: boolean }[];

    isPlatformAdmin: boolean;

}

export interface NavItem {

    code: string;

    title: string;

    icon: string | null;

    route: string | null;

    children: NavItem[];

}

export interface Licence {

    isLicensed: boolean;

    kind: string | null;

    planName: string | null;

    startsAt: string | null;

    endsAt: string | null;

    daysLeft: number | null;

    tenantStatus: string;

}

export interface NameSegment {

    text: string;

    color?: string | null;

    background?: string | null;

    bold?: boolean;

    italic?: boolean;

    underline?: boolean;

    strike?: boolean;

    font?: string | null;

    fontSize?: number | null;

}

export interface CompanyBranding {

    name: string;

    tagline: string | null;

    logoUrl: string | null;

    primaryColor: string;

    accentColor: string;

    phone: string | null;

    email: string | null;

    address: string | null;

    displayName: string | null;

    rowVersion: string;

    nameStyle: NameSegment[] | null;

}



/** A failed API call, carrying the message the server wrote for the user. */

export class ApiError extends Error {

    constructor(

        message: string,

        readonly status: number,

        readonly errors: Record<string, string[]> | null = null

    ) {

        super(message);

    }

}



const REFRESH_KEY = 'feghr.refresh';



/** Who is signed in, to which company, and what they may do. */

@Injectable({ providedIn: 'root' })

export class Session {

    accessToken = signal('');

    me = signal<Me | null>(null);

    nav = signal<NavItem[]>([]);

    licence = signal<Licence | null>(null);

    branding = signal<CompanyBranding | null>(null);

    tenants = signal<TenantOption[]>([]);



    signedIn = computed(() => !!this.accessToken());

    isOwner = computed(() => this.me()?.isPlatformAdmin ?? false);



    get refreshToken(): string {

        try {

            return localStorage.getItem(REFRESH_KEY) ?? '';

        } catch {

            return '';

        }

    }



    can(...anyOf: string[]): boolean {

        const granted = this.me()?.permissions ?? [];

        return anyOf.some((p) => granted.includes(p));

    }



    accept(session: AuthSession) {

        this.accessToken.set(session.accessToken);

        try {

            localStorage.setItem(REFRESH_KEY, session.refreshToken);

        } catch {

            /* Without storage the session simply ends with the tab. */

        }

    }



    clear() {

        this.accessToken.set('');

        this.me.set(null);

        this.nav.set([]);

        this.licence.set(null);

        this.branding.set(null);

        this.tenants.set([]);

        try {

            localStorage.removeItem(REFRESH_KEY);

        } catch {

            /* nothing stored */

        }

    }

}



@Injectable({ providedIn: 'root' })

export class Api {

    private http = inject(HttpClient);

    private router = inject(Router);

    session = inject(Session);

    private refreshing: Promise<boolean> | null = null;



    get<T>(path: string, params?: Record<string, unknown>): Promise<T> {

        return this.send<T>('GET', path, undefined, params).then((r) => r.data);

    }

    post<T = unknown>(path: string, body?: unknown): Promise<Envelope<T>> {

        return this.send<T>('POST', path, body ?? {});

    }

    put<T = unknown>(path: string, body?: unknown, params?: Record<string, unknown>): Promise<Envelope<T>> {

        return this.send<T>('PUT', path, body ?? {}, params);

    }

    delete<T = unknown>(path: string, params?: Record<string, unknown>): Promise<Envelope<T>> {

        return this.send<T>('DELETE', path, undefined, params);

    }

    /** Multipart upload; the browser sets the boundary. */

    upload<T = unknown>(path: string, form: FormData): Promise<Envelope<T>> {

        return this.send<T>('POST', path, form);

    }



    private async send<T>(method: string, path: string, body?: unknown, params?: Record<string, unknown>, retried = false): Promise<Envelope<T>> {

        let query = new HttpParams();

        for (const [key, value] of Object.entries(params ?? {})) {

            if (value !== null && value !== undefined && value !== '') query = query.set(key, String(value));

        }

        const token = this.session.accessToken();

        const headers = new HttpHeaders(token ? { Authorization: 'Bearer ' + token } : {});

        try {

            return await firstValueFrom(this.http.request<Envelope<T>>(method, path, { body, headers, params: query }).pipe(timeout(60000)));

        } catch (e) {

            if (e instanceof HttpErrorResponse) {

                // An expired access token: rotate the refresh token once and repeat the call.

                if (e.status === 401 && token && !retried && !path.includes('/auth/') && (await this.refresh())) {

                    return this.send<T>(method, path, body, params, true);

                }

                if (e.status === 401 && token && !path.includes('/auth/')) {

                    this.session.clear();

                    void this.router.navigate(['/login']);

                }

                // The licence lapsed: the licence page explains it and stays reachable.

                if (e.status === 402 && !this.router.url.startsWith('/app/admin/licence')) {

                    void this.router.navigate(['/app/admin/licence']);

                }

                const envelope = e.error as Partial<Envelope<unknown>> | null;

                const fallback = e.status === 0 ? 'Cannot reach the server. Check your connection.' : e.status === 429 ? 'Too many attempts. Please wait a little and try again.' : 'The request failed. Please try again.';

                throw new ApiError((envelope && typeof envelope === 'object' && envelope.message) || fallback, e.status, (envelope && typeof envelope === 'object' && envelope.errors) || null);

            }

            throw new ApiError('The server took too long to answer. Please try again.', 0);

        }

    }



    /** Exchanges the stored refresh token for a new session. Concurrent callers share one attempt. */

    refresh(): Promise<boolean> {

        const token = this.session.refreshToken;

        if (!token) return Promise.resolve(false);

        this.refreshing ??= firstValueFrom(this.http.post<Envelope<AuthSession>>('/api/iam/auth/refresh', { refreshToken: token }).pipe(timeout(30000)))

            .then((r) => {

                this.session.accept(r.data);

                return true;

            })

            .catch(() => false)

            .finally(() => (this.refreshing = null));

        return this.refreshing;

    }



    /** Loads everything the shell shows after sign-in. */

    async loadShell(): Promise<void> {

        const [me, nav] = await Promise.all([this.get<Me>('/api/iam/me'), this.get<NavItem[]>('/api/iam/navigation')]);

        this.session.me.set(me);

        this.session.nav.set(nav);

        const [licence, branding, tenants] = await Promise.all([

            this.get<Licence>('/api/platform/licence').catch(() => null),

            this.get<CompanyBranding>('/api/platform/branding').catch(() => null),

            this.get<TenantOption[]>('/api/iam/session/tenants').catch(() => [])

        ]);

        this.session.licence.set(licence);

        this.session.branding.set(branding);

        this.session.tenants.set(tenants);

    }



    async signOut(): Promise<void> {

        const token = this.session.refreshToken;

        if (token) await this.post('/api/iam/auth/logout', { refreshToken: token }).catch(() => undefined);

        this.session.clear();

        await this.router.navigate(['/login']);

    }



    async switchTenant(tenantId: number): Promise<void> {

        const result = await this.post<AuthSession>('/api/iam/session/switch-tenant', { tenantId, refreshToken: this.session.refreshToken || null });

        this.session.accept(result.data);

        await this.loadShell();

    }

}



/** The signed-in area: restores the session after a reload, then loads the shell once. */

export const authGuard: CanActivateFn = async (_, state) => {

    const api = inject(Api);

    const session = inject(Session);

    const router = inject(Router);

    const toLogin = router.createUrlTree(['/login'], { queryParams: state.url === '/app/dashboard' ? {} : { next: state.url } });

    if (!session.accessToken() && !(await api.refresh())) return toLogin;

    if (session.me()) return true;

    try {

        await api.loadShell();

        return true;

    } catch {

        session.clear();

        return toLogin;

    }

};



/** Route data: `permission` (one code or several, any of which is enough) and `owner`. */

export const permissionGuard: CanActivateFn = (route) => {

    const session = inject(Session);

    const router = inject(Router);

    if (route.data['owner'] && !session.isOwner()) return router.createUrlTree(['/app/dashboard']);

    const wanted = route.data['permission'] as string | string[] | undefined;

    if (!wanted) return true;

    return session.can(...(Array.isArray(wanted) ? wanted : [wanted])) ? true : router.createUrlTree(['/app/dashboard']);

};
