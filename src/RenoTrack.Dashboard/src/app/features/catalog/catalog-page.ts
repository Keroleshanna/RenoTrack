import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import { CatalogItemDto, PagedResult, STANDARD_UNITS } from '../../core/api/contracts';
import { RenoTrackApi } from '../../core/api/renotrack-api';
import { Auth } from '../../core/auth/auth';
import { Dialog } from '../../shared/ui/dialog';
import { Notifier } from '../../shared/ui/notifier';
import { WorkspaceChrome } from '../workspaces/workspace-chrome';
import { WorkspacePage } from '../workspaces/workspace-page';

/**
 * Wireframe **F1** — the shared service library behind every quote.
 *
 * **Both roles read it; only the office curates it.** `PermissionMatrix.md` §6 grants "View
 * Catalog" `F`/`F`, and Create/Edit/Retire to Admin alone — an Inspector contributes through
 * FR-4.10's "save as Catalog item" from inside a quote instead, which is the organic growth path
 * §6 describes. So this screen shows an Inspector the library and no management controls, and says
 * where their own contributions come from rather than leaving the absence unexplained.
 *
 * **"Delete" is retirement, never a row delete** (BR-12). A retired entry stops appearing in the
 * picker *and* here — retirement affects discovery only, and BR-14 keeps it valid as a direct
 * reference for a new line. There is deliberately no "show retired" toggle: the API provides no
 * flag to include them, because nothing in the documents asks to browse a graveyard.
 *
 * The status-filter slot the other workspaces use is empty here, because a Catalog entry has no
 * status — the one axis worth narrowing is the search term, which is what F1's own search box does.
 */
@Component({
  selector: 'app-catalog-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CurrencyPipe, DatePipe, ReactiveFormsModule, Dialog, WorkspaceChrome],
  template: `
    <app-workspace-chrome
      [title]="t().catalog.title"
      [subtitle]="t().catalog.subtitle"
      [headlineLabel]="t().catalog.count"
      [headlineValue]="total().toString()"
      [state]="chromeState()"
      [errorMessage]="errorMessage() ?? ''"
      [showing]="showingLabel()"
      [hasPrevious]="hasPrevious()"
      [hasNext]="hasNext()"
      [previousLabel]="t().paging.previous"
      [nextLabel]="t().paging.next"
      (pageChange)="goToPage($event)"
      (reload)="load()"
    >
      @if (isAdmin()) {
        <button chromeActions type="button" class="rt-button rt-button--primary" (click)="openCreate()">
          {{ t().catalog.newItem }}
        </button>
      }

      <div class="rt-table-wrap">
        <table class="rt-table">
          <thead>
            <tr>
              <th>{{ t().catalog.columns.title }}</th>
              <th>{{ t().catalog.columns.unit }}</th>
              <th class="rt-numeric">{{ t().catalog.columns.price }}</th>
              <th class="rt-numeric">{{ t().catalog.columns.created }}</th>
              @if (isAdmin()) {
                <th><span class="rt-visually-hidden">{{ t().actions.actionsColumn }}</span></th>
              }
            </tr>
          </thead>
          <tbody>
            @for (item of rows(); track item.id) {
              <tr>
                <td>
                  <span class="rt-table__primary">{{ item.title }}</span>
                  @if (item.defaultSpecification) {
                    <span class="rt-table__secondary">{{ item.defaultSpecification }}</span>
                  }
                </td>
                <td>{{ item.defaultUnit }}</td>
                <td class="rt-numeric">
                  {{ item.suggestedUnitPrice | currency: 'EUR' : 'symbol' : '1.2-2' : locale() }}
                </td>
                <td class="rt-numeric">
                  {{ item.createdAt | date: t().formats.date : undefined : locale() }}
                </td>
                @if (isAdmin()) {
                  <td>
                    <div class="actions">
                      <button
                        type="button"
                        class="rt-button rt-button--small"
                        (click)="openEdit(item)"
                      >
                        {{ t().actions.open }}
                      </button>
                      <button
                        type="button"
                        class="rt-button rt-button--small"
                        (click)="askRetire(item)"
                      >
                        {{ t().catalog.retire }}
                      </button>
                    </div>
                  </td>
                }
              </tr>
            }
          </tbody>
        </table>
      </div>

      <p class="rt-caption hint">
        {{ isAdmin() ? t().catalog.editHint : t().catalog.readOnlyHint }}
      </p>
      @if (!isAdmin()) {
        <p class="rt-caption hint">{{ t().catalog.contributeHint }}</p>
      }
    </app-workspace-chrome>

    <!-- Create and edit are one form: the fields are identical and only the target differs. -->
    <app-dialog
      [open]="formOpen()"
      [heading]="editing() ? t().catalog.editTitle : t().catalog.createTitle"
      [description]="t().catalog.editHint"
      (closed)="formOpen.set(false)"
    >
      <form [formGroup]="form">
        <div class="rt-field">
          <label class="rt-field__label" for="catalog-title">{{ t().catalog.itemTitle }}</label>
          <input id="catalog-title" type="text" class="rt-input" formControlName="title" />
          @if (form.controls.title.touched && form.controls.title.invalid) {
            <span class="rt-field__error">{{ t().angebotDetail.required }}</span>
          }
        </div>

        <div class="rt-field">
          <label class="rt-field__label" for="catalog-spec">
            {{ t().catalog.specification }} ({{ t().leadForm.optional }})
          </label>
          <textarea
            id="catalog-spec"
            class="rt-textarea"
            rows="3"
            formControlName="defaultSpecification"
          ></textarea>
        </div>

        <div class="fields">
          <div class="rt-field">
            <label class="rt-field__label" for="catalog-unit">{{ t().catalog.unit }}</label>
            <!--
              A select over the five standard codes with an escape hatch to free text — the same
              shape the Angebot line-item form uses, and for the same reason: ItemUnit is a
              deliberately *open* value object, so an unrecognised code becomes a custom unit
              rather than an error. Offering only a select would quietly remove a capability the
              Domain supports; offering only a text box would invite typos where a standard code
              was meant.
            -->
            @if (customUnit()) {
              <input id="catalog-unit" class="rt-input" formControlName="defaultUnitCode" />
            } @else {
              <select id="catalog-unit" class="rt-select" formControlName="defaultUnitCode">
                @for (unit of units; track unit) {
                  <option [value]="unit">{{ unit }}</option>
                }
              </select>
            }
            <button
              type="button"
              class="rt-button rt-button--small unit-toggle"
              (click)="toggleCustomUnit(!customUnit())"
            >
              {{ customUnit() ? t().catalog.useStandardUnit : t().catalog.useCustomUnit }}
            </button>
          </div>

          <div class="rt-field">
            <label class="rt-field__label" for="catalog-price">{{ t().catalog.price }}</label>
            <input
              id="catalog-price"
              type="number"
              min="0"
              step="0.01"
              class="rt-input"
              formControlName="suggestedUnitPrice"
            />
            @if (form.controls.suggestedUnitPrice.touched && form.controls.suggestedUnitPrice.invalid) {
              <span class="rt-field__error">{{ t().angebotDetail.required }}</span>
            }
          </div>
        </div>
      </form>

      <ng-container dialogActions>
        <button type="button" class="rt-button" (click)="formOpen.set(false)">
          {{ t().actions.cancel }}
        </button>
        <button
          type="button"
          class="rt-button rt-button--primary"
          [disabled]="busy()"
          (click)="save()"
        >
          {{ t().actions.save }}
        </button>
      </ng-container>
    </app-dialog>

    <!-- Retirement is confirmed because it moves an entry out of everyone else's reach (D83). -->
    <app-dialog
      [open]="retireOpen()"
      [heading]="t().catalog.retireTitle"
      [description]="t().catalog.retireBody"
      (closed)="retireOpen.set(false)"
    >
      <p class="rt-body">{{ retiring()?.title }}</p>

      <ng-container dialogActions>
        <button type="button" class="rt-button" (click)="retireOpen.set(false)">
          {{ t().actions.cancel }}
        </button>
        <button
          type="button"
          class="rt-button rt-button--danger"
          [disabled]="busy()"
          (click)="retire()"
        >
          {{ t().catalog.retire }}
        </button>
      </ng-container>
    </app-dialog>
  `,
  styles: `
    .hint {
      margin-top: var(--rt-space-4);
    }
    .actions {
      display: flex;
      gap: var(--rt-space-2);
      justify-content: flex-end;
    }
    .fields {
      display: grid;
      gap: var(--rt-space-4);
      grid-template-columns: 1fr 1fr;
    }
    @media (max-width: 599px) {
      .fields {
        grid-template-columns: 1fr;
      }
    }
  `,
})
export class CatalogPage extends WorkspacePage<CatalogItemDto> {
  private readonly api = inject(RenoTrackApi);
  private readonly auth = inject(Auth);
  private readonly notifier = inject(Notifier);

  protected readonly isAdmin = computed(() => this.auth.role() === 'admin');

  protected readonly busy = signal(false);
  protected readonly formOpen = signal(false);
  protected readonly retireOpen = signal(false);

  /** The entry being edited, or `null` while creating — the one thing that differs between them. */
  protected readonly editing = signal<CatalogItemDto | null>(null);
  protected readonly retiring = signal<CatalogItemDto | null>(null);

  protected readonly units = STANDARD_UNITS;

  /** Whether the unit field is a free-text box rather than the standard-code picker. */
  protected readonly customUnit = signal(false);

  protected readonly form = new FormGroup({
    title: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    defaultSpecification: new FormControl('', { nonNullable: true }),
    defaultUnitCode: new FormControl<string>(STANDARD_UNITS[0], { nonNullable: true }),
    suggestedUnitPrice: new FormControl<number | null>(null, {
      validators: [Validators.required, Validators.min(0)],
    }),
  });

  protected readonly chromeState = computed(() => {
    const state = this.state();
    if (state.kind !== 'ready') {
      return state.kind;
    }
    return state.page.items.length ? ('ready' as const) : ('empty' as const);
  });

  constructor() {
    super();
    this.initialise();
  }

  protected fetch(_status: string | null, page: number): Observable<PagedResult<CatalogItemDto>> {
    return this.api.catalogPage({ page, pageSize: this.pageSize });
  }

  protected toggleCustomUnit(custom: boolean): void {
    this.customUnit.set(custom);

    // Switching back to the picker must land on a value the picker can actually show, or the
    // control would display the first option while the form still held the custom text.
    if (!custom && !STANDARD_UNITS.includes(this.form.getRawValue().defaultUnitCode as never)) {
      this.form.patchValue({ defaultUnitCode: STANDARD_UNITS[0] });
    }
  }

  protected openCreate(): void {
    this.editing.set(null);
    this.customUnit.set(false);
    this.form.reset({
      title: '',
      defaultSpecification: '',
      defaultUnitCode: STANDARD_UNITS[0],
      suggestedUnitPrice: null,
    });
    this.formOpen.set(true);
  }

  protected openEdit(item: CatalogItemDto): void {
    this.editing.set(item);

    // An entry stored with a custom unit opens in the text box, so editing it does not silently
    // rewrite the unit to a standard code the user never chose.
    this.customUnit.set(!STANDARD_UNITS.includes(item.defaultUnit as never));
    this.form.reset({
      title: item.title,
      defaultSpecification: item.defaultSpecification ?? '',
      // The read DTO's resolved unit is its own code, so it round-trips into the select as-is.
      defaultUnitCode: item.defaultUnit,
      suggestedUnitPrice: item.suggestedUnitPrice,
    });
    this.formOpen.set(true);
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const payload = {
      title: value.title,
      // An empty box means "no specification", not the empty string — the column is nullable.
      defaultSpecification: value.defaultSpecification.trim() || null,
      defaultUnitCode: value.defaultUnitCode,
      suggestedUnitPrice: value.suggestedUnitPrice!,
    };

    const target = this.editing();
    this.busy.set(true);

    const request = target
      ? this.api.updateCatalogItem(target.id, payload)
      : this.api.createCatalogItem(payload);

    request.subscribe({
      next: () => {
        this.busy.set(false);
        this.formOpen.set(false);
        this.notifier.success(target ? this.t().catalog.itemUpdated : this.t().catalog.itemCreated);
        this.load();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.notifier.error(this.messageFor(error));
      },
    });
  }

  protected askRetire(item: CatalogItemDto): void {
    this.retiring.set(item);
    this.retireOpen.set(true);
  }

  protected retire(): void {
    const target = this.retiring();
    if (!target) {
      return;
    }

    this.busy.set(true);

    this.api.retireCatalogItem(target.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.retireOpen.set(false);
        this.notifier.success(this.t().catalog.itemRetired);
        // Re-read rather than splicing the row out: the entry leaves the list because the server
        // stopped returning it, which is the same fact the picker will act on (D81).
        this.load();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.notifier.error(this.messageFor(error));
      },
    });
  }

  private messageFor(error: unknown): string {
    const kind = error instanceof ApiError ? error.kind : 'server';
    return this.t().errors[kind];
  }
}
