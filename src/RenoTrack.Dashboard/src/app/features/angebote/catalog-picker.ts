import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { Subject, debounceTime, switchMap } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { CatalogItemDto } from '../../core/api/contracts';
import { RenoTrackApi } from '../../core/api/renotrack-api';
import { I18n } from '../../core/i18n/i18n';
import { Dialog } from '../../shared/ui/dialog';

/**
 * Wireframe D2 — the Catalog picker.
 *
 * **Retired entries never appear**, and there is no switch to include them: BR-12 makes retirement a
 * discovery rule, and the search endpoint has no flag for it by decision (D37). A line already built
 * from a retired entry keeps working (BR-14) — retirement affects what can be *found*, not what
 * exists.
 *
 * Selecting an entry hands its values back to the builder as a **starting point**. The server takes
 * description, specification and unit from the Catalog entry itself when `catalogItemId` is sent, so
 * what the form shows for those three is informative; quantity, price and VAT remain the
 * Inspector's, because a quote is priced per job.
 */
@Component({
  selector: 'app-catalog-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CurrencyPipe, Dialog],
  template: `
    <app-dialog
      [open]="open()"
      [heading]="t().catalog.pickerTitle"
      [description]="t().catalog.pickerHint"
      (closed)="cancelled.emit()"
    >
      <div class="rt-field">
        <label class="rt-field__label" for="catalog-search">{{ t().catalog.search }}</label>
        <input
          id="catalog-search"
          class="rt-input"
          type="search"
          autocomplete="off"
          [value]="term()"
          (input)="search($any($event.target).value)"
        />
      </div>

      @if (loading()) {
        <p class="rt-caption">{{ t().states.loading }}</p>
      } @else if (results().length === 0) {
        <p class="rt-caption">{{ term() ? t().catalog.noResults : t().catalog.searchPrompt }}</p>
      } @else {
        <ul class="results">
          @for (item of results(); track item.id) {
            <li>
              <button type="button" class="result" (click)="selected.emit(item)">
                <span class="result__title">{{ item.title }}</span>
                @if (item.defaultSpecification) {
                  <span class="result__spec">{{ item.defaultSpecification }}</span>
                }
                <span class="result__price">
                  {{ item.suggestedUnitPrice | currency: 'EUR' : 'symbol' : '1.2-2' : locale() }}
                  / {{ item.defaultUnit }}
                </span>
              </button>
            </li>
          }
        </ul>
      }

      <button dialogActions type="button" class="rt-button" (click)="cancelled.emit()">
        {{ t().actions.cancel }}
      </button>
    </app-dialog>
  `,
  styles: `
    .results {
      display: flex;
      flex-direction: column;
      gap: var(--rt-space-2);
      max-height: 46vh;
      margin: 0;
      padding: 0;
      overflow-y: auto;
      list-style: none;
    }
    .result {
      display: grid;
      width: 100%;
      gap: 2px;
      padding: var(--rt-space-3);
      background: var(--rt-surface-sunken);
      border: 1px solid transparent;
      border-radius: var(--rt-radius-md);
      font: inherit;
      text-align: left;
      cursor: pointer;
    }
    .result:hover {
      border-color: var(--rt-brand);
    }
    .result__title {
      font-size: 13.5px;
      font-weight: 600;
    }
    .result__spec {
      color: var(--rt-text-muted);
      font-size: 12px;
    }
    .result__price {
      color: var(--rt-brand);
      font-size: 12.5px;
      font-weight: 650;
      font-variant-numeric: tabular-nums;
    }
  `,
})
export class CatalogPicker {
  readonly open = input.required<boolean>();

  readonly selected = output<CatalogItemDto>();
  readonly cancelled = output<void>();

  private readonly api = inject(RenoTrackApi);
  private readonly i18n = inject(I18n);

  protected readonly t = this.i18n.t;
  protected readonly locale = this.i18n.locale;

  protected readonly term = signal('');
  protected readonly loading = signal(false);
  private readonly items = signal<readonly CatalogItemDto[]>([]);

  protected readonly results = computed(() => this.items());

  private readonly typed = new Subject<string>();

  constructor() {
    // Debounced, and `switchMap` so a slow earlier response can never overwrite a newer one — the
    // classic search race that shows results for a term the user has already replaced.
    //
    // **Deliberately no `distinctUntilChanged`.** It looks like an obvious optimisation and is a
    // real defect here: FR-4.10 lets any Inspector add a Catalog entry at any time — including from
    // the very dialog behind this one — so searching the same term twice must genuinely re-query.
    // Worse, suppressing the repeat means no response ever arrives to clear `loading`, and the
    // picker sits on "Wird geladen …" forever. Found by driving the real screen, not by review.
    this.typed
      .pipe(
        debounceTime(250),
        switchMap((term) => this.api.catalogItems(term)),
        takeUntilDestroyed(),
      )
      .subscribe({
        next: (page) => {
          this.items.set(page.items);
          this.loading.set(false);
        },
        error: () => {
          this.items.set([]);
          this.loading.set(false);
        },
      });
  }

  protected search(term: string): void {
    this.term.set(term);
    this.loading.set(true);
    this.typed.next(term);
  }
}
