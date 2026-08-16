import {
  ApplicationConfig,
  DEFAULT_CURRENCY_CODE,
  LOCALE_ID,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideRouter, withInMemoryScrolling } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';

import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth-interceptor';

// Locale data for both languages is registered by `core/i18n/i18n.ts`, not here: the module that
// hands out `locale()` is what must guarantee that locale is usable, and registering it there means
// component tests get it too (a TestBed never runs this file).
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withInMemoryScrolling({ scrollPositionRestoration: 'enabled' })),

    // Every call goes to a relative `/api/v1/...` path (D74), so the same code works unchanged under
    // Architecture.md §13's same-origin hosting option; `proxy.conf.json` bridges the two ports in
    // development only. The interceptor attaches the bearer token and turns a single 401 into a
    // refresh-and-replay.
    provideHttpClient(withInterceptors([authInterceptor])),

    provideAnimationsAsync(),

    // The default for anything that does not pass a locale explicitly. Pipes that must follow the
    // runtime language switch take `I18n.locale()` as their locale argument instead — `LOCALE_ID`
    // resolves once at injection time and cannot change afterwards (D79).
    { provide: LOCALE_ID, useValue: 'de-DE' },
    { provide: DEFAULT_CURRENCY_CODE, useValue: 'EUR' },
  ],
};
