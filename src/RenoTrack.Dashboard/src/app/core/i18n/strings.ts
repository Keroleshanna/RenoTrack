import { DE } from './de';

/**
 * Widens a literal-typed dictionary into its structural contract.
 *
 * `DE` is declared `as const`, so its values are literal types (`'Neu'`, not `string`). Useful
 * inside German code, and exactly wrong as a contract for a second language: asking `EN` to satisfy
 * `typeof DE` would demand that the English label for `New` *be the string* `'Neu'`. This keeps the
 * **shape** — every key, at every depth — and relaxes each leaf to `string`.
 */
type Widen<T> = {
  readonly [K in keyof T]: T[K] extends string ? string : Widen<T[K]>;
};

/**
 * The translation contract (D79).
 *
 * Derived from the German dictionary, because German is the primary language and the source of
 * truth for what the Dashboard says. Every other language is annotated with this type, which makes
 * **a missing translation a compile error rather than an English word appearing in a German UI**.
 */
export type Strings = Widen<typeof DE>;

/** The supported languages. German is the default. */
export const LANGUAGES = ['de', 'en'] as const;

export type Lang = (typeof LANGUAGES)[number];

/**
 * The Angular locale used for `DatePipe`/`DecimalPipe`/`CurrencyPipe` in each language.
 *
 * `en-GB` rather than `en-US`: this is a German company invoicing German customers, so
 * day-before-month reads correctly to the European audience actually using the English UI, and
 * nothing about the underlying values changes.
 */
export const LOCALE_BY_LANG: Readonly<Record<Lang, string>> = {
  de: 'de-DE',
  en: 'en-GB',
};
