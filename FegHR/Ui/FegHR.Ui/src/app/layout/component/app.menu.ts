import { Component, computed, inject } from '@angular/core';

import { CommonModule } from '@angular/common';

import { RouterModule } from '@angular/router';

import { MenuItem } from 'primeng/api';

import { AppMenuitem } from './app.menuitem';

import { NavItem, Session } from '@/app/core/api';



/**

 * The sidebar. Its entries come from the server (`GET /api/iam/navigation`), already filtered

 * by the user's permissions and the company's licence, so the menu never offers a page the

 * API would refuse.

 */

@Component({

    selector: 'app-menu',

    standalone: true,

    imports: [CommonModule, AppMenuitem, RouterModule],

    template: `<ul class="layout-menu">

        @for (item of model(); track item.label) {

            @if (!item.separator) {

                <li app-menuitem [item]="item" [root]="true"></li>

            } @else {

                <li class="menu-separator"></li>

            }

        }

    </ul> `

})

export class AppMenu {

    private session = inject(Session);



    model = computed<MenuItem[]>(() => {

        const nav = this.session.nav();

        // Sakai's root entries are section headings; a root leaf (the dashboard) gets a "Home" section.

        const home = nav.filter((n) => n.route);

        const sections = nav.filter((n) => !n.route && n.children.length);

        return [...(home.length ? [{ label: 'Home', items: home.map((n) => this.leaf(n)) }] : []), ...sections.map((n) => ({ label: n.title, items: n.children.map((c) => this.leaf(c)) }))];

    });



    private leaf(n: NavItem): MenuItem {

        return n.children.length ? { label: n.title, icon: n.icon ?? undefined, items: n.children.map((c) => this.leaf(c)) } : { label: n.title, icon: n.icon ?? undefined, routerLink: [n.route] };

    }

}
