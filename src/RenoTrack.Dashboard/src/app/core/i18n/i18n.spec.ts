import { DE } from './de';
import { EN } from './en';
import { I18n } from './i18n';

/**
 * The DE/EN contract.
 *
 * `en.ts` is annotated `Strings`, so a *missing* key is already a compile error. What a compile
 * check cannot catch is a key that exists in both but was never translated — copied German sitting
 * in the English dictionary. That is what the second test below is for.
 */
describe('i18n', () => {
  /** Every leaf, as `a.b.c` → value. */
  function flatten(value: unknown, prefix = ''): Record<string, string> {
    if (typeof value === 'string') {
      return { [prefix]: value };
    }
    if (typeof value !== 'object' || value === null) {
      return {};
    }

    return Object.entries(value).reduce<Record<string, string>>(
      (result, [key, child]) => ({
        ...result,
        ...flatten(child, prefix ? `${prefix}.${key}` : key),
      }),
      {},
    );
  }

  const german = flatten(DE);
  const english = flatten(EN);

  it('translates every German key', () => {
    expect(Object.keys(english).sort()).toEqual(Object.keys(german).sort());
  });

  /**
   * The allow-list is for values that are genuinely identical in both languages — proper nouns,
   * language codes, format patterns that happen to coincide, and words German borrowed from English.
   */
  it('leaves no English value identical to its German counterpart, except where that is correct', () => {
    const legitimatelyIdentical = new Set([
      'app.name',
      'app.dashboard',
      'nav.cockpit',
      'nav.leads',
      'leads.title',
      'leadSource.Website',
      'leadStatus.New',
      'language.de',
      'language.en',
      'filters.status',
      'a11y.statusLabel',
      'angebote.status.Draft',
      'invoices.status.Draft',
      'invoices.status.Sent',
      'projects.status.Active',
      'formats.time',
      // 'Status' is the same word in both languages, and 'LLL' is a pattern, not prose.
      'formats.month',
      'leads.columns.status',
      'angebote.columns.status',
      'invoices.columns.status',
      'projects.columns.status',
      'inspections.columns.status',
      'notifications.columns.status',
      'cockpit.money.total',
      // 'Name' and 'optional' are the same word in German and English.
      'leadForm.name',
      'leadForm.optional',
      // "Pos." abbreviates Position in both languages — it is the column marker on the printed
      // quote, not prose.
      'angebotDetail.position',
    ]);

    const untranslated = Object.keys(german).filter(
      (key) => !legitimatelyIdentical.has(key) && german[key] === english[key],
    );

    expect(untranslated).toEqual([]);
  });

  it('switches dictionary and locale together', () => {
    const i18n = new I18n();

    expect(i18n.lang()).toBe('de');
    expect(i18n.locale()).toBe('de-DE');
    expect(i18n.t().nav.invoices).toBe(DE.nav.invoices);

    i18n.use('en');

    expect(i18n.locale()).toBe('en-GB');
    expect(i18n.t().nav.invoices).toBe(EN.nav.invoices);
  });

  it('fills positional placeholders', () => {
    const i18n = new I18n();

    expect(i18n.format('{0}–{1} von {2}', 1, 25, 90)).toBe('1–25 von 90');
  });

  /**
   * German writes `04.08.2026`, English `4 Aug 2026`. Passing the active locale to a pipe translates
   * the words; only a per-language *pattern* fixes the order and the separators (D79).
   */
  it('carries a different date pattern per language, not just different month names', () => {
    expect(DE.formats.date).not.toBe(EN.formats.date);
  });
});
