import { Injectable, effect, signal, computed } from '@angular/core';



export interface LayoutConfig {

    preset: string;

    primary: string;

    surface: string | undefined | null;

    darkTheme: boolean;

    menuMode: string;

}



interface LayoutState {

    staticMenuDesktopInactive: boolean;

    overlayMenuActive: boolean;

    configSidebarVisible: boolean;

    mobileMenuActive: boolean;

    menuHoverActive: boolean;

    activePath: string | null;

}



@Injectable({

    providedIn: 'root'

})

export class LayoutService {

    // 'brand' as the primary means "follow the company's (or the product's) branding colour".

    // The whole choice is remembered per browser, so a reload keeps the theme.

    layoutConfig = signal<LayoutConfig>(LayoutService.restore());



    layoutState = signal<LayoutState>({

        staticMenuDesktopInactive: false,

        overlayMenuActive: false,

        configSidebarVisible: false,

        mobileMenuActive: false,

        menuHoverActive: false,

        activePath: null

    });



    theme = computed(() => (this.layoutConfig().darkTheme ? 'light' : 'dark'));



    isSidebarActive = computed(() => this.layoutState().overlayMenuActive || this.layoutState().mobileMenuActive);



    isDarkTheme = computed(() => this.layoutConfig().darkTheme);



    getPrimary = computed(() => this.layoutConfig().primary);



    getSurface = computed(() => this.layoutConfig().surface);



    isOverlay = computed(() => this.layoutConfig().menuMode === 'overlay');



    transitionComplete = signal<boolean>(false);



    private initialized = false;



    constructor() {

        // Dark mode is applied before the first paint; the effect below only handles later switches.

        this.toggleDarkMode();



        effect(() => {

            const config = this.layoutConfig();

            try {

                localStorage.setItem(LayoutService.STORAGE_KEY, JSON.stringify(config));

            } catch {

                /* Without storage the theme simply lasts for this visit. */

            }



            if (!this.initialized || !config) {

                this.initialized = true;

                return;

            }



            this.handleDarkModeTransition(config);

        });

    }



    private static readonly STORAGE_KEY = 'feghr.layout';



    private static restore(): LayoutConfig {

        const defaults: LayoutConfig = { preset: 'Aura', primary: 'brand', surface: null, darkTheme: false, menuMode: 'static' };

        try {

            const saved = JSON.parse(localStorage.getItem(LayoutService.STORAGE_KEY) ?? 'null') as Partial<LayoutConfig> | null;

            if (!saved || typeof saved !== 'object') return defaults;

            return {

                preset: ['Aura', 'Lara', 'Nora'].includes(saved.preset ?? '') ? saved.preset! : defaults.preset,

                primary: typeof saved.primary === 'string' && /^[a-z]{3,12}$/.test(saved.primary) ? saved.primary : defaults.primary,

                surface: typeof saved.surface === 'string' && /^[a-z]{3,12}$/.test(saved.surface) ? saved.surface : null,

                darkTheme: saved.darkTheme === true,

                menuMode: saved.menuMode === 'overlay' ? 'overlay' : 'static'

            };

        } catch {

            return defaults;

        }

    }



    private handleDarkModeTransition(config: LayoutConfig): void {

        const supportsViewTransition = 'startViewTransition' in document;



        if (supportsViewTransition) {

            this.startViewTransition(config);

        } else {

            this.toggleDarkMode(config);

        }

    }



    private startViewTransition(config: LayoutConfig): void {

        document.startViewTransition(() => {

            this.toggleDarkMode(config);

        });

    }



    toggleDarkMode(config?: LayoutConfig): void {

        const _config = config || this.layoutConfig();

        if (_config.darkTheme) {

            document.documentElement.classList.add('app-dark');

        } else {

            document.documentElement.classList.remove('app-dark');

        }

    }



    onMenuToggle() {

        if (this.isOverlay()) {

            this.layoutState.update((prev) => ({ ...prev, overlayMenuActive: !this.layoutState().overlayMenuActive }));

        }



        if (this.isDesktop()) {

            this.layoutState.update((prev) => ({ ...prev, staticMenuDesktopInactive: !this.layoutState().staticMenuDesktopInactive }));

        } else {

            this.layoutState.update((prev) => ({ ...prev, mobileMenuActive: !this.layoutState().mobileMenuActive }));

        }

    }



    showConfigSidebar() {

        this.layoutState.update((prev) => ({ ...prev, configSidebarVisible: true }));

    }



    hideConfigSidebar() {

        this.layoutState.update((prev) => ({ ...prev, configSidebarVisible: false }));

    }



    isDesktop() {

        return window.innerWidth > 991;

    }



    isMobile() {

        return !this.isDesktop();

    }

}
