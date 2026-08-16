import { Injectable, inject } from '@angular/core';
import { formatCurrency, formatNumber } from '@angular/common';
import { Observable, forkJoin, map, of } from 'rxjs';

import { RenoTrackApi } from '../../core/api/renotrack-api';
import { Auth, Role } from '../../core/auth/auth';
import { I18n } from '../../core/i18n/i18n';
import { FunnelRow, Segment } from '../../shared/charts/charts';
import {
  AngebotListItemDto,
  InspectionDetailDto,
  InvoiceListItemDto,
  LEAD_STATUSES,
  LeadDto,
  LeadStatusDto,
  PAGE_SIZE_MAX,
  ProjectListItemDto,
  ReceivablesSummaryDto,
} from '../../core/api/contracts';

/** A headline figure, already formatted for the active language. */
export interface Kpi {
  readonly id: string;
  readonly label: string;
  readonly value: string;
  readonly hint: string;
  readonly tone: 'neutral' | 'good' | 'warn' | 'bad';
  readonly route?: readonly unknown[];
  readonly queryParams?: Readonly<Record<string, string>>;
}

/** One thing waiting on this user. Every entry routes somewhere real. */
export interface Decision {
  readonly id: string;
  readonly severity: 'high' | 'medium' | 'info';
  readonly count: number;
  readonly title: string;
  readonly hint: string;
  readonly value: string | null;
  readonly route: readonly unknown[];
  readonly queryParams?: Readonly<Record<string, string>>;
}

export interface Appointment {
  readonly id: number;
  readonly time: string;
  readonly customer: string;
  readonly address: string;
  readonly done: boolean;
}

export interface ProjectRow {
  readonly id: number;
  readonly customer: string;
  readonly angebotNumber: string;
  readonly status: string;
  readonly agreed: string;
}

export interface Cockpit {
  readonly kpis: readonly Kpi[];
  readonly decisions: readonly Decision[];
  readonly funnel: readonly FunnelRow[];
  readonly funnelSummary: string | null;
  readonly money: readonly Segment[] | null;
  readonly moneyTotal: string;
  readonly today: readonly Appointment[];
  readonly tomorrow: readonly Appointment[];
  readonly projects: readonly ProjectRow[];
}

/**
 * Builds the Cockpit from the real API.
 *
 * ## Role is a change of priority, not a change of product
 *
 * Verwaltung and Bauleitung see **the same screen with different content in the same slots**, not
 * two dashboards. `PermissionMatrix.md` decides which is which, and it decides more than emphasis:
 * §5 makes every Invoice action Admin-only, so an Inspector's requests never even *ask* for money —
 * a 403 is not something to render around, it is something not to request. A queue a role cannot act
 * on is noise, and showing it implies a permission that role does not have.
 *
 * ## Everything is formatted here, nothing in the template
 *
 * Currency, counts and percentages are produced against the **active language** (D79: German writes
 * `1.234,56 €`, English `€1,234.56`). The value never changes, only its presentation, and currency
 * is always EUR. Doing it in one place keeps every formatting decision together.
 *
 * ## Counts come from `totalCount`, never from a page
 *
 * Each per-status figure is a `pageSize=1` request whose `totalCount` is exact regardless of table
 * size. Paging the whole table to bucket it client-side would silently under-count past page one.
 */
@Injectable({ providedIn: 'root' })
export class CockpitModel {
  private readonly api = inject(RenoTrackApi);
  private readonly auth = inject(Auth);
  private readonly i18n = inject(I18n);

  load(now = new Date()): Observable<Cockpit> {
    const role = this.auth.role() ?? 'admin';
    const tomorrow = addDays(now, 1);
    const dayAfter = addDays(now, 2);
    const weekEnd = addDays(now, 7);

    return forkJoin({
      // One request per Lead status: TotalCount is exact, and seven single-row reads cost less than
      // paging the table.
      leadCounts: forkJoin(
        Object.fromEntries(
          LEAD_STATUSES.map((status) => [
            status,
            this.api.leads({ status, page: 1, pageSize: 1 }).pipe(map((page) => page.totalCount)),
          ]),
        ) as Record<LeadStatusDto, Observable<number>>,
      ),
      recentLeads: this.api.leads({ page: 1, pageSize: PAGE_SIZE_MAX }).pipe(map((p) => p.items)),
      quotesInReview: this.api.angebote({ status: 'InReview', page: 1, pageSize: PAGE_SIZE_MAX }),
      quotesSent: this.api.angebote({ status: 'Sent', page: 1, pageSize: PAGE_SIZE_MAX }),
      quotesDraft: this.api.angebote({ status: 'Draft', page: 1, pageSize: 1 }),

      // A returned quote is outstanding work for its author, exactly like an unfinished draft, and
      // it is the one state with an explicit instruction attached to it.
      quotesChangesRequested: this.api.angebote({
        status: 'ChangesRequested',
        page: 1,
        pageSize: PAGE_SIZE_MAX,
      }),
      today: this.api.inspections(now, tomorrow),
      tomorrow: this.api.inspections(tomorrow, dayAfter),
      week: this.api.inspections(now, weekEnd),

      // Admin-only reads. PermissionMatrix.md §5 gives Bauleitung no Invoice permission at all, so
      // their Cockpit does not request them — a 403 avoided by not asking, rather than handled.
      receivables: role === 'admin' ? this.api.receivables(now) : of(null),
      overdue:
        role === 'admin'
          ? this.api.invoices({ status: 'Sent', dueBefore: isoDate(now), page: 1, pageSize: PAGE_SIZE_MAX })
          : of(null),
      projects: this.api.projects({ status: 'Active', page: 1, pageSize: PAGE_SIZE_MAX }),
    }).pipe(
      map((data) =>
        this.compose(role, {
          leadCounts: data.leadCounts,
          recentLeads: data.recentLeads,
          quotesInReview: data.quotesInReview.items,
          quotesInReviewCount: data.quotesInReview.totalCount,
          quotesSent: data.quotesSent.items,
          quotesSentCount: data.quotesSent.totalCount,
          draftCount: data.quotesDraft.totalCount,
          changesRequested: data.quotesChangesRequested.items,
          changesRequestedCount: data.quotesChangesRequested.totalCount,
          today: data.today,
          tomorrow: data.tomorrow,
          weekCount: data.week.length,
          receivables: data.receivables,
          overdue: data.overdue?.items ?? [],
          projects: data.projects.items,
          projectCount: data.projects.totalCount,
          now,
        }),
      ),
    );
  }

  // -----------------------------------------------------------------------------------------------

  private compose(
    role: Role,
    d: {
      leadCounts: Record<LeadStatusDto, number>;
      recentLeads: readonly LeadDto[];
      quotesInReview: readonly AngebotListItemDto[];
      quotesInReviewCount: number;
      quotesSent: readonly AngebotListItemDto[];
      quotesSentCount: number;
      draftCount: number;
      changesRequested: readonly AngebotListItemDto[];
      changesRequestedCount: number;
      today: readonly InspectionDetailDto[];
      tomorrow: readonly InspectionDetailDto[];
      weekCount: number;
      receivables: ReceivablesSummaryDto | null;
      overdue: readonly InvoiceListItemDto[];
      projects: readonly ProjectListItemDto[];
      projectCount: number;
      now: Date;
    },
  ): Cockpit {

    const reviewValue = d.quotesInReview.reduce((sum, q) => sum + q.grossTotal, 0);
    const sentValue = d.quotesSent.reduce((sum, q) => sum + q.grossTotal, 0);

    return {
      kpis: role === 'admin' ? this.adminKpis(d, sentValue) : this.inspectorKpis(d),
      decisions: this.decisions(role, d, reviewValue, sentValue),
      funnel: this.funnel(d.leadCounts, sentValue),
      funnelSummary: this.funnelSummary(d.leadCounts),
      money: d.receivables ? this.moneySegments(d.receivables) : null,
      moneyTotal: d.receivables ? this.money(d.receivables.invoicedGross) : '',
      today: d.today.map((i) => this.appointment(i)),
      tomorrow: d.tomorrow.map((i) => this.appointment(i)),
      projects: d.projects.slice(0, 6).map((p) => ({
        id: p.id,
        customer: p.customerName,
        angebotNumber: p.angebotNumber,
        status: p.status,
        agreed: this.money(p.agreedTotal),
      })),
    };
  }

  private adminKpis(
    d: { receivables: ReceivablesSummaryDto | null; projectCount: number; quotesInReviewCount: number; overdue: readonly InvoiceListItemDto[] },
    sentValue: number,
  ): readonly Kpi[] {
    const t = this.i18n.t().cockpit.kpi;
    const r = d.receivables;

    return [
      {
        id: 'revenue',
        label: t.revenue,
        value: this.money(r?.paidGross ?? 0),
        hint: t.revenueHint,
        tone: 'good',
        route: ['/rechnungen'],
        queryParams: { status: 'Paid' },
      },
      {
        id: 'open-quotes',
        label: t.openQuotes,
        value: this.money(sentValue),
        hint: t.openQuotesHint,
        tone: 'neutral',
        route: ['/angebote'],
        queryParams: { status: 'Sent' },
      },
      {
        id: 'outstanding',
        label: t.outstanding,
        value: this.money(r?.openGross ?? 0),
        hint: t.outstandingHint,
        tone: (r?.openGross ?? 0) > 0 ? 'warn' : 'neutral',
        route: ['/rechnungen'],
      },
      {
        id: 'overdue',
        label: t.overdue,
        value: this.money(r?.overdueGross ?? 0),
        hint: t.overdueHint,
        tone: (r?.overdueGross ?? 0) > 0 ? 'bad' : 'good',
        route: ['/rechnungen'],
        queryParams: { overdue: 'true' },
      },
      {
        id: 'projects',
        label: t.projects,
        value: this.count(d.projectCount),
        hint: t.projectsHint,
        tone: 'neutral',
        route: ['/projekte'],
      },
      {
        id: 'decisions',
        label: t.decisions,
        value: this.count(d.quotesInReviewCount + (r?.overdueCount ?? 0)),
        hint: t.decisionsHint,
        tone: d.quotesInReviewCount + (r?.overdueCount ?? 0) > 0 ? 'warn' : 'good',
      },
    ];
  }

  /**
   * Site management's row.
   *
   * **No money anywhere**, and not merely as emphasis: `PermissionMatrix.md` §5 gives Bauleitung no
   * Invoice permission, so leading their screen with revenue would show them a figure they cannot
   * influence in place of the work they can.
   */
  private inspectorKpis(d: {
    today: readonly InspectionDetailDto[];
    weekCount: number;
    leadCounts: Record<LeadStatusDto, number>;
    draftCount: number;
    projectCount: number;
  }): readonly Kpi[] {
    const t = this.i18n.t().cockpit.kpi;
    const openToday = d.today.filter((i) => !i.completedAt).length;

    return [
      {
        id: 'today',
        label: t.today,
        value: this.count(openToday),
        hint: t.todayHint,
        tone: openToday > 0 ? 'warn' : 'neutral',
        route: ['/besichtigungen'],
      },
      {
        id: 'quotes-to-write',
        label: t.quotesToWrite,
        value: this.count(d.leadCounts.InspectionDone),
        hint: t.quotesToWriteHint,
        tone: d.leadCounts.InspectionDone > 0 ? 'warn' : 'good',
        route: ['/leads'],
        queryParams: { status: 'InspectionDone' },
      },
      {
        id: 'my-quotes',
        label: t.myQuotes,
        value: this.count(d.draftCount),
        hint: t.myQuotesHint,
        tone: 'neutral',
        route: ['/angebote'],
      },
      {
        id: 'week',
        label: t.week,
        value: this.count(d.weekCount),
        hint: t.weekHint,
        tone: 'neutral',
        route: ['/besichtigungen'],
      },
      {
        id: 'projects',
        label: t.projects,
        value: this.count(d.projectCount),
        hint: t.projectsHint,
        tone: 'neutral',
        route: ['/projekte'],
      },
      {
        id: 'scheduled',
        label: this.i18n.t().leadStatus.InspectionScheduled,
        value: this.count(d.leadCounts.InspectionScheduled),
        hint: t.weekHint,
        tone: 'neutral',
        route: ['/leads'],
        queryParams: { status: 'InspectionScheduled' },
      },
    ];
  }

  private decisions(
    role: Role,
    d: {
      leadCounts: Record<LeadStatusDto, number>;
      quotesInReviewCount: number;
      quotesSentCount: number;
      draftCount: number;
      changesRequestedCount: number;
      today: readonly InspectionDetailDto[];
      receivables: ReceivablesSummaryDto | null;
    },
    reviewValue: number,
    sentValue: number,
  ): readonly Decision[] {
    const t = this.i18n.t().cockpit.decisions;
    const openToday = d.today.filter((i) => !i.completedAt).length;

    const all: Decision[] =
      role === 'admin'
        ? [
            {
              id: 'quotes-to-approve',
              severity: 'high',
              count: d.quotesInReviewCount,
              title: t.quotesToApprove,
              hint: t.quotesToApproveHint,
              value: this.money(reviewValue),
              route: ['/angebote'],
              queryParams: { status: 'InReview' },
            },
            {
              id: 'invoices-overdue',
              severity: 'high',
              count: d.receivables?.overdueCount ?? 0,
              title: t.invoicesOverdue,
              hint: t.invoicesOverdueHint,
              value: this.money(d.receivables?.overdueGross ?? 0),
              route: ['/rechnungen'],
              queryParams: { overdue: 'true' },
            },
            {
              id: 'leads-to-schedule',
              severity: 'medium',
              count: d.leadCounts.New,
              title: t.leadsToSchedule,
              hint: t.leadsToScheduleHint,
              value: null,
              route: ['/leads'],
              queryParams: { status: 'New' },
            },
            {
              id: 'awaiting-customer',
              severity: 'info',
              count: d.quotesSentCount,
              title: t.awaitingCustomer,
              hint: t.awaitingCustomerHint,
              value: this.money(sentValue),
              route: ['/angebote'],
              queryParams: { status: 'Sent' },
            },
          ]
        : [
            {
              id: 'inspections-today',
              severity: 'high',
              count: openToday,
              title: t.inspectionsToday,
              hint: t.inspectionsTodayHint,
              value: null,
              route: ['/besichtigungen'],
            },
            // A returned quote is the most urgent thing an Inspector can be holding: the office is
            // waiting on it and it carries an explicit instruction. Listed above the unwritten ones.
            {
              id: 'quotes-changes-requested',
              severity: 'high',
              count: d.changesRequestedCount,
              title: t.quotesChangesRequested,
              hint: t.quotesChangesRequestedHint,
              value: null,
              route: ['/angebote'],
              queryParams: { status: 'ChangesRequested' },
            },
            {
              id: 'quotes-to-write',
              severity: 'high',

              // Leads whose visit is done and have no quote yet, **plus** quotes already started
              // and not yet submitted.
              //
              // Counting only `InspectionDone` was a real defect: creating the draft moves the Lead
              // to `AngebotInProgress`, so the task vanished the moment the Inspector opened the
              // builder — while the quote was still empty and unsubmitted. The work disappeared
              // from the list precisely when it started.
              count: d.leadCounts.InspectionDone + d.draftCount,
              title: t.quotesToWrite,
              hint: t.quotesToWriteHint,
              value: null,

              // To the quotes workspace when there are drafts to finish, since that is where the
              // work is; to the pipeline when the only outstanding item is a quote not yet begun.
              route: d.draftCount > 0 ? ['/angebote'] : ['/leads'],
              queryParams: d.draftCount > 0 ? { status: 'Draft' } : { status: 'InspectionDone' },
            },
            {
              id: 'awaiting-customer',
              severity: 'info',
              count: d.quotesSentCount,
              title: t.awaitingCustomer,
              hint: t.awaitingCustomerHint,
              value: null,
              route: ['/angebote'],
              queryParams: { status: 'Sent' },
            },
          ];

    // An empty queue is not shown as a zero. A cockpit lists work, and "0 overdue invoices" is not
    // work — it is the absence of it.
    return all.filter((entry) => entry.count > 0);
  }

  /**
   * The pipeline as a funnel over the six non-terminal-plus-Won stages.
   *
   * `Lost` is deliberately absent: a funnel shows progression, and a lost enquiry left it. The
   * conversion line below reports the loss instead, where it belongs.
   */
  private funnel(counts: Record<LeadStatusDto, number>, sentValue: number): readonly FunnelRow[] {
    const labels = this.i18n.t().cockpit.funnel.stages;
    const stages: readonly LeadStatusDto[] = [
      'New',
      'InspectionScheduled',
      'InspectionDone',
      'AngebotInProgress',
      'AngebotSent',
      'Won',
    ];

    const widest = Math.max(1, ...stages.map((stage) => counts[stage]));

    return stages.map((stage, index) => {
      const previousStage = index > 0 ? stages[index - 1] : null;
      const previous = previousStage ? counts[previousStage] : null;

      return {
        id: stage,
        label: labels[stage as keyof typeof labels],
        count: counts[stage],
        // Only the stage whose money the API can actually attribute carries a figure. Inventing a
        // value for "new enquiries" would be inventing a number.
        value: stage === 'AngebotSent' && sentValue > 0 ? this.money(sentValue) : null,
        share: counts[stage] / widest,

        // **The percentage names its own denominator**, because without it the figure is
        // unreadable: "50 %" next to "Visit scheduled" was taken for a conversion rate, when it is
        // this stage's *current* count against the previous stage's *current* count.
        //
        // The arithmetic is unchanged — QA confirmed it was already right — but these are snapshot
        // counts of where enquiries are sitting now, not a cohort followed through the funnel. An
        // enquiry that reached `InspectionDone` has left `New` and is no longer in that
        // denominator. Saying which stage the comparison is against is the difference between a
        // number someone can use and one they will misread.
        conversion:
          previous && previous > 0
            ? this.i18n.format(
                this.i18n.t().cockpit.funnel.ofPrevious,
                Math.round((counts[stage] / previous) * 100),
                labels[previousStage as keyof typeof labels],
              )
            : null,
      };
    });
  }

  private funnelSummary(counts: Record<LeadStatusDto, number>): string | null {
    const decided = counts.Won + counts.Lost;
    if (decided === 0) {
      return null;
    }

    const rate = Math.round((counts.Won / decided) * 100);
    return this.i18n.format(this.i18n.t().cockpit.funnel.conversion, rate, counts.Won, decided);
  }

  private moneySegments(r: ReceivablesSummaryDto): readonly Segment[] {
    const t = this.i18n.t().cockpit.money;

    return [
      { id: 'paid', label: t.paid, value: r.paidGross, formatted: this.money(r.paidGross), color: '--rt-money-paid' },
      {
        id: 'open',
        // Open minus overdue, so the three segments sum to the invoiced total rather than
        // double-counting the overdue portion, which is a subset of open.
        label: t.open,
        value: Math.max(0, r.openGross - r.overdueGross),
        formatted: this.money(Math.max(0, r.openGross - r.overdueGross)),
        color: '--rt-money-open',
      },
      { id: 'overdue', label: t.overdue, value: r.overdueGross, formatted: this.money(r.overdueGross), color: '--rt-money-overdue' },
    ];
  }

  private appointment(i: InspectionDetailDto): Appointment {
    const at = new Date(i.scheduledAt);
    return {
      id: i.id,
      time: `${`${at.getHours()}`.padStart(2, '0')}:${`${at.getMinutes()}`.padStart(2, '0')}`,
      customer: i.leadName,
      address: i.leadAddress ?? i.leadPhone,
      done: i.completedAt !== null,
    };
  }

  /**
   * Money, rounded to whole euros.
   *
   * **Cents are dropped here and only here.** A cockpit compares magnitudes, and two decimals on
   * every tile is noise that makes the row harder to scan. Every screen showing an actual document
   * keeps BR-11's exact cents, because there the figure is the contract.
   */
  private money(value: number): string {
    return formatCurrency(value, this.i18n.locale(), '€', 'EUR', '1.0-0');
  }

  private count(value: number): string {
    return formatNumber(value, this.i18n.locale());
  }
}

function addDays(date: Date, days: number): Date {
  const copy = new Date(date);
  copy.setDate(copy.getDate() + days);
  copy.setHours(0, 0, 0, 0);
  return copy;
}

function isoDate(value: Date): string {
  const month = `${value.getMonth() + 1}`.padStart(2, '0');
  const day = `${value.getDate()}`.padStart(2, '0');
  return `${value.getFullYear()}-${month}-${day}`;
}
