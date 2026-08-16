import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';

import { RenoTrackApi } from '../../core/api/renotrack-api';
import {
  ANGEBOT_STATUSES,
  AngebotListItemDto,
  AngebotStatusDto,
  PagedResult,
} from '../../core/api/contracts';
import { StatusChip } from '../../shared/ui/status-chip';
import { FilterOption, WorkspaceChrome } from './workspace-chrome';
import { WorkspacePage } from './workspace-page';

/**
 * The Angebote workspace — every quote, its state and its value.
 *
 * **The commercial heart of the product.** `SRS.md` §1.1 says the whole system exists to replace a
 * 3–4 hour manual Word/Excel job, and this is where that work is tracked.
 *
 * Reached from the Cockpit's decision queue with `?status=InReview`, so the count on the tile and
 * the rows here are the same query and cannot disagree.
 *
 * Scoping is server-side: an Admin sees every quote, an Inspector only their own.
 */
@Component({
  selector: 'app-angebote-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CurrencyPipe, DatePipe, RouterLink, StatusChip, WorkspaceChrome],
  template: `
    <app-workspace-chrome
      [title]="t().angebote.title"
      [subtitle]="t().angebote.subtitle"
      [headlineLabel]="t().angebote.totalValue"
      [headlineValue]="pageValue()"
      [filters]="filters()"
      [filterLegend]="t().filters.status"
      [active]="statusFilter()"
      [state]="chromeState()"
      [errorMessage]="errorMessage() ?? ''"
      [showing]="showingLabel()"
      [hasPrevious]="hasPrevious()"
      [hasNext]="hasNext()"
      [previousLabel]="t().paging.previous"
      [nextLabel]="t().paging.next"
      (statusChange)="applyStatus($event)"
      (pageChange)="goToPage($event)"
      (reload)="load()"
    >
      <div class="rt-table-wrap">
        <table class="rt-table">
          <thead>
            <tr>
              <th>{{ t().angebote.columns.number }}</th>
              <th>{{ t().angebote.columns.customer }}</th>
              <th>{{ t().angebote.columns.status }}</th>
              <th class="rt-numeric">{{ t().angebote.columns.net }}</th>
              <th class="rt-numeric">{{ t().angebote.columns.gross }}</th>
              <th class="rt-numeric">{{ t().angebote.columns.created }}</th>
            </tr>
          </thead>
          <tbody>
            @for (angebot of rows(); track angebot.id) {
              <tr>
                <td class="rt-numeric">
                  <a class="row-link" [routerLink]="['/angebote', angebot.id]">{{
                    angebot.angebotNumber
                  }}</a>
                </td>
                <td>{{ angebot.leadName }}</td>
                <td><app-status-chip kind="angebot" [value]="angebot.status" /></td>
                <td class="rt-numeric">
                  {{ angebot.netTotal | currency: 'EUR' : 'symbol' : '1.2-2' : locale() }}
                </td>
                <td class="rt-numeric strong">
                  {{ angebot.grossTotal | currency: 'EUR' : 'symbol' : '1.2-2' : locale() }}
                </td>
                <td class="rt-numeric">
                  {{ angebot.createdAt | date: t().formats.date : undefined : locale() }}
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </app-workspace-chrome>
  `,
  styles: `
    .strong {
      font-weight: 650;
    }
    /* A link in the first cell rather than a click handler on the row: a quote is a destination, and
       a destination should be openable in a new tab and reachable by keyboard. */
    .row-link {
      color: var(--rt-brand);
      font-weight: 650;
      text-decoration: none;
    }
    .row-link:hover {
      text-decoration: underline;
    }
  `,
})
export class AngebotePage extends WorkspacePage<AngebotListItemDto> {
  private readonly api = inject(RenoTrackApi);

  protected readonly filters = computed<readonly FilterOption[]>(() => [
    { value: null, label: this.t().filters.allStatuses },
    ...ANGEBOT_STATUSES.map((status) => ({
      value: status,
      label: this.t().angebote.status[status],
    })),
  ]);

  protected readonly chromeState = computed(() => {
    const state = this.state();
    if (state.kind !== 'ready') {
      return state.kind;
    }
    return state.page.items.length ? ('ready' as const) : ('empty' as const);
  });

  /**
   * The gross value of **this page**, not of the whole filtered set.
   *
   * A whole-set total would need its own aggregate endpoint; summing the page and labelling it as
   * the company's quote volume would be a wrong number of exactly the kind a cockpit exists to
   * avoid. The Cockpit's own figure comes from the receivables/quote reads instead.
   */
  protected readonly pageValue = computed(() => {
    const total = this.rows().reduce((sum, row) => sum + row.grossTotal, 0);
    return new Intl.NumberFormat(this.locale(), {
      style: 'currency',
      currency: 'EUR',
      maximumFractionDigits: 0,
    }).format(total);
  });

  constructor() {
    super();
    this.initialise();
  }

  protected fetch(status: string | null, page: number): Observable<PagedResult<AngebotListItemDto>> {
    return this.api.angebote({
      status: (status as AngebotStatusDto | null) ?? undefined,
      page,
      pageSize: this.pageSize,
    });
  }
}
