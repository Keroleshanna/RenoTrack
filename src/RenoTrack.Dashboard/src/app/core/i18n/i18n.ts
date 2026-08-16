import { Injectable, computed, signal } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeDe from '@angular/common/locales/de';
import localeEnGb from '@angular/common/locales/en-GB';
import { DE } from './de';
import { EN } from './en';
import { LOCALE_BY_LANG, Lang, Strings } from './strings';

// Registered here rather than in `app.config.ts`, on purpose: this module is what promises callers a
// `locale()` they can hand to `DatePipe`/`CurrencyPipe`, so it is what must guarantee the data
// behind that locale exists. Registering at bootstrap instead leaves every component test throwing
// `NG0701: Missing locale data`, because a TestBed never runs `app.config`.
registerLocaleData(localeDe);
registerLocaleData(localeEnGb);

/**
 * The Dashboard's language (D79).
 *
 * **German is the default and stays the default.** English exists so a non-German-speaking user can
 * operate the workflow.
 *
 * **Deliberately not Angular's built-in i18n.** `$localize` compiles one bundle per locale and
 * cannot switch at runtime, which is precisely what a `DE | EN` toggle in the header needs. A
 * translation library would be a second dependency, which D75 forbids without an explicit decision.
 * A typed dictionary gives runtime switching, no new package, **and** compile-time completeness.
 */
@Injectable({ providedIn: 'root' })
export class I18n {
  private readonly current = signal<Lang>('de');

  readonly lang = this.current.asReadonly();

  /**
   * The active dictionary. Read as `t().nav.leads` in a template — because it is a signal, every
   * label re-renders on switch without a single component knowing the language changed.
   */
  readonly t = computed<Strings>(() => (this.current() === 'de' ? DE : EN));

  /**
   * The Angular locale for `DatePipe`/`DecimalPipe`/`CurrencyPipe`.
   *
   * Passed explicitly at each call site rather than provided through `LOCALE_ID`, which resolves
   * once at injection time and cannot follow a runtime switch.
   *
   * **Currency is always EUR and its value never changes with the language** — only its formatting
   * does (`1.234,56 €` vs `€1,234.56`). The money is the same money.
   */
  readonly locale = computed(() => LOCALE_BY_LANG[this.current()]);

  use(lang: Lang): void {
    this.current.set(lang);
  }

  /** Fills `{0}`, `{1}`, … in a dictionary string. */
  format(template: string, ...values: readonly (string | number)[]): string {
    return values.reduce<string>(
      (result, value, index) => result.replaceAll(`{${index}}`, String(value)),
      template,
    );
  }
}
