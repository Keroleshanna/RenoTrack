import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';

import { RenoTrackApi } from '../../core/api/renotrack-api';
import {
  INVOICE_STATUSES,
  InvoiceListItemDto,
  InvoiceStatusDto,
  PagedResult,
  ReceivablesSummaryDto,
} from '../../core/api/contracts';
import { SegmentedBar } from '../../shared/charts/charts';
import { StatusChip } from '../../shared/ui/status-chip';
import { FilterOption, WorkspaceChrome } from './workspace-chrome';
import { WorkspacePage } from './workspace-page';

/**
 * The Rechnungen workspace — receivables, not a CRUD table.
 *
 * **Admin only.** `PermissionMatrix.md` §5 makes every Invoice action Admin-`F`, so the route is
 * role-guarded and the navigation link is omitted for Bauleitung. The endpoint refuses them
 * regardless — the guard is a courtesy (CLAUDE.md §23).
 *
 * The receivables band above the table is the point: a list of invoices tells you what exists, the
 * split tells you what it means. `overdue` is derived server-side as `Sent && dueDate < today` —
 * nothing ever writes `InvoiceStatus.Overdue`, because there is no scheduler and Phase 10 adds none.
 */
@Component({
  selector: 'app-invoices-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CurrencyPipe, DatePipe, RouterLink, StatusChip, SegmentedBar, WorkspaceChrome],
  templateUrl: './invoices-page.html',
  styles: `
    :host {
      display: block;
      max-width: var(--rt-content-max);
      margin: 0 auto;
    }
    .money {
      margin-bottom: var(--rt-space-5);
    }
    .strong {
      font-weight: 650;
    }
    .overdue {
      margin-left: var(--rt-space-2);
      color: var(--rt-money-overdue);
      font-size: 11px;
      font-weight: 650;
    }
    .row-link {
      color: var(--rt-brand);
      font-weight: 600;
      text-decoration: none;
    }
    .row-link:hover {
      text-decoration: underline;
    }
  `,
})
export class InvoicesPage extends WorkspacePage<InvoiceListItemDto> {
  private readonly api = inject(RenoTrackApi);

  protected readonly receivables = signal<ReceivablesSummaryDto | null>(null);

  protected readonly filters = computed<readonly FilterOption[]>(() => [
    { value: null, label: this.t().filters.allStatuses },
    ...INVOICE_STATUSES.map((status) => ({
      value: status,
      label: this.t().invoices.status[status],
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
   * Open is shown net of overdue, so the three segments sum to the invoiced total rather than
   * double-counting the overdue portion — which is a subset of open, not a sibling of it.
   */
  protected readonly segments = computed(() => {
    const r = this.receivables();
    if (!r) {
      return [];
    }
    const t = this.t().cockpit.money;

    return [
      {
        id: 'paid',
        label: t.paid,
        value: r.paidGross,
        formatted: this.money(r.paidGross),
        color: '--rt-money-paid',
      },
      {
        id: 'open',
        label: t.open,
        value: Math.max(0, r.openGross - r.overdueGross),
        formatted: this.money(Math.max(0, r.openGross - r.overdueGross)),
        color: '--rt-money-open',
      },
      {
        id: 'overdue',
        label: t.overdue,
        value: r.overdueGross,
        formatted: this.money(r.overdueGross),
        color: '--rt-money-overdue',
      },
    ];
  });

  constructor() {
    super();
    this.initialise();

    this.api.receivables(new Date()).subscribe({
      next: (summary) => this.receivables.set(summary),
      // The band is supplementary: if it fails the table is still useful, so this does not take the
      // whole screen into an error state.
      error: () => this.receivables.set(null),
    });
  }

  /** Derived exactly as the server derives it, so the badge and the aggregate always agree. */
  protected isOverdue(invoice: InvoiceListItemDto): boolean {
    return invoice.status === 'Sent' && new Date(invoice.dueDate) < new Date();
  }

  protected money(value: number): string {
    return new Intl.NumberFormat(this.locale(), {
      style: 'currency',
      currency: 'EUR',
      maximumFractionDigits: 0,
    }).format(value);
  }

  protected fetch(status: string | null, page: number): Observable<PagedResult<InvoiceListItemDto>> {
    // The Cockpit links here with ?overdue=true, which means "Sent and past due" rather than a
    // status — because no status is ever written for it.
    const overdueOnly = this.route.snapshot.queryParamMap.get('overdue') === 'true';
    const today = new Date();
    const isoToday = `${today.getFullYear()}-${`${today.getMonth() + 1}`.padStart(2, '0')}-${`${today.getDate()}`.padStart(2, '0')}`;

    return this.api.invoices({
      status: overdueOnly ? 'Sent' : ((status as InvoiceStatusDto | null) ?? undefined),
      dueBefore: overdueOnly ? isoToday : undefined,
      page,
      pageSize: this.pageSize,
    });
  }
}
