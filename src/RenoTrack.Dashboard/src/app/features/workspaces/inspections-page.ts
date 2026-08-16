import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';

import { RenoTrackApi } from '../../core/api/renotrack-api';
import { InspectionDetailDto } from '../../core/api/contracts';
import { ApiError } from '../../core/api/api-error';
import { I18n } from '../../core/i18n/i18n';
import { EmptyState, ErrorState, Skeleton } from '../../shared/ui/state-panels';

export type Range = 'week' | 'nextWeek' | 'month';

type State =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly items: readonly InspectionDetailDto[] }
  | { readonly kind: 'error'; readonly message: string };

/**
 * The Besichtigungen workspace — the operational schedule.
 *
 * **Filtered by a time window rather than paged**, and that is the endpoint's own shape: the result
 * is bounded by the range the user picks, so paging a week of site visits would make "this week" a
 * multi-request operation. The server caps the window so it cannot become "every visit ever".
 *
 * Scoping is server-side: an Admin sees the whole company's schedule, an Inspector only their own
 * assignments (`PermissionMatrix.md` §2 marks them "S").
 *
 * Grouped by day, because that is how the work is actually done — a flat list of thirty
 * appointments is a report, a list under date headings is a plan.
 */
@Component({
  selector: 'app-inspections-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, EmptyState, ErrorState, Skeleton],
  template: `
    <header class="head">
      <div>
        <h1 class="rt-display">{{ t().inspections.title }}</h1>
        <p class="rt-body rt-muted head__subtitle">{{ t().inspections.subtitle }}</p>
      </div>
      <div class="rt-panel__aside">
        <span class="rt-label">{{ t().inspections.count }}</span>
        <span class="rt-panel__aside-value">{{ count() }}</span>
      </div>
    </header>

    <section class="rt-panel">
      <div class="filters" role="group" [attr.aria-label]="t().filters.legend">
        @for (option of ranges; track option.value) {
          <button
            type="button"
            class="filters__chip"
            [class.filters__chip--active]="option.value === range()"
            [attr.aria-pressed]="option.value === range()"
            (click)="setRange(option.value)"
          >
            {{ label(option.value) }}
          </button>
        }
      </div>

      @switch (state().kind) {
        @case ('loading') {
          <app-skeleton [lineCount]="8" />
        }
        @case ('error') {
          <app-error-state [body]="errorMessage() ?? ''" (retry)="load()" />
        }
        @default {
          @if (days().length) {
            @for (day of days(); track day.key) {
              <div class="day">
                <p class="day__label">
                  {{ day.date | date: t().formats.weekday : undefined : locale() }}
                </p>
                @for (item of day.items; track item.id) {
                  <a
                    class="visit"
                    [class.visit--done]="item.completedAt"
                    [routerLink]="['/besichtigungen', item.id]"
                  >
                    <span class="visit__time">
                      {{ item.scheduledAt | date: t().formats.time : undefined : locale() }}
                    </span>
                    <span class="visit__body">
                      <span class="visit__customer">{{ item.leadName }}</span>
                      <span class="visit__address">{{ item.leadAddress ?? item.leadPhone }}</span>
                    </span>
                    <span class="visit__meta">
                      @if (item.photoCount > 0) {
                        <span class="visit__photos">{{ item.photoCount }} {{ t().inspections.photos }}</span>
                      }
                      <span class="visit__status">
                        {{ item.completedAt ? t().inspections.done : t().inspections.open }}
                      </span>
                    </span>
                  </a>
                }
              </div>
            }
          } @else {
            <app-empty-state />
          }
        }
      }
    </section>
  `,
  styles: `
    :host {
      display: block;
      max-width: var(--rt-content-max);
      margin: 0 auto;
      padding-bottom: var(--rt-space-16);
    }
    .head {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      flex-wrap: wrap;
      gap: var(--rt-space-4);
      margin-bottom: var(--rt-space-6);
    }
    .head__subtitle {
      margin-top: var(--rt-space-2);
    }
    .filters {
      display: flex;
      flex-wrap: wrap;
      gap: var(--rt-space-2);
      margin-bottom: var(--rt-space-5);
      padding-bottom: var(--rt-space-5);
      border-bottom: 1px solid var(--rt-border);
    }
    .filters__chip {
      padding: var(--rt-space-2) var(--rt-space-4);
      background: var(--rt-surface-raised);
      border: 1px solid var(--rt-border-strong);
      border-radius: var(--rt-radius-pill);
      font: inherit;
      font-size: 12.5px;
      font-weight: 600;
      color: var(--rt-text-muted);
      cursor: pointer;
    }
    .filters__chip--active {
      background: var(--rt-brand);
      border-color: var(--rt-brand);
      color: #fff;
    }
    .day + .day {
      margin-top: var(--rt-space-5);
    }
    .day__label {
      margin: 0 0 var(--rt-space-3);
      color: var(--rt-text-muted);
      font-size: 11px;
      font-weight: 700;
      letter-spacing: 0.06em;
      text-transform: uppercase;
    }
    /* A whole-row link: an appointment is a destination — the on-site screen (C3) — so it is a real
       anchor rather than a click handler, and stays openable in a new tab and reachable by keyboard. */
    .visit {
      display: grid;
      grid-template-columns: 56px 1fr auto;
      align-items: center;
      gap: var(--rt-space-4);
      padding: var(--rt-space-3) var(--rt-space-2);
      border-bottom: 1px solid var(--rt-border);
      border-radius: var(--rt-radius-sm);
      color: inherit;
      text-decoration: none;
    }
    .visit:hover {
      background: var(--rt-surface-sunken);
    }
    /* A completed visit stays on the plan — it shows what has been done — but recedes. */
    .visit--done {
      opacity: 0.55;
    }
    .visit__time {
      font-size: 13.5px;
      font-weight: 700;
      font-variant-numeric: tabular-nums;
      color: var(--rt-brand);
    }
    .visit__body {
      display: flex;
      flex-direction: column;
      gap: 1px;
      min-width: 0;
    }
    .visit__customer {
      font-size: 13.5px;
      font-weight: 600;
    }
    .visit__address {
      color: var(--rt-text-muted);
      font-size: 12px;
    }
    .visit__meta {
      display: flex;
      align-items: center;
      gap: var(--rt-space-3);
      color: var(--rt-text-muted);
      font-size: 11.5px;
    }
    .visit__status {
      font-weight: 650;
    }
    @media (max-width: 599px) {
      .visit {
        grid-template-columns: 48px 1fr;
      }
      .visit__meta {
        grid-column: 2;
      }
    }
  `,
})
export class InspectionsPage {
  private readonly api = inject(RenoTrackApi);
  private readonly i18n = inject(I18n);

  protected readonly t = this.i18n.t;
  protected readonly locale = this.i18n.locale;

  protected readonly ranges = [
    { value: 'week' as const },
    { value: 'nextWeek' as const },
    { value: 'month' as const },
  ];

  protected readonly range = signal<Range>('week');
  protected readonly state = signal<State>({ kind: 'loading' });

  protected readonly count = computed(() => {
    const state = this.state();
    return state.kind === 'ready' ? state.items.length : 0;
  });

  protected readonly errorMessage = computed(() => {
    const state = this.state();
    return state.kind === 'error' ? state.message : null;
  });

  /** Grouped by calendar day — a plan, not a report. */
  protected readonly days = computed(() => {
    const state = this.state();
    if (state.kind !== 'ready') {
      return [];
    }

    const groups = new Map<string, { key: string; date: Date; items: InspectionDetailDto[] }>();

    for (const item of state.items) {
      const date = new Date(item.scheduledAt);
      const key = `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`;
      const existing = groups.get(key);
      if (existing) {
        existing.items.push(item);
      } else {
        groups.set(key, { key, date, items: [item] });
      }
    }

    return [...groups.values()];
  });

  constructor() {
    this.load();
  }

  protected label(range: Range): string {
    const t = this.t().inspections;
    return range === 'week' ? t.rangeThisWeek : range === 'nextWeek' ? t.rangeNextWeek : t.rangeMonth;
  }

  protected setRange(range: Range): void {
    this.range.set(range);
    this.load();
  }

  protected load(): void {
    this.state.set({ kind: 'loading' });

    const { from, to } = rangeBounds(this.range(), new Date());

    this.api.inspections(from, to).subscribe({
      next: (items) => this.state.set({ kind: 'ready', items }),
      error: (error: unknown) => {
        const kind = error instanceof ApiError ? error.kind : 'server';
        this.state.set({ kind: 'error', message: this.t().errors[kind] });
      },
    });
  }
}

export function startOfDay(date: Date): Date {
  const copy = new Date(date);
  copy.setHours(0, 0, 0, 0);
  return copy;
}

export function addDays(date: Date, days: number): Date {
  const copy = new Date(date);
  copy.setDate(copy.getDate() + days);
  return copy;
}

/**
 * Midnight on the Monday of `date`'s own week.
 *
 * Monday, not Sunday: this is a German business calendar (ISO-8601), and "this week" has to mean
 * the week the office means. `getDay()` returns 0 for Sunday, which is the end of an ISO week, not
 * the start — the `|| 7` is what maps it to day 7 rather than day 0.
 */
export function startOfWeek(date: Date): Date {
  const start = startOfDay(date);
  return addDays(start, -((start.getDay() || 7) - 1));
}

/**
 * The half-open `[from, to)` window each filter means.
 *
 * **These were rolling windows and that was the bug.** "This week" ran today→+7 and "next 30 days"
 * ran today→+30, so with a single upcoming visit both filters returned the same row and the three
 * chips looked broken. Rolling ranges are a defensible design, but they are not what the labels
 * say, and a filter that does not mean its label is worse than one that is merely coarse.
 *
 * Now: the two week filters are **calendar** weeks, mutually exclusive and adjacent, so a visit
 * falls in exactly one of them. "Next 30 days" stays a rolling window from today, because that is
 * precisely what its label claims — it legitimately overlaps both weeks, and that overlap is the
 * point rather than a defect.
 *
 * `to` is exclusive, matching the API (`ScheduledAt >= from && ScheduledAt < to`), so no visit is
 * double-counted at a boundary and none falls through the gap between two ranges.
 */
export function rangeBounds(range: Range, now: Date): { from: Date; to: Date } {
  const weekStart = startOfWeek(now);

  switch (range) {
    case 'week':
      return { from: weekStart, to: addDays(weekStart, 7) };

    case 'nextWeek':
      return { from: addDays(weekStart, 7), to: addDays(weekStart, 14) };

    case 'month':
      // From today, not from the week start: nobody means "the last four days" by "next 30 days".
      return { from: startOfDay(now), to: addDays(startOfDay(now), 30) };
  }
}
