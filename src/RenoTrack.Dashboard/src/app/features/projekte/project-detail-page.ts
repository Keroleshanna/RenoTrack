import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import {
  PAYMENT_METHODS,
  PaymentMethodDto,
  ProjectDetailDto,
  ProjectInvoiceDto,
} from '../../core/api/contracts';
import { RenoTrackApi } from '../../core/api/renotrack-api';
import { Auth } from '../../core/auth/auth';
import { I18n } from '../../core/i18n/i18n';
import { Dialog } from '../../shared/ui/dialog';
import { Notifier } from '../../shared/ui/notifier';
import { ErrorState, Skeleton } from '../../shared/ui/state-panels';
import { StatusChip } from '../../shared/ui/status-chip';
import { invoiceCapabilitiesFor } from './invoice-capabilities';

/**
 * Wireframe **E1** — the Project, its money, and every Invoice action (E2, E3).
 *
 * ## Why the invoice workflow lives here and not on the Rechnungen list
 *
 * `POST /api/v1/projects/{projectId}/invoices` is Project-scoped, and BR-3's *remaining balance* —
 * the one figure that tells an Admin what to invoice next — exists only in a Project's context.
 * Creating an invoice from a flat list would mean asking which Project first, with no balance in
 * sight. The list keeps the id-scoped actions (send, mark paid, void), which need no Project.
 *
 * ## Both roles read it; only Verwaltung acts
 *
 * `PermissionMatrix.md` §5 grants an Inspector `R` on the Project detail **and** its Invoice list,
 * and `—` on every Invoice action. That is exactly what this screen renders: the same figures for
 * both, with the action column present only for an Admin. The endpoints refuse an Inspector
 * regardless (CLAUDE.md §23).
 *
 * ## BR-3 warns; it does not block
 *
 * A negative `remaining` is over-invoicing, and it is shown as a warning rather than prevented: the
 * server accepts the invoice, and clamping or refusing it here would delete the only signal BR-3
 * exists to produce.
 */
@Component({
  selector: 'app-project-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CurrencyPipe,
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    Dialog,
    ErrorState,
    Skeleton,
    StatusChip,
  ],
  templateUrl: './project-detail-page.html',
  styleUrl: './project-detail-page.scss',
})
export class ProjectDetailPage {
  private readonly api = inject(RenoTrackApi);
  private readonly auth = inject(Auth);
  private readonly i18n = inject(I18n);
  private readonly notifier = inject(Notifier);
  private readonly route = inject(ActivatedRoute);

  protected readonly t = this.i18n.t;
  protected readonly locale = this.i18n.locale;

  protected readonly id = Number(this.route.snapshot.paramMap.get('id'));

  protected readonly project = signal<ProjectDetailDto | null>(null);
  protected readonly loadState = signal<'loading' | 'ready' | 'error'>('loading');
  protected readonly loadError = signal('');
  protected readonly busy = signal(false);

  protected readonly isAdmin = computed(() => this.auth.role() === 'admin');

  /** BR-3's warning: invoiced beyond the agreed total. Never hidden, never clamped. */
  protected readonly overInvoiced = computed(() => (this.project()?.remaining ?? 0) < 0);

  protected readonly canComplete = computed(
    () => this.isAdmin() && this.project()?.status === 'Active',
  );

  /**
   * §5 is Admin-only, and StateMachine §4.3 allows each move from exactly one state.
   *
   * These mirror the aggregate rather than pre-empting it: the server refuses the same combinations
   * with a 409, and these exist only so an Admin is not offered work that would be refused.
   */
  protected readonly canHold = computed(
    () => this.isAdmin() && this.project()?.status === 'Active',
  );

  protected readonly canResume = computed(
    () => this.isAdmin() && this.project()?.status === 'OnHold',
  );

  // ---- Dialog state ------------------------------------------------------------------------------

  protected readonly invoiceDialogOpen = signal(false);
  protected readonly payingInvoice = signal<ProjectInvoiceDto | null>(null);
  protected readonly voidingInvoice = signal<ProjectInvoiceDto | null>(null);
  protected readonly sendingInvoice = signal<ProjectInvoiceDto | null>(null);
  protected readonly overrideDialogOpen = signal(false);
  protected readonly holdDialogOpen = signal(false);
  protected readonly resumeDialogOpen = signal(false);

  protected readonly invoiceForm = new FormGroup({
    grossAmount: new FormControl(0, {
      nonNullable: true,
      validators: [Validators.required, Validators.min(0)],
    }),
    dueDate: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  protected readonly paymentForm = new FormGroup({
    paidAt: new FormControl(today(), { nonNullable: true, validators: [Validators.required] }),
    method: new FormControl<PaymentMethodDto>('BankTransfer', { nonNullable: true }),
  });

  protected readonly voidForm = new FormGroup({
    reason: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  protected readonly overrideForm = new FormGroup({
    reason: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  protected readonly paymentMethods = PAYMENT_METHODS;

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loadState.set('loading');

    this.api.project(this.id).subscribe({
      next: (project) => {
        this.project.set(project);
        this.loadState.set('ready');
      },
      error: (error: unknown) => {
        this.loadError.set(this.messageFor(error));
        this.loadState.set('error');
      },
    });
  }

  private reload(): void {
    this.api.project(this.id).subscribe({
      next: (project) => this.project.set(project),
      error: (error: unknown) => this.notifier.error(this.messageFor(error)),
    });
  }

  // ---- Which action a given Invoice accepts (StateMachine.md §3.3) -------------------------------

  protected canSend(invoice: ProjectInvoiceDto): boolean {
    return invoiceCapabilitiesFor(this.auth.role(), invoice.status).canSend;
  }

  protected canMarkPaid(invoice: ProjectInvoiceDto): boolean {
    return invoiceCapabilitiesFor(this.auth.role(), invoice.status).canMarkPaid;
  }

  protected canVoid(invoice: ProjectInvoiceDto): boolean {
    return invoiceCapabilitiesFor(this.auth.role(), invoice.status).canVoid;
  }

  // ---- Invoice workflow --------------------------------------------------------------------------

  protected openInvoiceDialog(): void {
    const remaining = this.project()?.remaining ?? 0;

    // Pre-filled with what is left to invoice — the number the Admin most often wants, and one they
    // may freely overwrite. Never clamped: BR-3 permits exceeding it.
    this.invoiceForm.reset({
      grossAmount: remaining > 0 ? round2(remaining) : 0,
      dueDate: inDays(14),
    });
    this.invoiceDialogOpen.set(true);
  }

  protected createInvoice(): void {
    if (this.invoiceForm.invalid) {
      this.invoiceForm.markAllAsTouched();
      return;
    }

    const value = this.invoiceForm.getRawValue();

    this.perform(
      this.api.createInvoice(this.id, value.grossAmount, value.dueDate),
      this.t().projectDetail.invoiceCreated,
      () => this.invoiceDialogOpen.set(false),
    );
  }

  protected sendInvoice(): void {
    const invoice = this.sendingInvoice();
    if (!invoice) {
      return;
    }

    this.perform(this.api.sendInvoice(invoice.id), this.t().projectDetail.invoiceSent, () =>
      this.sendingInvoice.set(null),
    );
  }

  protected openPayment(invoice: ProjectInvoiceDto): void {
    this.paymentForm.reset({ paidAt: today(), method: 'BankTransfer' });
    this.payingInvoice.set(invoice);
  }

  protected markPaid(): void {
    const invoice = this.payingInvoice();
    if (!invoice || this.paymentForm.invalid) {
      this.paymentForm.markAllAsTouched();
      return;
    }

    const value = this.paymentForm.getRawValue();

    this.perform(
      this.api.markInvoicePaid(invoice.id, value.paidAt, value.method),
      this.t().projectDetail.invoicePaid,
      () => this.payingInvoice.set(null),
    );
  }

  protected openVoid(invoice: ProjectInvoiceDto): void {
    this.voidForm.reset({ reason: '' });
    this.voidingInvoice.set(invoice);
  }

  protected voidInvoice(): void {
    const invoice = this.voidingInvoice();
    if (!invoice) {
      return;
    }
    if (this.voidForm.invalid) {
      this.voidForm.markAllAsTouched();
      return;
    }

    this.perform(
      this.api.voidInvoice(invoice.id, this.voidForm.getRawValue().reason.trim()),
      this.t().projectDetail.invoiceVoided,
      () => this.voidingInvoice.set(null),
    );
  }

  // ---- Completion (FR-8.6) -----------------------------------------------------------------------

  /**
   * Tries the ordinary completion first, and only offers the override when the server actually
   * refuses.
   *
   * **The override is never offered speculatively.** An override with nothing to override is a 400
   * by design — it would write a false justification into the audit trail — so the only honest way
   * to reach it is through a real refusal.
   */
  protected complete(): void {
    this.busy.set(true);

    this.api.completeProject(this.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.notifier.success(this.t().projectDetail.completed);
        this.reload();
      },
      error: (error: unknown) => {
        this.busy.set(false);

        if (error instanceof ApiError && error.kind === 'conflict') {
          this.overrideForm.reset({ reason: '' });
          this.overrideDialogOpen.set(true);
          return;
        }

        this.notifier.error(this.messageFor(error));
      },
    });
  }

  protected completeWithOverride(): void {
    if (this.overrideForm.invalid) {
      this.overrideForm.markAllAsTouched();
      return;
    }

    this.perform(
      this.api.completeProject(this.id, {
        reason: this.overrideForm.getRawValue().reason.trim(),
      }),
      this.t().projectDetail.completed,
      () => this.overrideDialogOpen.set(false),
    );
  }

  // ---- Plumbing ----------------------------------------------------------------------------------

  protected askHold(): void {
    this.holdDialogOpen.set(true);
  }

  protected askResume(): void {
    this.resumeDialogOpen.set(true);
  }

  protected putOnHold(): void {
    this.perform(this.api.putProjectOnHold(this.id), this.t().projectDetail.heldMessage, () =>
      this.holdDialogOpen.set(false),
    );
  }

  protected resume(): void {
    this.perform(this.api.resumeProject(this.id), this.t().projectDetail.resumedMessage, () =>
      this.resumeDialogOpen.set(false),
    );
  }

  private perform(
    request: Observable<unknown>,
    successMessage: string,
    onSuccess?: () => void,
  ): void {
    this.busy.set(true);

    request.subscribe({
      next: () => {
        this.busy.set(false);
        onSuccess?.();
        this.notifier.success(successMessage);
        this.reload();
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

/** `yyyy-MM-dd` from the local calendar — never `toISOString()`, which shifts across midnight. */
function isoDate(value: Date): string {
  const month = `${value.getMonth() + 1}`.padStart(2, '0');
  const day = `${value.getDate()}`.padStart(2, '0');
  return `${value.getFullYear()}-${month}-${day}`;
}

function today(): string {
  return isoDate(new Date());
}

function inDays(days: number): string {
  const date = new Date();
  date.setDate(date.getDate() + days);
  return isoDate(date);
}

/** Two decimals, because money is `decimal(18,2)` everywhere it is stored (BR-11). */
function round2(value: number): number {
  return Math.round(value * 100) / 100;
}
