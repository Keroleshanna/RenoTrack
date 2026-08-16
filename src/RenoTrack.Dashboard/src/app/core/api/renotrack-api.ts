import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  AddAngebotItemResult,
  AngebotDetailDto,
  AngebotHeaderDto,
  AngebotListItemDto,
  AngebotReviewCommentDto,
  AngebotStatusDto,
  AngebotSummaryDto,
  CatalogItemDto,
  CatalogItemWrite,
  InspectionDetailDto,
  InspectionDto,
  InvoiceDto,
  InvoiceListItemDto,
  InvoiceStatusDto,
  LeadDto,
  LeadStatusDto,
  ManualLeadSourceDto,
  NotificationDeliveryDto,
  NotificationDeliveryStatusDto,
  PagedResult,
  PaymentMethodDto,
  ProjectDetailDto,
  ProjectDto,
  ProjectInvoiceBalanceDto,
  ProjectListItemDto,
  ProjectStatusDto,
  ReceivablesSummaryDto,
  SectionDto,
  UserSummaryDto,
  VatRateDto,
} from './contracts';

/**
 * Every call the Dashboard makes, in one place.
 *
 * **All paths are relative `/api/v1/...`** (D74), so the same code works unchanged under
 * `Architecture.md` §13's same-origin hosting option; a dev-server proxy bridges the two ports in
 * development only.
 *
 * **Scoping is never a parameter here.** An Inspector's own id is forced server-side on every scoped
 * read (`LeadsController.GetAll`, `AngeboteController.GetAll`, the Inspection reads), so the client
 * neither sends it nor could widen its own visibility by trying. The one exception is an *Admin*
 * narrowing their own view, which is a filter rather than a scope.
 *
 * One thin class rather than six: these are HTTP calls with no logic, and splitting them by resource
 * would mean six files that each inject `HttpClient` and do nothing else.
 */
@Injectable({ providedIn: 'root' })
export class RenoTrackApi {
  private readonly http = inject(HttpClient);

  // ---- Leads -----------------------------------------------------------------------------------

  leads(query: {
    status?: LeadStatusDto;
    assignedInspectorId?: number;
    createdFrom?: string;
    createdTo?: string;
    page?: number;
    pageSize?: number;
  } = {}): Observable<PagedResult<LeadDto>> {
    return this.http.get<PagedResult<LeadDto>>('/api/v1/leads', { params: params(query) });
  }

  lead(id: number): Observable<LeadDto> {
    return this.http.get<LeadDto>(`/api/v1/leads/${id}`);
  }

  /**
   * Admin's manual Lead entry (FR-2.1, `PermissionMatrix.md` §1).
   *
   * A different route from the public contact form, which is anonymous and always produces a
   * `Website` Lead. `source` here is restricted server-side to `Phone`/`Email` by the request
   * type itself, so this path can never fire the FR-9.2 "new website Lead" notification (D86).
   * The creating Admin comes from the token, never the body (D61).
   */
  createLeadManually(lead: {
    name: string;
    phone: string;
    email: string;
    source: ManualLeadSourceDto;
    address: string | null;
    notes: string | null;
  }): Observable<LeadDto> {
    return this.http.post<LeadDto>('/api/v1/leads/manual', lead);
  }

  /**
   * Corrects a Lead's contact details (§1 — Admin any Lead, Inspector their own).
   *
   * `PUT`, so all four fields are replaced: omitting the address clears it. `notes` is deliberately
   * not editable — §1 grants "contact details" and no document grants an edit of the enquiry text.
   */
  updateLeadContactDetails(
    id: number,
    contact: { name: string; phone: string; email: string; address: string | null },
  ): Observable<LeadDto> {
    return this.http.put<LeadDto>(`/api/v1/leads/${id}`, contact);
  }

  /**
   * Assigns or reassigns the responsible Inspector (§1, Admin only).
   *
   * `inspectorId` names *who is acted upon*, so unlike a scoping value it is a genuine input —
   * an Admin is never an Inspector, so deriving it from the token would make this impossible.
   * A 404 here means "no assignable Inspector with that id", which covers unknown, deactivated
   * and wrong-role alike (D62).
   */
  assignLeadInspector(id: number, inspectorId: number): Observable<LeadDto> {
    return this.http.put<LeadDto>(`/api/v1/leads/${id}/inspector`, { inspectorId });
  }

  // ---- Angebote --------------------------------------------------------------------------------

  angebote(query: {
    status?: AngebotStatusDto;
    page?: number;
    pageSize?: number;
  } = {}): Observable<PagedResult<AngebotListItemDto>> {
    return this.http.get<PagedResult<AngebotListItemDto>>('/api/v1/angebote', {
      params: params(query),
    });
  }

  /** The full document — header, sections, items, VAT breakdown (Wireframes D1/D3). */
  angebot(id: number): Observable<AngebotDetailDto> {
    return this.http.get<AngebotDetailDto>(`/api/v1/angebote/${id}`);
  }

  /** Every Angebot on one Lead, newest first. Unpaged — one non-terminal quote per Lead (§2.4). */
  leadAngebote(leadId: number): Observable<readonly AngebotHeaderDto[]> {
    return this.http.get<AngebotHeaderDto[]>(`/api/v1/leads/${leadId}/angebote`);
  }

  /**
   * Starts a Draft for a Lead. Inspector only, and only once the Inspection is done.
   *
   * The creating Inspector is **not** sent: it comes from the token's subject claim server-side
   * (D61). `inspectionId` names what the quote is based on, which is a genuine input.
   */
  createAngebot(leadId: number, inspectionId: number | null): Observable<AngebotHeaderDto> {
    return this.http.post<AngebotHeaderDto>(`/api/v1/leads/${leadId}/angebote`, { inspectionId });
  }

  addSection(angebotId: number, title: string, sortOrder: number): Observable<SectionDto> {
    return this.http.post<SectionDto>(`/api/v1/angebote/${angebotId}/sections`, {
      title,
      sortOrder,
    });
  }

  /**
   * Adds a line in either of FR-4.9's two modes.
   *
   * With `catalogItemId` set the server takes description, specification and unit from the Catalog
   * entry and ignores whatever the body carries for them — so the picker fills the form for the
   * user's benefit, never as the authority.
   */
  addItem(
    angebotId: number,
    item: {
      sectionId: number;
      catalogItemId: number | null;
      description: string | null;
      specification: string | null;
      unitCode: string | null;
      quantity: number;
      unitPrice: number;
      vatRate: VatRateDto;
    },
  ): Observable<AddAngebotItemResult> {
    return this.http.post<AddAngebotItemResult>(`/api/v1/angebote/${angebotId}/items`, item);
  }

  removeSection(angebotId: number, sectionId: number): Observable<AngebotSummaryDto> {
    return this.http.delete<AngebotSummaryDto>(
      `/api/v1/angebote/${angebotId}/sections/${sectionId}`,
    );
  }

  removeItem(angebotId: number, itemId: number): Observable<AngebotSummaryDto> {
    return this.http.delete<AngebotSummaryDto>(`/api/v1/angebote/${angebotId}/items/${itemId}`);
  }

  /**
   * FR-4.11 — starts a fresh Draft on another Lead from this Angebot. Inspector only.
   *
   * Both ownership rules live server-side: the Inspector must own the source *and* the target
   * Lead. StateMachine §2.4's one-active-Angebot rule applies to the target exactly as it does to
   * a fresh creation, so this is not a second route around it — a target that already has a live
   * quote comes back 409.
   */
  duplicateAngebot(id: number, targetLeadId: number): Observable<AngebotHeaderDto> {
    return this.http.post<AngebotHeaderDto>(`/api/v1/angebote/${id}/duplicate`, { targetLeadId });
  }

  submitAngebotForReview(id: number): Observable<AngebotHeaderDto> {
    return this.http.post<AngebotHeaderDto>(`/api/v1/angebote/${id}/submit-for-review`, {});
  }

  approveAngebot(id: number): Observable<AngebotHeaderDto> {
    return this.http.post<AngebotHeaderDto>(`/api/v1/angebote/${id}/approve`, {});
  }

  requestAngebotChanges(id: number, comment: string): Observable<AngebotHeaderDto> {
    return this.http.post<AngebotHeaderDto>(`/api/v1/angebote/${id}/request-changes`, { comment });
  }

  /** Issues the customer's token link and emails it. No value is returned that reveals the token. */
  sendAngebot(id: number): Observable<AngebotHeaderDto> {
    return this.http.post<AngebotHeaderDto>(`/api/v1/angebote/${id}/send`, {});
  }

  angebotReviewComments(id: number): Observable<readonly AngebotReviewCommentDto[]> {
    return this.http.get<AngebotReviewCommentDto[]>(`/api/v1/angebote/${id}/review-comments`);
  }

  // ---- Catalog ---------------------------------------------------------------------------------

  /** The picker's search (Wireframe D2). Retired entries never appear — BR-12, and no flag exists. */
  catalogItems(searchTerm: string, pageSize = 20): Observable<PagedResult<CatalogItemDto>> {
    return this.http.get<PagedResult<CatalogItemDto>>('/api/v1/catalog-items', {
      params: params({ searchTerm, page: 1, pageSize }),
    });
  }

  /**
   * The Catalog management list (Wireframe F1). Both roles browse it (§6 grants View to both).
   *
   * Separate from {@link catalogItems} because the picker and the management screen want different
   * page sizes and paging, not because the endpoint differs — retired entries are invisible to
   * both, since BR-12 provides no flag to include them.
   */
  catalogPage(query: { searchTerm?: string; page?: number; pageSize?: number } = {}): Observable<
    PagedResult<CatalogItemDto>
  > {
    return this.http.get<PagedResult<CatalogItemDto>>('/api/v1/catalog-items', {
      params: params(query),
    });
  }

  /**
   * Creates a curated Catalog entry (§6 — Admin only; Inspectors contribute via "save as").
   *
   * Note the asymmetry between write and read: the request names the unit `defaultUnitCode`
   * because a unit is a value object addressed by its code (`ItemUnit.FromCode`), while the
   * response DTO carries the resolved `defaultUnit`. That is the server's contract, not an
   * inconsistency to paper over here.
   */
  createCatalogItem(item: CatalogItemWrite): Observable<CatalogItemDto> {
    return this.http.post<CatalogItemDto>('/api/v1/catalog-items', item);
  }

  /** Edits an existing entry (§6 — Admin only). BR-8 keeps past Angebote unaffected. */
  updateCatalogItem(id: number, item: CatalogItemWrite): Observable<CatalogItemDto> {
    return this.http.put<CatalogItemDto>(`/api/v1/catalog-items/${id}`, item);
  }

  /**
   * Retires an entry (§6 — Admin only). **Never a delete**: BR-12 keeps the row so BR-8's trace
   * links stay valid and BR-14 keeps it usable as a direct reference. Retirement affects
   * discovery only, which is why the item then vanishes from every list including this one.
   */
  retireCatalogItem(id: number): Observable<CatalogItemDto> {
    return this.http.post<CatalogItemDto>(`/api/v1/catalog-items/${id}/retire`, {});
  }

  /** FR-4.10 — promotes a custom line into the shared Catalog. Any Inspector may contribute. */
  saveItemAsCatalogItem(angebotItemId: number): Observable<CatalogItemDto> {
    return this.http.post<CatalogItemDto>(
      `/api/v1/angebot-items/${angebotItemId}/save-as-catalog-item`,
      {},
    );
  }

  // ---- Invoices (Admin only — PermissionMatrix.md §5) -------------------------------------------

  invoices(query: {
    status?: InvoiceStatusDto;
    projectId?: number;
    dueBefore?: string;
    page?: number;
    pageSize?: number;
  } = {}): Observable<PagedResult<InvoiceListItemDto>> {
    return this.http.get<PagedResult<InvoiceListItemDto>>('/api/v1/invoices', {
      params: params(query),
    });
  }

  /**
   * @param asOf The date "overdue" is judged against. Sent explicitly so the client's own notion of
   * today decides, rather than the server's timezone.
   */
  receivables(asOf: Date): Observable<ReceivablesSummaryDto> {
    return this.http.get<ReceivablesSummaryDto>('/api/v1/invoices/receivables', {
      params: params({ asOf: isoDate(asOf) }),
    });
  }

  /**
   * Creates one Invoice against a Project (FR-8.1/8.2, Wireframe E2).
   *
   * **Exceeding the remaining balance is accepted, not refused** — BR-3 warns rather than blocks, so
   * the result is a negative `remaining`, never an error the UI should pre-empt.
   */
  createInvoice(projectId: number, grossAmount: number, dueDate: string): Observable<InvoiceDto> {
    return this.http.post<InvoiceDto>(`/api/v1/projects/${projectId}/invoices`, {
      grossAmount,
      dueDate,
    });
  }

  /** Sends a Draft Invoice as a token link (FR-8.3). No PDF is generated — that is Phase 14. */
  sendInvoice(id: number): Observable<InvoiceDto> {
    return this.http.post<InvoiceDto>(`/api/v1/invoices/${id}/send`, {});
  }

  /** Records full payment (FR-8.4, Wireframe E3). No amount exists to send — Phase 8 is full-only. */
  markInvoicePaid(id: number, paidAt: string, method: PaymentMethodDto): Observable<InvoiceDto> {
    return this.http.post<InvoiceDto>(`/api/v1/invoices/${id}/mark-paid`, { paidAt, method });
  }

  /** Cancels an Invoice. BR-9: the row and its number survive — this is never a delete. */
  voidInvoice(id: number, reason: string): Observable<InvoiceDto> {
    return this.http.post<InvoiceDto>(`/api/v1/invoices/${id}/void`, { reason });
  }

  // ---- Projects --------------------------------------------------------------------------------

  projects(query: {
    status?: ProjectStatusDto;
    page?: number;
    pageSize?: number;
  } = {}): Observable<PagedResult<ProjectListItemDto>> {
    return this.http.get<PagedResult<ProjectListItemDto>>('/api/v1/projects', {
      params: params(query),
    });
  }

  /**
   * The Project detail (Wireframe E1). Readable by both roles and unscoped — `PermissionMatrix.md`
   * §5's own rule — which confers **no** Invoice permission.
   */
  project(id: number): Observable<ProjectDetailDto> {
    return this.http.get<ProjectDetailDto>(`/api/v1/projects/${id}`);
  }

  /** BR-3's agreed / invoiced / remaining. A negative `remaining` is the warning, not an error. */
  projectInvoiceBalance(id: number): Observable<ProjectInvoiceBalanceDto> {
    return this.http.get<ProjectInvoiceBalanceDto>(`/api/v1/projects/${id}/invoice-balance`);
  }

  /**
   * Closes a Project (FR-8.6). Admin only.
   *
   * The override is sent **only** when the caller genuinely overrides: a reason without it is
   * rejected server-side rather than ignored, so an empty body is the one representation of the
   * ordinary case.
   */
  completeProject(id: number, override?: { readonly reason: string }): Observable<ProjectDto> {
    return this.http.post<ProjectDto>(
      `/api/v1/projects/${id}/complete`,
      override ? { forceOverride: true, reason: override.reason } : {},
    );
  }

  /**
   * Turns a customer-approved Angebot into a Project (FR-7.1, BR-2). Admin only.
   *
   * No body at all: the Angebot comes from the route and the Admin from the token (D61).
   */
  convertAngebotToProject(angebotId: number): Observable<ProjectDto> {
    return this.http.post<ProjectDto>(`/api/v1/angebote/${angebotId}/convert-to-project`, {});
  }

  // ---- Inspections -----------------------------------------------------------------------------

  /** Schedules a site visit (FR-2.3). Admin only; BR-13 assigns that Inspector to the Lead too. */
  scheduleInspection(
    leadId: number,
    scheduledAt: string,
    inspectorId: number,
  ): Observable<InspectionDto> {
    return this.http.post<InspectionDto>(`/api/v1/leads/${leadId}/inspections`, {
      scheduledAt,
      inspectorId,
    });
  }

  inspection(id: number): Observable<InspectionDetailDto> {
    return this.http.get<InspectionDetailDto>(`/api/v1/inspections/${id}`);
  }

  /**
   * Records the Inspector's on-site notes (FR-3.3). Assigned Inspector only, and only while the
   * Inspection is open — BR-10 makes a completed Inspection immutable, and the aggregate enforces it.
   */
  updateInspectionNotes(id: number, notes: string | null): Observable<InspectionDto> {
    return this.http.patch<InspectionDto>(`/api/v1/inspections/${id}`, { notes });
  }

  /**
   * Uploads one photo as evidence (FR-3.2).
   *
   * `FormData`, and **no explicit `Content-Type`**: the browser must set the multipart boundary
   * itself, and naming the header here would produce a boundary-less type the model binder rejects.
   */
  uploadInspectionPhoto(id: number, file: File, caption: string | null): Observable<unknown> {
    const body = new FormData();
    body.append('File', file);
    if (caption) {
      body.append('Caption', caption);
    }

    return this.http.post(`/api/v1/inspections/${id}/photos`, body);
  }

  /**
   * Closes the site visit (FR-3.4). This is what moves the Lead to `InspectionDone` and therefore
   * what makes an Angebot possible — and it is irreversible (BR-10).
   */
  completeInspection(id: number): Observable<InspectionDto> {
    return this.http.post<InspectionDto>(`/api/v1/inspections/${id}/complete`, {});
  }

  /**
   * Moves a scheduled visit to a different Inspector (§2 — Admin only).
   *
   * BR-13 follows: the Lead's assigned Inspector moves with the visit, in the same commit. **409
   * once the visit is completed** — BR-10 makes a finished Inspection immutable, so the screen
   * stops offering this rather than inviting a refusal.
   */
  reassignInspection(id: number, inspectorId: number): Observable<InspectionDto> {
    return this.http.put<InspectionDto>(`/api/v1/inspections/${id}/inspector`, { inspectorId });
  }

  /** `to` is exclusive server-side, so `[day, day+1)` is exactly one day. */
  inspections(from: Date, to: Date, includeCompleted = true): Observable<InspectionDetailDto[]> {
    return this.http.get<InspectionDetailDto[]>('/api/v1/inspections', {
      params: params({ from: isoDate(from), to: isoDate(to), includeCompleted }),
    });
  }

  /**
   * Pauses an active Project (§5 — Admin only, StateMachine §4.3 `Active → OnHold`).
   *
   * No reason is sent because no column stores one — see the command's own reasoning. Invoicing
   * continues to work while paused (§5 permits an Invoice against `Active` *or* `OnHold`).
   */
  putProjectOnHold(id: number): Observable<ProjectDto> {
    return this.http.post<ProjectDto>(`/api/v1/projects/${id}/hold`, {});
  }

  /** Resumes a paused Project (§5, `OnHold → Active`) — the mirror of {@link putProjectOnHold}. */
  resumeProject(id: number): Observable<ProjectDto> {
    return this.http.post<ProjectDto>(`/api/v1/projects/${id}/resume`, {});
  }

  // ---- Users -----------------------------------------------------------------------------------

  staff(role?: 'Admin' | 'Inspector'): Observable<UserSummaryDto[]> {
    return this.http.get<UserSummaryDto[]>('/api/v1/users', { params: params({ role }) });
  }

  // ---- Notification deliveries (Admin only — PermissionMatrix.md §9) ----------------------------

  /**
   * The operational view of outgoing email (§9, D69).
   *
   * Needed because a committed business operation stays successful when its notification fails —
   * and two of the six senders are anonymous public endpoints, where no Admin was present to be
   * told anything at the time.
   *
   * Omitting `status` returns every status including `Sent`, deliberately: §9 says "failed/pending",
   * but hiding successes would make "did my retry actually work?" unanswerable.
   */
  notificationDeliveries(query: {
    status?: NotificationDeliveryStatusDto;
    page?: number;
    pageSize?: number;
  } = {}): Observable<PagedResult<NotificationDeliveryDto>> {
    return this.http.get<PagedResult<NotificationDeliveryDto>>('/api/v1/notification-deliveries', {
      params: params(query),
    });
  }

  /**
   * Re-sends **only** the notification, rebuilt from currently persisted business data — it never
   * re-executes the underlying business operation and never mints a token (D70).
   *
   * Manual and synchronous by design: no backoff, no attempt cap, no background sweeper. The row
   * is claimed by one atomic conditional `UPDATE`, so two Admins double-clicking produce one send
   * and one 409.
   *
   * **Refusals are 409 and are never repaired** — already `Sent`, a lost claim race, an expired or
   * used `TokenLink`, a `Void`/`Paid` Invoice, or email disabled for the deployment. A *delivery*
   * failure is not a refusal: that returns 200 with the row recording `Failed`.
   */
  retryNotificationDelivery(id: number): Observable<NotificationDeliveryDto> {
    return this.http.post<NotificationDeliveryDto>(
      `/api/v1/notification-deliveries/${id}/retry`,
      {},
    );
  }
}

/** Drops absent values, so an unset filter never becomes the literal string "undefined". */
function params(query: Record<string, unknown>): HttpParams {
  let result = new HttpParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== '') {
      result = result.set(key, String(value));
    }
  }
  return result;
}

/**
 * A local calendar date as `yyyy-MM-dd`.
 *
 * **Not `toISOString()`**, which converts to UTC first and can shift the date across midnight —
 * a schedule request for "today" made at 01:00 in Berlin would otherwise ask for yesterday.
 */
function isoDate(value: Date): string {
  const month = `${value.getMonth() + 1}`.padStart(2, '0');
  const day = `${value.getDate()}`.padStart(2, '0');
  return `${value.getFullYear()}-${month}-${day}`;
}
