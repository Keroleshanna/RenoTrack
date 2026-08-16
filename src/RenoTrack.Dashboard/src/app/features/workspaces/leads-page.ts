import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';

import { ApiError } from '../../core/api/api-error';
import { ContactActions } from '../../shared/ui/contact-actions';
import { RenoTrackApi } from '../../core/api/renotrack-api';
import { Auth } from '../../core/auth/auth';
import {
  InspectionDetailDto,
  LEAD_STATUSES,
  LeadDto,
  LeadStatusDto,
  MANUAL_LEAD_SOURCES,
  ManualLeadSourceDto,
  PagedResult,
} from '../../core/api/contracts';
import { Dialog } from '../../shared/ui/dialog';
import { Notifier } from '../../shared/ui/notifier';
import { StatusChip } from '../../shared/ui/status-chip';
import { FilterOption, WorkspaceChrome } from './workspace-chrome';
import { WorkspacePage } from './workspace-page';

/**
 * The Leads workspace — the pipeline as a working list.
 *
 * **This is where inventory belongs**, which is why the Cockpit shows a funnel instead: a list
 * answers "which enquiry is where and what do I do with it", a funnel answers "where does the
 * business lose momentum". Two different questions, two different screens.
 *
 * **The list is read-only with respect to status.** `PermissionMatrix.md` §1 gives "Change Lead
 * status directly" as `—` for both roles, and no endpoint exists: status moves only through the
 * action that causes it (BR-7). There is no drag-to-move and no status control here.
 *
 * Scoping is server-side — an Inspector's own id is forced onto the query by `LeadsController`, so
 * this screen neither sends nor could widen it.
 */
@Component({
  selector: 'app-leads-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    ContactActions,
    Dialog,
    StatusChip,
    WorkspaceChrome,
  ],
  template: `
    <app-workspace-chrome
      [title]="t().leads.title"
      [subtitle]="subtitle()"
      [headlineLabel]="t().leads.count"
      [headlineValue]="total().toString()"
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
      @if (isAdmin()) {
        <button chromeActions type="button" class="rt-button rt-button--primary" (click)="openCreate()">
          {{ t().leads.newLead }}
        </button>
      }

      <div class="rt-table-wrap">
        <table class="rt-table">
          <thead>
            <tr>
              <th>{{ t().leads.columns.customer }}</th>
              <th>{{ t().leads.columns.contact }}</th>
              <th>{{ t().leads.columns.source }}</th>
              <th>{{ t().leads.columns.status }}</th>
              <th>{{ t().leads.columns.visit }}</th>
              <th class="rt-numeric">{{ t().leads.columns.created }}</th>
            </tr>
          </thead>
          <tbody>
            @for (lead of rows(); track lead.id) {
              <tr>
                <td>
                  <a class="row-link" [routerLink]="['/leads', lead.id]">{{ lead.name }}</a>
                  @if (lead.address) {
                    <span class="rt-table__secondary">{{ lead.address }}</span>
                  }
                </td>
                <td>
                  <span class="rt-table__primary">{{ lead.phone }}</span>
                  <span class="rt-table__secondary">{{ lead.email }}</span>
                  <app-contact-actions
                    [phone]="lead.phone"
                    [email]="lead.email"
                    [address]="lead.address"
                  />
                </td>
                <td>{{ t().leadSource[lead.source] }}</td>
                <td><app-status-chip kind="lead" [value]="lead.status" /></td>
                <td>
                  <!--
                    Real appointment data from the schedule read, not inferred from the Lead's
                    status: "InspectionScheduled" says a visit exists, never when it is. An
                    Inspector planning their day should not have to go back to the Cockpit for it.
                  -->
                  @if (visitFor(lead.id); as visit) {
                    <span class="rt-table__primary">
                      {{ visit.scheduledAt | date: t().formats.date : undefined : locale() }}
                    </span>
                    <span class="rt-table__secondary">
                      {{ visit.scheduledAt | date: t().formats.time : undefined : locale() }}
                      @if (visit.completedAt) {
                        · {{ t().leads.visitDone }}
                      }
                    </span>
                  } @else {
                    <span class="rt-muted">{{ t().leads.noVisit }}</span>
                  }
                </td>
                <td class="rt-numeric">
                  {{ lead.createdAt | date: t().formats.date : undefined : locale() }}
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>

      <p class="rt-caption hint">{{ t().leads.readOnlyHint }}</p>
    </app-workspace-chrome>

    <!--
      FR-2.1. The source is offered as Phone/Email only — the contract has no Website member, so
      a manually-entered Lead structurally cannot claim to have arrived through the contact form.
    -->
    <app-dialog
      [open]="createOpen()"
      [heading]="t().leadForm.createTitle"
      [description]="t().leadForm.createHint"
      (closed)="createOpen.set(false)"
    >
      <form [formGroup]="form">
        <div class="rt-field">
          <label class="rt-field__label" for="lead-name">{{ t().leadForm.name }}</label>
          <input id="lead-name" type="text" class="rt-input" formControlName="name" />
          @if (form.controls.name.touched && form.controls.name.invalid) {
            <span class="rt-field__error">{{ t().angebotDetail.required }}</span>
          }
        </div>

        <div class="fields">
          <div class="rt-field">
            <label class="rt-field__label" for="lead-phone">{{ t().leadForm.phone }}</label>
            <input id="lead-phone" type="tel" class="rt-input" formControlName="phone" />
            @if (form.controls.phone.touched && form.controls.phone.invalid) {
              <span class="rt-field__error">{{ t().angebotDetail.required }}</span>
            }
          </div>

          <div class="rt-field">
            <label class="rt-field__label" for="lead-email">{{ t().leadForm.email }}</label>
            <input id="lead-email" type="email" class="rt-input" formControlName="email" />
            @if (form.controls.email.touched && form.controls.email.invalid) {
              <span class="rt-field__error">{{ t().angebotDetail.required }}</span>
            }
          </div>
        </div>

        <div class="rt-field">
          <label class="rt-field__label" for="lead-source">{{ t().leadForm.source }}</label>
          <select id="lead-source" class="rt-select" formControlName="source">
            @for (source of manualSources; track source) {
              <option [value]="source">{{ t().leadSource[source] }}</option>
            }
          </select>
        </div>

        <div class="rt-field">
          <label class="rt-field__label" for="lead-address">
            {{ t().leadForm.address }} ({{ t().leadForm.optional }})
          </label>
          <input id="lead-address" type="text" class="rt-input" formControlName="address" />
        </div>

        <div class="rt-field">
          <label class="rt-field__label" for="lead-notes">
            {{ t().leadForm.notes }} ({{ t().leadForm.optional }})
          </label>
          <textarea id="lead-notes" class="rt-textarea" rows="3" formControlName="notes"></textarea>
        </div>
      </form>

      <ng-container dialogActions>
        <button type="button" class="rt-button" (click)="createOpen.set(false)">
          {{ t().actions.cancel }}
        </button>
        <button
          type="button"
          class="rt-button rt-button--primary"
          [disabled]="busy()"
          (click)="create()"
        >
          {{ t().actions.create }}
        </button>
      </ng-container>
    </app-dialog>
  `,
  styles: `
    .hint {
      margin-top: var(--rt-space-4);
    }
    .row-link {
      display: block;
      color: var(--rt-brand);
      font-weight: 600;
      text-decoration: none;
    }
    .row-link:hover {
      text-decoration: underline;
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
export class LeadsPage extends WorkspacePage<LeadDto> {
  private readonly api = inject(RenoTrackApi);
  private readonly auth = inject(Auth);
  private readonly notifier = inject(Notifier);
  private readonly navigator = inject(Router);

  /** §1: "Create Lead manually (phone/email) — Admin F, Inspector —". */
  protected readonly isAdmin = computed(() => this.auth.role() === 'admin');

  protected readonly manualSources = MANUAL_LEAD_SOURCES;

  protected readonly busy = signal(false);
  protected readonly createOpen = signal(false);

  /**
   * Scheduled visits, keyed by Lead, for the appointment column.
   *
   * A separate read because a Lead carries no appointment: the schedule endpoint is the only place
   * that fact lives, and its window is generous here (a quarter back, a year forward) so a visit
   * shows up whether it is next week or already done. Scoping is still the server's — an Inspector
   * sees only their own visits, exactly as they see only their own Leads.
   *
   * If it fails the column simply reads "not scheduled" rather than taking the list down: the
   * pipeline is the point of this screen, and the appointment is a useful extra on top of it.
   */
  private readonly visits = signal<ReadonlyMap<number, InspectionDetailDto>>(new Map());

  protected visitFor(leadId: number): InspectionDetailDto | undefined {
    return this.visits().get(leadId);
  }

  protected readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    phone: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email],
    }),
    source: new FormControl<ManualLeadSourceDto>('Phone', { nonNullable: true }),
    address: new FormControl('', { nonNullable: true }),
    notes: new FormControl('', { nonNullable: true }),
  });

  protected readonly subtitle = computed(() =>
    this.auth.role() === 'inspector' ? this.t().leads.subtitleInspector : this.t().leads.subtitleAdmin,
  );

  protected readonly filters = computed<readonly FilterOption[]>(() => [
    { value: null, label: this.t().filters.allStatuses },
    ...LEAD_STATUSES.map((status) => ({ value: status, label: this.t().leadStatus[status] })),
  ]);

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
    this.loadVisits();
  }

  private loadVisits(): void {
    const from = new Date();
    from.setMonth(from.getMonth() - 3);

    const to = new Date();
    to.setFullYear(to.getFullYear() + 1);

    this.api
      .inspections(from, to)
      .pipe(catchError(() => of([] as InspectionDetailDto[])))
      .subscribe((visits) => {
        const byLead = new Map<number, InspectionDetailDto>();

        // Latest visit wins when a Lead has more than one — a reassigned or repeated visit is the
        // one the user is planning around, not the historical first attempt.
        for (const visit of [...visits].sort((a, b) => a.scheduledAt.localeCompare(b.scheduledAt))) {
          byLead.set(visit.leadId, visit);
        }

        this.visits.set(byLead);
      });
  }

  protected fetch(status: string | null, page: number): Observable<PagedResult<LeadDto>> {
    return this.api.leads({
      status: (status as LeadStatusDto | null) ?? undefined,
      page,
      pageSize: this.pageSize,
    });
  }

  protected openCreate(): void {
    this.form.reset({
      name: '',
      phone: '',
      email: '',
      source: 'Phone',
      address: '',
      notes: '',
    });
    this.createOpen.set(true);
  }

  /**
   * Creates the Lead and goes straight to it.
   *
   * Navigating rather than reloading the list: an Admin who has just typed an enquiry in almost
   * always wants to schedule the visit next, and that action lives on the detail screen. The list
   * would otherwise show the new row somewhere under the current filter — or not at all, if the
   * filter excludes `New`.
   */
  protected create(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    this.busy.set(true);

    this.api
      .createLeadManually({
        name: value.name,
        phone: value.phone,
        email: value.email,
        source: value.source,
        // Empty boxes are absent values, not empty strings — both columns are nullable.
        address: value.address.trim() || null,
        notes: value.notes.trim() || null,
      })
      .subscribe({
        next: (lead) => {
          this.busy.set(false);
          this.createOpen.set(false);
          this.notifier.success(this.t().leadForm.created);
          void this.navigator.navigate(['/leads', lead.id]);
        },
        error: (error: unknown) => {
          this.busy.set(false);
          const kind = error instanceof ApiError ? error.kind : 'server';
          this.notifier.error(this.t().errors[kind]);
        },
      });
  }
}
