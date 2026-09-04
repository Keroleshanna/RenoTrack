import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';

import { I18n } from '../core/i18n/i18n';
import { LANGUAGES } from '../core/i18n/strings';
import { Auth } from '../core/auth/auth';

/**
 * The top app bar.
 *
 * A **top bar, not a sidebar**: the widest thing this application renders is a table, and a
 * horizontal bar leaves the full width to it.
 *
 * **Navigation lists only what a role may actually use.** `Rechnungen` is absent for Bauleitung —
 * `PermissionMatrix.md` §5 gives them no Invoice permission at all, so the link would lead to a 403.
 * That is presentation, not security: the endpoint refuses them regardless (CLAUDE.md §23).
 */
@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive],
  template: `
    <header class="bar">
      <div class="bar__inner">
        <span class="bar__brand">
          <span class="bar__mark" aria-hidden="true"></span>
          {{ t().app.name }}
        </span>

        <nav class="bar__nav" [attr.aria-label]="t().nav.mainNavigation">
          @for (item of navItems(); track item.route) {
            <a
              class="bar__link"
              [routerLink]="item.route"
              routerLinkActive="bar__link--active"
              >{{ item.label }}</a
            >
          }
        </nav>

        <!--
          The language switcher: deliberately small, low-contrast and set apart by a divider. It is a
          preference, not a destination, and must not compete with the navigation (D79).
        -->
        <div class="bar__lang" role="group" [attr.aria-label]="t().language.label">
          @for (option of languages; track option) {
            <button
              type="button"
              class="bar__lang-option"
              [class.bar__lang-option--active]="lang() === option"
              [attr.aria-pressed]="lang() === option"
              [attr.lang]="option"
              (click)="i18n.use(option)"
            >
              {{ option === 'de' ? t().language.de : t().language.en }}
            </button>
          }
        </div>

        @if (user(); as user) {
          <span class="bar__user">
            <span class="bar__user-name">{{ user.name }}</span>
            <span class="bar__user-role">{{ roleLabel() }}</span>
          </span>
        }

        <button type="button" class="bar__logout" (click)="logout()">{{ t().nav.logout }}</button>
      </div>
    </header>
  `,
  styles: `
    .bar {
      background: var(--rt-surface-inverse);
      color: var(--rt-text-inverse);
      box-shadow: var(--rt-shadow-2);
    }
    .bar__inner {
      display: flex;
      align-items: center;
      gap: var(--rt-space-5);
      height: var(--rt-header-height);
      max-width: var(--rt-content-max);
      margin: 0 auto;
      padding: 0 var(--rt-space-6);
    }
    .bar__brand {
      display: inline-flex;
      align-items: center;
      gap: var(--rt-space-2);
      font-size: 16px;
      font-weight: 700;
      letter-spacing: -0.01em;
      white-space: nowrap;
    }
    /* A small terracotta tile — the accent used as a mark, not as a colour wash. */
    .bar__mark {
      width: 13px;
      height: 13px;
      border-radius: 3px;
      background: var(--rt-accent);
      box-shadow: inset 0 0 0 2px rgb(255 255 255 / 18%);
    }
    .bar__nav {
      display: flex;
      align-items: center;
      gap: 2px;
      margin-right: auto;
      overflow-x: auto;
    }
    .bar__link {
      padding: var(--rt-space-2) var(--rt-space-3);
      border-radius: var(--rt-radius-sm);
      color: rgb(255 255 255 / 78%);
      font-size: 13.5px;
      font-weight: 500;
      text-decoration: none;
      white-space: nowrap;
      transition: background var(--rt-motion-fast) var(--rt-ease),
        color var(--rt-motion-fast) var(--rt-ease);
    }
    .bar__link:hover {
      background: rgb(255 255 255 / 8%);
      color: #fff;
    }
    /* Marked with the accent underline, not a filled pill — a pill competes with the wordmark. */
    .bar__link--active {
      color: #fff;
      box-shadow: inset 0 -2px 0 var(--rt-accent);
      border-radius: var(--rt-radius-sm) var(--rt-radius-sm) 0 0;
    }
    .bar__lang {
      display: flex;
      gap: 2px;
      padding-right: var(--rt-space-3);
      border-right: 1px solid rgb(255 255 255 / 16%);
    }
    .bar__lang-option {
      padding: 2px var(--rt-space-2);
      background: transparent;
      border: 0;
      border-radius: var(--rt-radius-sm);
      color: rgb(255 255 255 / 55%);
      font: inherit;
      font-size: 11px;
      font-weight: 700;
      letter-spacing: 0.04em;
      cursor: pointer;
    }
    .bar__lang-option:hover {
      color: rgb(255 255 255 / 85%);
    }
    .bar__lang-option--active {
      color: #fff;
      background: rgb(255 255 255 / 12%);
    }
    .bar__user {
      display: flex;
      flex-direction: column;
      align-items: flex-end;
      line-height: 1.25;
    }
    .bar__user-name {
      font-size: 12.5px;
      font-weight: 600;
    }
    .bar__user-role {
      font-size: 10.5px;
      opacity: 0.7;
    }
    .bar__logout {
      flex: none;
      padding: var(--rt-space-2) var(--rt-space-3);
      background: transparent;
      border: 0;
      color: rgb(255 255 255 / 82%);
      font: inherit;
      font-size: 13px;
      cursor: pointer;
    }
    .bar__logout:hover {
      color: #fff;
    }
    @media (max-width: 899px) {
      .bar__inner {
        gap: var(--rt-space-3);
        padding: 0 var(--rt-space-4);
      }
      .bar__user {
        display: none;
      }
    }
  `,
})
export class AppShell {
  protected readonly i18n = inject(I18n);
  private readonly auth = inject(Auth);
  private readonly router = inject(Router);

  protected readonly t = this.i18n.t;
  protected readonly lang = this.i18n.lang;
  protected readonly languages = LANGUAGES;
  protected readonly user = this.auth.user;

  protected readonly roleLabel = computed(() =>
    this.auth.role() === 'admin' ? this.t().roles.admin : this.t().roles.inspector,
  );

  /**
   * The workspaces this role may actually reach. Invoices are Admin-only (§5), so the link is
   * omitted rather than shown and refused — a dead control implies a capability.
   */
  protected readonly navItems = computed(() => {
    const t = this.t();
    const items = [
      { label: t.nav.cockpit, route: '/cockpit' },
      { label: t.nav.leads, route: '/leads' },
      { label: t.nav.inspections, route: '/besichtigungen' },
      { label: t.nav.angebote, route: '/angebote' },
    ];

    if (this.auth.role() === 'admin') {
      items.push({ label: t.nav.invoices, route: '/rechnungen' });
    }

    items.push({ label: t.nav.projects, route: '/projekte' });

    // Both roles browse the Catalog (§6 grants View F/F); only Admin sees the controls inside it.
    items.push({ label: t.nav.catalog, route: '/katalog' });

    // Operational, not commercial, and Admin-only (§9) — so it sits last rather than beside the
    // workspaces, and an Inspector never sees the link at all.
    if (this.auth.role() === 'admin') {
      items.push({ label: t.nav.notifications, route: '/benachrichtigungen' });
    }

    return items;
  });

  /**
   * Ends the session client-side. There is deliberately no logout endpoint (D60) — clearing the
   * refresh token is what makes this meaningful, which is why D73 refused `localStorage`.
   */
  protected logout(): void {
    this.auth.logout();
    void this.router.navigateByUrl('/anmelden');
  }
}
