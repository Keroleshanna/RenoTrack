// @ts-check
const eslint = require('@eslint/js');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

/**
 * The Dashboard's lint configuration.
 *
 * **`npm run lint -- --max-warnings=0` is the only part of the frontend gate that turns a warning
 * into a failure.** `ng build` has no `--fail-on-warning`, so there is no Angular equivalent of
 * `TreatWarningsAsErrors` — this file plus TypeScript `strict` and `strictTemplates` are the teeth.
 * Do not weaken the flag to make a slice green (CLAUDE.md §14).
 *
 * Lint is not redundant with the build, and that was proven rather than assumed: a production build
 * exits 0 on a file carrying an unused import while lint exits 1 on the same file.
 */
module.exports = tseslint.config(
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      ...tseslint.configs.recommended,
      ...tseslint.configs.stylistic,
      ...angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix: 'app', style: 'camelCase' },
      ],
      '@angular-eslint/component-selector': [
        'error',
        { type: 'element', prefix: 'app', style: 'kebab-case' },
      ],
    },
  },
  {
    files: ['**/*.html'],
    extends: [...angular.configs.templateRecommended, ...angular.configs.templateAccessibility],
    rules: {},
  },
);
