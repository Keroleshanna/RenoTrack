import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map, startWith } from 'rxjs';

import { AppShell } from './layout/app-shell';
import { I18n } from './core/i18n/i18n';
import { Notices } from './shared/ui/notifier';

/**
 * The application shell: top app bar, then the routed workspace.
 *
 * The chrome is hidden on the login screen — it is a standalone card, and a signed-out user has
 * nothing to navigate to.
 */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, AppShell, Notices],
  template: `
    <a class="skip-link" href="#main">{{ t().nav.skipToContent }}</a>

    @if (showChrome()) {
      <app-shell />
    }

    <main id="main" class="main" tabindex="-1">
      <div class="main__content" [class.main__content--bare]="!showChrome()">
        <router-outlet />
      </div>
    </main>

    <!-- Hosted once, above every screen: a write action's confirmation should not depend on which
         screen performed it, and a navigation away must not swallow the acknowledgement. -->
    <app-notices />
  `,
  styles: `
    .skip-link {
      position: absolute;
      left: -9999px;
      z-index: 10;
      padding: var(--rt-space-3) var(--rt-space-4);
      background: var(--rt-surface-raised);
      border-radius: var(--rt-radius-md);
      box-shadow: var(--rt-shadow-2);
    }
    .skip-link:focus {
      left: var(--rt-space-4);
      top: var(--rt-space-4);
    }
    .main {
      display: block;
      outline: none;
    }
    .main__content {
      padding: var(--rt-space-8) var(--rt-space-6);
    }
    /* The login screen owns its own full-height layout. */
    .main__content--bare {
      padding: 0;
    }
    @media (max-width: 899px) {
      .main__content {
        padding: var(--rt-space-5) var(--rt-space-4);
      }
    }
  `,
})
export class App {
  private readonly router = inject(Router);

  protected readonly t = inject(I18n).t;

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
      startWith(this.router.url),
    ),
    { initialValue: this.router.url },
  );

  protected readonly showChrome = computed(() => !this.url().startsWith('/anmelden'));
}
