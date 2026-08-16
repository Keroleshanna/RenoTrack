import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';

import { ApiError } from '../../core/api/api-error';
import { AngebotHeaderDto, LeadDto, UserSummaryDto } from '../../core/api/contracts';
import { RenoTrackApi } from '../../core/api/renotrack-api';
import { Auth } from '../../core/auth/auth';
import { I18n } from '../../core/i18n/i18n';
import { ContactActions } from '../../shared/ui/contact-actions';
import { Dialog } from '../../shared/ui/dialog';
import { Notifier } from '../../shared/ui/notifier';
import { ErrorState, Skeleton } from '../../shared/ui/state-panels';
import { StatusChip } from '../../shared/ui/status-chip';

/**
 * Wireframe **C1** — one enquiry, and the two actions that move it forward.
 *
 * This screen exists because a pipeline that cannot be acted on is a report. `PermissionMatrix.md`
 * §1 forbids editing a Lead's status directly (BR-7): a Lead moves **only** as a side effect of the
 * action that causes the move, and both of those actions live here.
 *
 * - **Schedule an Inspection** (Admin, §2 `F`, Wireframe C2). BR-13 also assigns that Inspector to
 *   the Lead, which is why no separate "assign" control exists beside it.
 * - **Start an Angebot** (Inspector, §3 `S`). The Lead's own guard is what requires `InspectionDone`
 *   first, and it surfaces as 409 — the button is offered on the state the workflow expects, and the
 *   refusal is reported rather than pre-empted.
 *
 * **The Activity Timeline C1 also draws is deliberately absent.** `GET /api/v1/audit-logs` does not
 * exist — `PermissionMatrix.md` §8 assigns the Audit Log screen to Admin and `PROJECT_ROADMAP.md`
 * puts it in Phase 15. Rendering a timeline would mean inventing a read endpoint, and faking one from
 * the Lead's own fields would be a fabricated history. The Angebot history below is real data.
 */
@Component({
  selector: 'app-lead-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CurrencyPipe,
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    ContactActions,
    Dialog,
    ErrorState,
    Skeleton,
    StatusChip,
  ],
  templateUrl: './lead-detail-page.html',
  styleUrl: './lead-detail-page.scss',
})
export class LeadDetailPage {
  private readonly api = inject(RenoTrackApi);
  private readonly auth = inject(Auth);
  private readonly i18n = inject(I18n);
  private readonly notifier = inject(Notifier);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly t = this.i18n.t;
  protected readonly locale = this.i18n.locale;

  protected readonly id = Number(this.route.snapshot.paramMap.get('id'));

  protected readonly lead = signal<LeadDto | null>(null);
  protected readonly angebote = signal<readonly AngebotHeaderDto[]>([]);
  protected readonly inspectors = signal<readonly UserSummaryDto[]>([]);
  protected readonly loadState = signal<'loading' | 'ready' | 'error'>('loading');
  protected readonly loadError = signal('');
  protected readonly busy = signal(false);

  protected readonly scheduleOpen = signal(false);
  protected readonly editContactOpen = signal(false);
  protected readonly assignOpen = signal(false);

  protected readonly isAdmin = computed(() => this.auth.role() === 'admin');

  /**
   * The assigned colleague's name, falling back to the raw id.
   *
   * The Lead carries only an id, and the staff list is a separate read that can legitimately be
   * empty — an Inspector's own list may not have loaded, or the account may since have been
   * deactivated and so be absent from an `activeOnly` directory. Showing `#7` is honest in that
   * case; inventing a name, or rendering nothing, would not be.
   */
  protected readonly assignedInspectorName = computed(() => {
    const assignedId = this.lead()?.assignedInspectorId;
    if (!assignedId) {
      return this.t().leads.unassigned;
    }
    return this.inspectors().find((user) => user.id === assignedId)?.name ?? `#${assignedId}`;
  });

  /** Admin, on an enquiry nobody has been sent to yet — `Lead.MarkInspectionScheduled` requires `New`. */
  protected readonly canSchedule = computed(
    () => this.isAdmin() && this.lead()?.status === 'New',
  );

  /** Inspector, once the site visit is done — FR-4.1's guard, enforced by the Lead itself. */
  protected readonly canCreateAngebot = computed(
    () => this.auth.role() === 'inspector' && this.lead()?.status === 'InspectionDone',
  );

  protected readonly scheduleForm = new FormGroup({
    scheduledAt: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    inspectorId: new FormControl<number | null>(null, { validators: [Validators.required] }),
  });

  protected readonly contactForm = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    phone: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email],
    }),
    address: new FormControl('', { nonNullable: true }),
  });

  protected readonly assignForm = new FormGroup({
    inspectorId: new FormControl<number | null>(null, { validators: [Validators.required] }),
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loadState.set('loading');

    forkJoin({
      lead: this.api.lead(this.id),
      // Supplementary: an Inspector scoped away from a quote, or an unreadable history, must not
      // take the whole screen into an error state.
      angebote: this.api.leadAngebote(this.id).pipe(catchError(() => of([]))),
    }).subscribe({
      next: ({ lead, angebote }) => {
        this.lead.set(lead);
        this.angebote.set(angebote);
        this.loadState.set('ready');

        // Fetched for both roles, not just Admin: it is what turns "#7" into a colleague's name in
        // the contact panel. `GET /api/v1/users` is readable by Admin and Inspector alike, and
        // reading the staff directory confers no permission to change an assignment — that stays
        // Admin-only and is enforced by the endpoint, not by whether this list was loaded.
        this.api
          .staff('Inspector')
          .pipe(catchError(() => of([])))
          .subscribe((staff) => this.inspectors.set(staff.filter((user) => user.isActive)));
      },
      error: (error: unknown) => {
        this.loadError.set(this.messageFor(error));
        this.loadState.set('error');
      },
    });
  }

  protected openSchedule(): void {
    this.scheduleForm.reset({ scheduledAt: '', inspectorId: null });
    this.scheduleOpen.set(true);
  }

  /**
   * Schedules the visit.
   *
   * `scheduledAt` is sent exactly as the `datetime-local` control produced it — a **local** wall
   * clock time with no zone suffix, which is what the API binds to a `DateTime` and what an
   * appointment actually is. Converting to UTC first would move a 09:00 site visit by an hour twice
   * a year.
   */
  protected schedule(): void {
    if (this.scheduleForm.invalid) {
      this.scheduleForm.markAllAsTouched();
      return;
    }

    const value = this.scheduleForm.getRawValue();
    this.busy.set(true);

    this.api.scheduleInspection(this.id, value.scheduledAt, value.inspectorId!).subscribe({
      next: () => {
        this.busy.set(false);
        this.scheduleOpen.set(false);
        this.notifier.success(this.t().leadDetail.scheduled);
        this.load();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.notifier.error(this.messageFor(error));
      },
    });
  }

  /**
   * Starts the quote and goes straight to it.
   *
   * `inspectionId` is sent as `null`. The field is optional in both the request and the aggregate,
   * and **no endpoint returns a Lead's Inspections** — the schedule read is a date window, not a
   * per-Lead history. Guessing the Inspection by scanning a window would attach the wrong visit as
   * easily as the right one, and inventing a read endpoint is scope this work did not agree.
   */
  protected createAngebot(): void {
    this.busy.set(true);

    this.api.createAngebot(this.id, null).subscribe({
      next: (angebot) => {
        this.busy.set(false);
        this.notifier.success(this.t().leadDetail.angebotCreated);
        void this.router.navigate(['/angebote', angebot.id]);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.notifier.error(this.messageFor(error));
      },
    });
  }

  protected openEditContact(): void {
    const lead = this.lead();
    if (!lead) {
      return;
    }

    this.contactForm.reset({
      name: lead.name,
      phone: lead.phone,
      email: lead.email,
      address: lead.address ?? '',
    });
    this.editContactOpen.set(true);
  }

  /**
   * Saves the corrected contact details.
   *
   * All four fields are always sent, because the endpoint is a `PUT` that replaces them — clearing
   * the address box is a request to clear the address, not to leave it as it was.
   */
  protected saveContact(): void {
    if (this.contactForm.invalid) {
      this.contactForm.markAllAsTouched();
      return;
    }

    const value = this.contactForm.getRawValue();
    this.busy.set(true);

    this.api
      .updateLeadContactDetails(this.id, {
        name: value.name,
        phone: value.phone,
        email: value.email,
        address: value.address.trim() || null,
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.editContactOpen.set(false);
          this.notifier.success(this.t().leadForm.updated);
          // Re-read rather than trusting the response shape (D81).
          this.load();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.notifier.error(this.messageFor(error));
        },
      });
  }

  protected openAssign(): void {
    // Pre-selected with whoever holds it, so "change" starts from the truth rather than blank.
    this.assignForm.reset({ inspectorId: this.lead()?.assignedInspectorId ?? null });
    this.assignOpen.set(true);
  }

  protected assignInspector(): void {
    if (this.assignForm.invalid) {
      this.assignForm.markAllAsTouched();
      return;
    }

    this.busy.set(true);

    this.api.assignLeadInspector(this.id, this.assignForm.getRawValue().inspectorId!).subscribe({
      next: () => {
        this.busy.set(false);
        this.assignOpen.set(false);
        this.notifier.success(this.t().leadDetail.assigned);
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
