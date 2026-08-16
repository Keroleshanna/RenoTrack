import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';

import { I18n } from '../../core/i18n/i18n';
import { Auth } from '../../core/auth/auth';
import { ApiError } from '../../core/api/api-error';
import { Cockpit, CockpitModel } from './cockpit-model';
import { FunnelChart, SegmentedBar } from '../../shared/charts/charts';
import { EmptyState, ErrorState, Skeleton } from '../../shared/ui/state-panels';
import { StatusChip } from '../../shared/ui/status-chip';

type State =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly cockpit: Cockpit }
  | { readonly kind: 'error'; readonly message: string };

/**
 * The Cockpit — the management view opened first after signing in.
 *
 * One question, answered in about ten seconds: **how is the business doing, and what needs me
 * today.** The reading order is the design:
 *
 * 1. **KPIs** — the state of the business in one row, each tile a link to the work behind it.
 * 2. **What needs you now** — the only part of the page that is *work* rather than information, so
 *    it sits high and carries the single accent on the screen.
 * 3. **From enquiry to order** — the funnel, with per-stage conversion and the money at the stage
 *    the API can actually attribute it to.
 * 4. **Invoices** — the invoiced total split paid / open / overdue.
 * 5. **Schedule and projects** — what is happening on site.
 *
 * ## Not a Leads screen
 *
 * The Kanban, the filter row and the search live in the Leads workspace, where inventory belongs. A
 * board answers "which item is where"; a cockpit has to answer "where is the business losing money",
 * and a column of cards cannot.
 *
 * ## Real data only
 *
 * Every figure comes from the API. Nothing is mocked, and a panel with no data says so rather than
 * rendering a zero that would read as a fact.
 */
@Component({
  selector: 'app-cockpit-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, FunnelChart, SegmentedBar, StatusChip, EmptyState, ErrorState, Skeleton],
  templateUrl: './cockpit-page.html',
  styleUrl: './cockpit-page.scss',
})
export class CockpitPage {
  private readonly model = inject(CockpitModel);
  private readonly auth = inject(Auth);
  private readonly i18n = inject(I18n);

  protected readonly t = this.i18n.t;
  protected readonly locale = this.i18n.locale;
  protected readonly user = this.auth.user;
  protected readonly isAdmin = computed(() => this.auth.role() === 'admin');

  protected readonly state = signal<State>({ kind: 'loading' });
  protected readonly loadedAt = signal<Date | null>(null);

  protected readonly cockpit = computed(() => {
    const state = this.state();
    return state.kind === 'ready' ? state.cockpit : null;
  });

  protected readonly errorMessage = computed(() => {
    const state = this.state();
    return state.kind === 'error' ? state.message : null;
  });

  protected readonly subtitle = computed(() =>
    this.isAdmin() ? this.t().cockpit.subtitleAdmin : this.t().cockpit.subtitleInspector,
  );

  /** A greeting that matches the clock — this screen is opened at 6am and at 7pm. */
  protected readonly greeting = computed(() => {
    const hour = new Date().getHours();
    const t = this.t().cockpit;
    return hour < 11 ? t.greetingMorning : hour < 18 ? t.greetingDay : t.greetingEvening;
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.set({ kind: 'loading' });

    this.model.load().subscribe({
      next: (cockpit) => {
        this.state.set({ kind: 'ready', cockpit });
        this.loadedAt.set(new Date());
      },
      // The server's own message is never shown — it is English and phrased for an API caller.
      // The status code is mapped onto the Dashboard's dictionary instead (CLAUDE.md §23).
      error: (error: unknown) => {
        const kind = error instanceof ApiError ? error.kind : 'server';
        this.state.set({ kind: 'error', message: this.t().errors[kind] });
      },
    });
  }
}
