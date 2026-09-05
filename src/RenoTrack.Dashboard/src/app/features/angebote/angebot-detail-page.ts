import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable, forkJoin, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';

import { ApiError } from '../../core/api/api-error';
import {
  AngebotDetailDto,
  AngebotReviewCommentDto,
  CatalogItemDto,
  ItemDto,
  LeadDto,
  PAGE_SIZE_MAX,
  STANDARD_UNITS,
  SectionDetailDto,
  VAT_PERCENT,
  VAT_RATES,
  VatRateDto,
} from '../../core/api/contracts';
import { RenoTrackApi } from '../../core/api/renotrack-api';
import { Auth } from '../../core/auth/auth';
import { I18n } from '../../core/i18n/i18n';
import { Dialog } from '../../shared/ui/dialog';
import { Notifier } from '../../shared/ui/notifier';
import { EmptyState, ErrorState, Skeleton } from '../../shared/ui/state-panels';
import { StatusChip } from '../../shared/ui/status-chip';
import { AngebotCapabilities, capabilitiesFor } from './angebot-capabilities';
import { CatalogPicker } from './catalog-picker';

/** The confirmations that guard an action with a consequence outside this screen. */
type Confirmable = 'submit' | 'approve' | 'send' | 'resend' | 'convert' | null;

/**
 * The Angebot document — Wireframes **D1** (builder) and **D3** (review), as one screen.
 *
 * ## Why one screen and not two
 *
 * D3 describes itself as "the same read view as D1, but read-only for Admin". Building two
 * components would mean maintaining two renderings of the same commercial document and hoping they
 * agree about what a subtotal is. The document is one thing; **what you may do to it** differs, and
 * that difference is `angebot-capabilities.ts` — one place, unit-tested, derived from
 * `PermissionMatrix.md` and `StateMachine.md` rather than scattered through a template.
 *
 * ## Totals are read, never computed here
 *
 * `NetTotal`/`GrossTotal` are stored fields the aggregate recalculates on every mutation, and the
 * VAT breakdown is computed server-side from the live item collection (CLAUDE.md §2). Summing rows
 * in the browser would produce a second answer to a question that has an authoritative one — and
 * BR-11's rounding is the Domain's, not JavaScript's. So every mutation **re-reads the document**,
 * which is also what Sequence Diagram §4 prescribes ("recalculated on every change").
 *
 * ## Nothing here decides whether an action is allowed
 *
 * The aggregate's guards do. `Submit for review` with no items, editing a quote that has moved to
 * `InReview`, approving something already approved — each is refused by the Domain and surfaces as
 * 409. The screen hides what a role cannot reach, and reports what the server refuses; it never
 * re-implements a rule to pre-empt it (CLAUDE.md §6, §23).
 */
@Component({
  selector: 'app-angebot-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CurrencyPipe,
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    CatalogPicker,
    Dialog,
    EmptyState,
    ErrorState,
    Skeleton,
    StatusChip,
  ],
  templateUrl: './angebot-detail-page.html',
  styleUrl: './angebot-detail-page.scss',
})
export class AngebotDetailPage {
  private readonly api = inject(RenoTrackApi);
  private readonly auth = inject(Auth);
  private readonly i18n = inject(I18n);
  private readonly notifier = inject(Notifier);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly t = this.i18n.t;
  protected readonly locale = this.i18n.locale;

  protected readonly id = Number(this.route.snapshot.paramMap.get('id'));

  protected readonly detail = signal<AngebotDetailDto | null>(null);
  protected readonly comments = signal<readonly AngebotReviewCommentDto[]>([]);
  protected readonly leadName = signal<string | null>(null);
  protected readonly loadState = signal<'loading' | 'ready' | 'error'>('loading');
  protected readonly loadError = signal('');

  /** One action at a time. A double-submitted Angebot is a second email to a real customer. */
  protected readonly busy = signal(false);

  protected readonly capabilities = computed<AngebotCapabilities>(() => {
    const angebot = this.detail();
    return angebot
      ? capabilitiesFor(this.auth.role(), angebot.status)
      : capabilitiesFor(null, 'Draft');
  });

  /**
   * The most recent review comment, for the changes-requested banner.
   *
   * Newest last in the history (oldest-first, per the endpoint), so the last entry is the one the
   * Inspector is being asked to act on.
   */
  protected readonly latestComment = computed(() => this.comments().at(-1) ?? null);

  protected readonly isEmptyDocument = computed(
    () => (this.detail()?.sections.length ?? 0) === 0,
  );

  // ---- Dialog state ------------------------------------------------------------------------------

  protected readonly sectionDialogOpen = signal(false);
  protected readonly itemDialogSection = signal<SectionDetailDto | null>(null);

  /** The line being corrected, or `null` while adding — the one thing that differs between them. */
  protected readonly editingItem = signal<ItemDto | null>(null);
  protected readonly changesDialogOpen = signal(false);
  protected readonly pickerOpen = signal(false);
  protected readonly confirming = signal<Confirmable>(null);
  protected readonly duplicateOpen = signal(false);

  /**
   * Candidate Leads for FR-4.11, fetched on demand.
   *
   * The list is whatever `GET /api/v1/leads` returns for this caller, which for an Inspector is
   * already only their own — the scope is forced server-side, so this cannot widen it. It is
   * deliberately *not* filtered to Leads without a live quote: that is StateMachine §2.4's rule,
   * the server enforces it, and reproducing it here would need a quote read per Lead and would
   * still be a guess.
   */
  protected readonly duplicateTargets = signal<readonly LeadDto[]>([]);

  protected readonly duplicateForm = new FormGroup({
    targetLeadId: new FormControl<number | null>(null, { validators: [Validators.required] }),
  });

  protected readonly sectionForm = new FormGroup({
    title: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  /**
   * The line-item form.
   *
   * `catalogItemId` is a hidden control rather than component state, because it is what decides
   * which of FR-4.9's two modes the request uses — and the server ignores description/unit entirely
   * when it is set. Keeping it inside the form means the mode travels with the values it governs.
   */
  protected readonly itemForm = new FormGroup({
    catalogItemId: new FormControl<number | null>(null),
    description: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    specification: new FormControl('', { nonNullable: true }),
    unitCode: new FormControl<string>('m2', {
      nonNullable: true,
      validators: [Validators.required],
    }),
    quantity: new FormControl(1, {
      nonNullable: true,
      validators: [Validators.required, Validators.min(0.0001)],
    }),
    unitPrice: new FormControl(0, {
      nonNullable: true,
      validators: [Validators.required, Validators.min(0)],
    }),
    vatRate: new FormControl<VatRateDto>('Standard', { nonNullable: true }),
  });

  protected readonly changesForm = new FormGroup({
    comment: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  protected readonly units = STANDARD_UNITS;
  protected readonly vatRates = VAT_RATES;

  /** True while the unit control holds something outside the five standard codes. */
  protected readonly customUnit = signal(false);

  constructor() {
    this.load();
  }

  // ---- Reading -----------------------------------------------------------------------------------

  protected load(): void {
    this.loadState.set('loading');

    forkJoin({
      angebot: this.api.angebot(this.id),
      comments: this.api.angebotReviewComments(this.id).pipe(catchError(() => of([]))),
    }).subscribe({
      next: ({ angebot, comments }) => {
        this.detail.set(angebot);
        this.comments.set(comments);
        this.loadState.set('ready');
        this.loadLeadName(angebot.leadId);
      },
      error: (error: unknown) => {
        this.loadError.set(this.messageFor(error));
        this.loadState.set('error');
      },
    });
  }

  /**
   * The customer's name, from the Lead.
   *
   * A separate request because `AngebotDetailDto` carries `leadId` and no name — and the fix for
   * that belongs in the backend DTO if it is ever wanted, not in a client that invents a field.
   * Failure is silent: a missing name degrades the heading, it does not break the document.
   */
  private loadLeadName(leadId: number): void {
    this.api
      .lead(leadId)
      .pipe(
        map((lead) => lead.name),
        catchError(() => of(null)),
      )
      .subscribe((name) => this.leadName.set(name));
  }

  private reload(): void {
    forkJoin({
      angebot: this.api.angebot(this.id),
      comments: this.api.angebotReviewComments(this.id).pipe(catchError(() => of([]))),
    }).subscribe({
      next: ({ angebot, comments }) => {
        this.detail.set(angebot);
        this.comments.set(comments);
      },
      error: (error: unknown) => this.notifier.error(this.messageFor(error)),
    });
  }

  // ---- Building (Inspector, Draft/ChangesRequested) ----------------------------------------------

  protected openSectionDialog(): void {
    this.sectionForm.reset({ title: '' });
    this.sectionDialogOpen.set(true);
  }

  protected addSection(): void {
    if (this.sectionForm.invalid) {
      this.sectionForm.markAllAsTouched();
      return;
    }

    // Appended last. `SortOrder` is the document's own layout, and a new section is written at the
    // bottom of the page — there is no reorder endpoint and none is invented here.
    const sortOrder = (this.detail()?.sections.length ?? 0) + 1;

    this.perform(
      this.api.addSection(this.id, this.sectionForm.getRawValue().title.trim(), sortOrder),
      this.t().angebotDetail.sectionAdded,
      () => this.sectionDialogOpen.set(false),
    );
  }

  protected removeSection(section: SectionDetailDto): void {
    this.perform(
      this.api.removeSection(this.id, section.id),
      this.t().angebotDetail.sectionRemoved,
    );
  }

  protected openItemDialog(section: SectionDetailDto): void {
    this.editingItem.set(null);
    this.itemForm.reset({
      catalogItemId: null,
      description: '',
      specification: '',
      unitCode: 'm2',
      quantity: 1,
      unitPrice: 0,
      vatRate: 'Standard',
    });
    this.customUnit.set(false);
    this.itemDialogSection.set(section);
  }

  /**
   * Fills the form from a Catalog entry (FR-4.9's Catalog mode).
   *
   * Description, specification and unit are shown for the user's benefit only — with
   * `catalogItemId` set the server reads all three from the Catalog entry itself and ignores the
   * body's copies, which is what stops a stale form value entering the document. The suggested price
   * is genuinely a suggestion: it is sent, and the Inspector may change it.
   */
  protected useCatalogItem(item: CatalogItemDto): void {
    this.itemForm.patchValue({
      catalogItemId: item.id,
      description: item.title,
      specification: item.defaultSpecification ?? '',
      unitCode: item.defaultUnit,
      unitPrice: item.suggestedUnitPrice,
    });
    this.customUnit.set(!(STANDARD_UNITS as readonly string[]).includes(item.defaultUnit));
    this.pickerOpen.set(false);
  }

  protected clearCatalogLink(): void {
    this.itemForm.patchValue({ catalogItemId: null });
  }

  protected toggleCustomUnit(custom: boolean): void {
    this.customUnit.set(custom);
    this.itemForm.patchValue({ unitCode: custom ? '' : 'm2' });
  }

  /**
   * Opens the same dialog to correct an existing line.
   *
   * Added after QA found a typo could only be fixed by deleting and re-entering the line — which
   * also discards any Catalog entry contributed from it (FR-4.10), a real cost for a spelling
   * mistake. Catalog-sourced lines are editable too: BR-8 makes the copy independent at creation.
   */
  protected openEditItem(section: SectionDetailDto, item: ItemDto): void {
    this.itemForm.reset({
      catalogItemId: item.catalogItemId,
      description: item.description,
      specification: item.specification ?? '',
      unitCode: item.unit,
      quantity: item.quantity,
      unitPrice: item.unitPrice,
      vatRate: item.vatRate,
    });

    // A line stored with a custom unit opens in the text box, so correcting the price does not
    // silently rewrite the unit to a standard code the Inspector never chose.
    this.customUnit.set(!STANDARD_UNITS.includes(item.unit as never));
    this.editingItem.set(item);
    this.itemDialogSection.set(section);
  }

  protected closeItemDialog(): void {
    this.itemDialogSection.set(null);
    this.editingItem.set(null);
  }

  /** One entry point for both modes — which one runs is decided by {@link editingItem}. */
  protected saveItem(): void {
    const section = this.itemDialogSection();
    if (!section) {
      return;
    }
    if (this.itemForm.invalid) {
      this.itemForm.markAllAsTouched();
      return;
    }

    const value = this.itemForm.getRawValue();
    const editing = this.editingItem();

    if (editing) {
      this.perform(
        // Every value is sent: the endpoint is a PUT that replaces them all. `catalogItemId` is
        // absent from the request by design — editing has one mode, unlike adding.
        this.api.updateItem(this.id, editing.id, {
          description: value.description.trim(),
          specification: value.specification.trim() || null,
          unitCode: value.unitCode.trim(),
          quantity: value.quantity,
          unitPrice: value.unitPrice,
          vatRate: value.vatRate,
        }),
        this.t().angebotDetail.itemUpdated,
        () => this.closeItemDialog(),
      );

      return;
    }

    this.perform(
      this.api.addItem(this.id, {
        sectionId: section.id,
        catalogItemId: value.catalogItemId,
        description: value.description.trim() || null,
        specification: value.specification.trim() || null,
        unitCode: value.unitCode.trim() || null,
        quantity: value.quantity,
        unitPrice: value.unitPrice,
        vatRate: value.vatRate,
      }),
      this.t().angebotDetail.itemAdded,
      () => this.closeItemDialog(),
    );
  }

  protected removeItem(item: ItemDto): void {
    this.perform(this.api.removeItem(this.id, item.id), this.t().angebotDetail.itemRemoved);
  }

  /** FR-4.10 — offered on custom lines only, since a Catalog line is already in the Catalog. */
  protected saveToCatalog(item: ItemDto): void {
    this.perform(
      this.api.saveItemAsCatalogItem(item.id),
      this.t().angebotDetail.savedToCatalog,
    );
  }

  // ---- Workflow ----------------------------------------------------------------------------------

  protected confirm(action: Exclude<Confirmable, null>): void {
    this.confirming.set(action);
  }

  protected confirmationHeading(): string {
    const t = this.t().angebotDetail.confirm;
    switch (this.confirming()) {
      case 'submit':
        return t.submitTitle;
      case 'approve':
        return t.approveTitle;
      case 'send':
        return t.sendTitle;
      case 'resend':
        return t.resendTitle;
      case 'convert':
        return t.convertTitle;
      default:
        return '';
    }
  }

  protected confirmationBody(): string {
    const t = this.t().angebotDetail.confirm;
    switch (this.confirming()) {
      case 'submit':
        return t.submitBody;
      case 'approve':
        return t.approveBody;
      case 'send':
        return t.sendBody;
      case 'resend':
        return t.resendBody;
      case 'convert':
        return t.convertBody;
      default:
        return '';
    }
  }

  protected runConfirmed(): void {
    const action = this.confirming();
    const t = this.t().angebotDetail;

    switch (action) {
      case 'submit':
        this.perform(this.api.submitAngebotForReview(this.id), t.submitted, undefined, () =>
          this.confirming.set(null),
        );
        return;
      case 'approve':
        this.perform(this.api.approveAngebot(this.id), t.approved, undefined, () =>
          this.confirming.set(null),
        );
        return;
      case 'send':
        this.perform(this.api.sendAngebot(this.id), t.sent, undefined, () =>
          this.confirming.set(null),
        );
        return;
      case 'resend':
        // Re-issuing supersedes the link the customer already has (D99), so the reload afterwards
        // matters as much as the call: the quote itself is unchanged, but re-reading is what keeps
        // this screen from asserting a state it merely assumed (D81).
        this.perform(this.api.resendAngebot(this.id), t.resent, undefined, () =>
          this.confirming.set(null),
        );
        return;
      case 'convert':
        // The one action whose result is a *different* aggregate, so it navigates rather than
        // reloading: staying on the quote would leave the user on the document they just finished
        // with, one click from the Project that now matters.
        this.busy.set(true);
        this.api.convertAngebotToProject(this.id).subscribe({
          next: (project) => {
            this.busy.set(false);
            this.confirming.set(null);
            this.notifier.success(t.converted);
            void this.router.navigate(['/projekte', project.id]);
          },
          error: (error: unknown) => {
            this.busy.set(false);
            this.confirming.set(null);
            this.notifier.error(this.messageFor(error));
          },
        });
        return;
      default:
        return;
    }
  }

  protected openChangesDialog(): void {
    this.changesForm.reset({ comment: '' });
    this.changesDialogOpen.set(true);
  }

  protected requestChanges(): void {
    if (this.changesForm.invalid) {
      this.changesForm.markAllAsTouched();
      return;
    }

    this.perform(
      this.api.requestAngebotChanges(this.id, this.changesForm.getRawValue().comment.trim()),
      this.t().angebotDetail.changesRequested,
      () => this.changesDialogOpen.set(false),
    );
  }

  // ---- Presentation helpers ----------------------------------------------------------------------

  protected vatPercent(rate: VatRateDto): number {
    return VAT_PERCENT[rate];
  }

  protected vatLabel(rate: VatRateDto): string {
    return this.i18n.format(this.t().angebotDetail.vatLine, VAT_PERCENT[rate]);
  }

  // ---- Plumbing ----------------------------------------------------------------------------------

  /**
   * Runs one mutation: block further actions, re-read the document, confirm, or report.
   *
   * The response body of every command is deliberately **ignored**. Some return the header, some the
   * refreshed totals, one the created item — but none returns the whole tree plus the VAT breakdown,
   * and merging partial shapes into local state is how a screen starts disagreeing with the server
   * about what a document contains.
   */
  protected openDuplicate(): void {
    this.duplicateForm.reset({ targetLeadId: null });

    // The API's maximum page, not an arbitrary large number: `Pagination.MaxPageSize` is 100 and
    // the validator rejects anything above it, so an earlier `pageSize: 200` produced a 400 that
    // the user saw as an empty picker. An Inspector's own caseload is comfortably inside one page;
    // if that ever stops being true this needs real paging, not a bigger number.
    this.api
      .leads({ page: 1, pageSize: PAGE_SIZE_MAX })
      .pipe(catchError(() => of({ items: [], page: 1, pageSize: 0, totalCount: 0 })))
      .subscribe((result) =>
        // The source Lead is excluded: this quote already belongs to it, so copying onto itself is
        // never what the user means.
        this.duplicateTargets.set(
          result.items.filter((lead) => lead.id !== this.detail()?.leadId),
        ),
      );

    this.duplicateOpen.set(true);
  }

  /**
   * Creates the copy and navigates to it.
   *
   * Going to the new draft rather than staying: the point of duplicating is to work on the copy,
   * and the source is unchanged and still one click away from its own Lead.
   */
  protected duplicate(): void {
    if (this.duplicateForm.invalid) {
      this.duplicateForm.markAllAsTouched();
      return;
    }

    this.busy.set(true);

    this.api.duplicateAngebot(this.id, this.duplicateForm.getRawValue().targetLeadId!).subscribe({
      next: (copy) => {
        this.busy.set(false);
        this.duplicateOpen.set(false);
        this.notifier.success(this.t().angebotDetail.duplicated);
        void this.router.navigate(['/angebote', copy.id]);
      },
      // A target that already has a live quote comes back 409 (StateMachine §2.4), and a Lead that
      // is not this Inspector's comes back 403. Both are reported, neither is pre-empted.
      error: (error: unknown) => {
        this.busy.set(false);
        this.notifier.error(this.messageFor(error));
      },
    });
  }

  /**
   * Runs one write, then reports it.
   *
   * The two callbacks close two different kinds of dialog, and the difference is the point:
   *
   * - `onSuccess` closes a **form** dialog, and only on success — a refusal there is usually the
   *   user's own input being rejected, and their typed values are still in the form for them to
   *   correct. Closing it would throw the work away along with the message explaining it.
   * - `onSettled` closes a **confirmation**, on both outcomes, because a refusal is terminal for
   *   that click (CLAUDE.md §23): the dialog would otherwise sit on top of the message explaining
   *   why, inviting an identical second refusal. There is nothing in it to preserve.
   *
   * The reload stays success-only either way — a refused write leaves nothing new to read.
   */
  private perform(
    request: Observable<unknown>,
    successMessage: string,
    onSuccess?: () => void,
    onSettled?: () => void,
  ): void {
    this.busy.set(true);

    request.subscribe({
      next: () => {
        this.busy.set(false);
        onSuccess?.();
        onSettled?.();
        this.notifier.success(successMessage);
        this.reload();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        onSettled?.();
        this.notifier.error(this.messageFor(error));
      },
    });
  }

  /** The server's own `detail` is never shown — it is English and written for an API caller. */
  private messageFor(error: unknown): string {
    const kind = error instanceof ApiError ? error.kind : 'server';
    return this.t().errors[kind];
  }
}
