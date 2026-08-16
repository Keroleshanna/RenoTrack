/**
 * The backend's wire contracts, transcribed from the C# DTOs they mirror.
 *
 * **The only place backend shapes are allowed to appear.** Screens hold their own view models and a
 * service maps between the two, so a DTO change lands in one mapper rather than in every template
 * that happened to read a field.
 *
 * Enums arrive as **names, not ordinals** (`JsonStringEnumConverter`, D61), so the string unions
 * below are the real wire values.
 */

/** `PagedResult<T>` — `totalCount` is every matching row, not just this page's. */
export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

export const PAGE_SIZE_DEFAULT = 25;
export const PAGE_SIZE_MAX = 100;

// ---- Auth ---------------------------------------------------------------------------------------

export interface AuthResponseDto {
  readonly accessToken: string;
  readonly accessTokenExpiresAt: string;
  readonly refreshToken: string;
  readonly refreshTokenExpiresAt: string;
  readonly userId: number;
  readonly name: string;
  readonly email: string;
  /** Exactly the two Identity role names. */
  readonly role: 'Admin' | 'Inspector';
}

// ---- Leads --------------------------------------------------------------------------------------

export const LEAD_STATUSES = [
  'New',
  'InspectionScheduled',
  'InspectionDone',
  'AngebotInProgress',
  'AngebotSent',
  'Won',
  'Lost',
] as const;

export type LeadStatusDto = (typeof LEAD_STATUSES)[number];

/** `Won` and `Lost` are terminal (`StateMachine.md` §1.4) — history, not work. */
export const TERMINAL_LEAD_STATUSES: readonly LeadStatusDto[] = ['Won', 'Lost'];

export type LeadSourceDto = 'Website' | 'Phone' | 'Email';

/**
 * The sources an Admin may choose when logging a Lead by hand (FR-2.1).
 *
 * Narrower than {@link LeadSourceDto} on purpose, mirroring the server's own `ManualLeadSource`:
 * `Website` is not expressible, so the manual path can never claim an enquiry arrived through a
 * form nobody filled in — which would fire FR-9.2's new-website-Lead notification (D86).
 */
export const MANUAL_LEAD_SOURCES = ['Phone', 'Email'] as const;

export type ManualLeadSourceDto = (typeof MANUAL_LEAD_SOURCES)[number];

export interface LeadDto {
  readonly id: number;
  readonly name: string;
  readonly phone: string;
  readonly email: string;
  readonly address: string | null;
  readonly notes: string | null;
  readonly source: LeadSourceDto;
  readonly status: LeadStatusDto;
  readonly assignedInspectorId: number | null;
  readonly createdAt: string;
}

// ---- Angebote -----------------------------------------------------------------------------------

export const ANGEBOT_STATUSES = [
  'Draft',
  'InReview',
  'ChangesRequested',
  'ApprovedInternally',
  'Sent',
  'CustomerApproved',
  'CustomerRejected',
] as const;

export type AngebotStatusDto = (typeof ANGEBOT_STATUSES)[number];

export interface AngebotListItemDto {
  readonly id: number;
  readonly angebotNumber: string;
  readonly leadId: number;
  readonly leadName: string;
  readonly status: AngebotStatusDto;
  readonly netTotal: number;
  readonly grossTotal: number;
  readonly createdByInspectorId: number;
  readonly createdAt: string;
  readonly sentAt: string | null;
  readonly decisionAt: string | null;
}

/**
 * The header-only shape every Angebot *command* returns (`AngebotDto`).
 *
 * Deliberately not the same type as the detail read: the backend keeps them apart because a
 * transition response has no reason to serialise the whole tree (CLAUDE.md §7), and mirroring that
 * split here is what stops a screen assuming `sections` exists on a value that never carries it.
 */
export interface AngebotHeaderDto {
  readonly id: number;
  readonly leadId: number;
  readonly inspectionId: number | null;
  readonly angebotNumber: string;
  readonly status: AngebotStatusDto;
  readonly createdByInspectorId: number;
  readonly reviewedByAdminId: number | null;
  readonly sentAt: string | null;
  readonly decisionAt: string | null;
  readonly createdAt: string;
  readonly netTotal: number;
  readonly grossTotal: number;
}

/**
 * The four German VAT rates (`VatRate`), as **names** on the wire and percentages on screen.
 *
 * The enum's underlying values already *are* the percentages server-side, but `JsonStringEnumConverter`
 * (D61) sends the name — so the percentage a document must show is reconstructed here rather than
 * parsed out of a label.
 */
export const VAT_RATES = [
  { value: 'Standard', percent: 19 },
  { value: 'Sixteen', percent: 16 },
  { value: 'Reduced', percent: 7 },
  { value: 'Zero', percent: 0 },
] as const;

export type VatRateDto = (typeof VAT_RATES)[number]['value'];

export const VAT_PERCENT: Readonly<Record<VatRateDto, number>> = {
  Standard: 19,
  Sixteen: 16,
  Reduced: 7,
  Zero: 0,
};

/**
 * The five standard `ItemUnit` codes.
 *
 * `ItemUnit.FromCode` accepts anything else as a *custom* unit, which is why the builder offers a
 * free-text option alongside these rather than a closed dropdown — the Domain's own contract is open.
 */
export const STANDARD_UNITS = ['m2', 'Stk', 'lfm', 'pauschal', 'm'] as const;

export interface ItemDto {
  readonly id: number;
  readonly catalogItemId: number | null;
  readonly description: string;
  readonly specification: string | null;
  readonly quantity: number;
  readonly unit: string;
  readonly unitPrice: number;
  readonly vatRate: VatRateDto;
  readonly lineTotal: number;
}

export interface SectionDetailDto {
  readonly id: number;
  readonly title: string;
  readonly sortOrder: number;
  readonly subtotal: number;
  readonly items: readonly ItemDto[];
}

/** One row of the "zzgl. X % MwSt" breakdown — computed, never stored (Architecture.md §6.1). */
export interface VatBreakdownLineDto {
  readonly rate: VatRateDto;
  readonly netAmount: number;
  readonly vatAmount: number;
}

/** The full document: header, the section/item tree, and the VAT breakdown (Wireframes D1/D3). */
export interface AngebotDetailDto {
  readonly id: number;
  readonly leadId: number;
  readonly inspectionId: number | null;
  readonly angebotNumber: string;
  readonly status: AngebotStatusDto;
  readonly createdByInspectorId: number;
  readonly reviewedByAdminId: number | null;
  readonly sentAt: string | null;
  readonly decisionAt: string | null;
  readonly createdAt: string;
  readonly netTotal: number;
  readonly grossTotal: number;
  readonly vatBreakdown: readonly VatBreakdownLineDto[];
  readonly sections: readonly SectionDetailDto[];
}

/** What a section/item removal returns — the refreshed header totals, not the tree. */
export interface AngebotSummaryDto {
  readonly id: number;
  readonly angebotNumber: string;
  readonly status: AngebotStatusDto;
  readonly netTotal: number;
  readonly grossTotal: number;
}

export interface SectionDto {
  readonly id: number;
  readonly title: string;
  readonly sortOrder: number;
  readonly subtotal: number;
}

export interface AddAngebotItemResult {
  readonly item: ItemDto;
  readonly summary: AngebotSummaryDto;
}

/** The internal review log (SRS FR-5.4) — append-only, oldest first. */
export interface AngebotReviewCommentDto {
  readonly id: number;
  readonly angebotId: number;
  readonly adminUserId: number;
  readonly comment: string;
  readonly createdAt: string;
}

// ---- Catalog ------------------------------------------------------------------------------------

/**
 * What a Catalog create/update sends, as distinct from what a read returns.
 *
 * `defaultUnitCode` rather than `defaultUnit`: a unit is a value object addressed by its code on
 * the way in (`ItemUnit.FromCode`) and resolved on the way out. Naming the write shape separately
 * is what stops the read DTO's field name being sent by mistake — which is exactly the 400 this
 * type was introduced to prevent.
 */
export interface CatalogItemWrite {
  readonly title: string;
  readonly defaultUnitCode: string;
  readonly suggestedUnitPrice: number;
  readonly defaultSpecification: string | null;
}

export interface CatalogItemDto {
  readonly id: number;
  readonly title: string;
  readonly defaultSpecification: string | null;
  readonly defaultUnit: string;
  readonly suggestedUnitPrice: number;
  readonly createdFromAngebotItemId: number | null;
  readonly isRetired: boolean;
  readonly createdAt: string;
}

// ---- Invoices -----------------------------------------------------------------------------------

export const INVOICE_STATUSES = ['Draft', 'Sent', 'Paid', 'Overdue', 'Void'] as const;

export type InvoiceStatusDto = (typeof INVOICE_STATUSES)[number];

export interface InvoiceListItemDto {
  readonly id: number;
  readonly invoiceNumber: string;
  readonly projectId: number;
  readonly customerId: number;
  readonly customerName: string;
  readonly status: InvoiceStatusDto;
  readonly issueDate: string;
  readonly dueDate: string;
  readonly netAmount: number;
  readonly vatAmount: number;
  readonly grossAmount: number;
  readonly paidAt: string | null;
  readonly voidReason: string | null;
}

/** What every Invoice *command* returns. Carries no customer name — the list read supplies that. */
export interface InvoiceDto {
  readonly id: number;
  readonly projectId: number;
  readonly invoiceNumber: string;
  readonly issueDate: string;
  readonly dueDate: string;
  readonly status: InvoiceStatusDto;
  readonly netAmount: number;
  readonly vatAmount: number;
  readonly grossAmount: number;
  readonly voidReason: string | null;
}

/** `PaymentMethod` — Phase 8 records full payment only, so no amount accompanies it. */
export const PAYMENT_METHODS = ['BankTransfer', 'Cash', 'Other'] as const;

export type PaymentMethodDto = (typeof PAYMENT_METHODS)[number];

/**
 * The receivables position.
 *
 * `overdueGross` is derived server-side as `Sent && dueDate < asOf` — **not** read from
 * `InvoiceStatus.Overdue`, which nothing ever sets (there is no scheduler, by decision).
 */
export interface ReceivablesSummaryDto {
  readonly invoicedGross: number;
  readonly paidGross: number;
  readonly openGross: number;
  readonly overdueGross: number;
  readonly voidedGross: number;
  readonly invoiceCount: number;
  readonly openCount: number;
  readonly overdueCount: number;
}

// ---- Projects -----------------------------------------------------------------------------------

export const PROJECT_STATUSES = ['Active', 'OnHold', 'Completed'] as const;

export type ProjectStatusDto = (typeof PROJECT_STATUSES)[number];

export interface ProjectListItemDto {
  readonly id: number;
  readonly status: ProjectStatusDto;
  readonly agreedTotal: number;
  readonly createdAt: string;
  readonly completedAt: string | null;
  readonly customerId: number;
  readonly customerName: string;
  readonly angebotId: number;
  readonly angebotNumber: string;
}

/** The header-only shape a Project *command* returns (conversion, completion). */
export interface ProjectDto {
  readonly id: number;
  readonly customerId: number;
  readonly angebotId: number;
  readonly status: ProjectStatusDto;
  readonly agreedTotal: number;
  readonly createdAt: string;
  readonly completedAt: string | null;
}

/** One Invoice as it appears on its Project (FR-7.4, Wireframe E1). */
export interface ProjectInvoiceDto {
  readonly id: number;
  readonly invoiceNumber: string;
  readonly grossAmount: number;
  readonly status: InvoiceStatusDto;
  readonly dueDate: string;
}

/**
 * The Project detail read (Wireframe E1).
 *
 * `remaining` **may be negative** — that is BR-3's warning, not an error, and clamping it here would
 * delete the only signal the rule exists to produce.
 */
export interface ProjectDetailDto {
  readonly id: number;
  readonly status: ProjectStatusDto;
  readonly agreedTotal: number;
  readonly createdAt: string;
  readonly completedAt: string | null;
  readonly customerId: number;
  readonly customerName: string;
  readonly leadId: number;
  readonly inspectionId: number | null;
  readonly angebotId: number;
  readonly angebotNumber: string;
  readonly alreadyInvoiced: number;
  readonly remaining: number;
  readonly invoices: readonly ProjectInvoiceDto[];
}

export interface ProjectInvoiceBalanceDto {
  readonly projectId: number;
  readonly agreedTotal: number;
  readonly alreadyInvoiced: number;
  readonly remaining: number;
}

// ---- Inspections --------------------------------------------------------------------------------

/** What scheduling returns — the aggregate itself, without the Lead's contact details. */
export interface InspectionDto {
  readonly id: number;
  readonly leadId: number;
  readonly scheduledAt: string;
  readonly inspectorId: number;
  readonly notes: string | null;
  readonly completedAt: string | null;
}

export interface InspectionDetailDto {
  readonly id: number;
  readonly leadId: number;
  readonly leadName: string;
  readonly leadAddress: string | null;
  readonly leadPhone: string;
  readonly scheduledAt: string;
  readonly inspectorId: number;
  readonly notes: string | null;
  readonly completedAt: string | null;
  readonly photoCount: number;
}

// ---- Users --------------------------------------------------------------------------------------

export interface UserSummaryDto {
  readonly id: number;
  readonly name: string;
  readonly role: string;
  readonly isActive: boolean;
}

// ---- Notification deliveries (Admin only — PermissionMatrix.md §9) -------------------------------

/**
 * `Pending → Sending → Sent | Failed`, with `Failed → Sending` and `Sending → Sending` reachable
 * only by a manual retry (D69).
 *
 * **`Sending` is retryable, and that is deliberate, not an oversight.** There is no lease, timeout
 * or sweeper anywhere in this system, so a process that died mid-attempt strands a row here
 * permanently — an Admin clicking retry again is the only thing that can rescue it.
 */
export const NOTIFICATION_DELIVERY_STATUSES = ['Pending', 'Sending', 'Sent', 'Failed'] as const;

export type NotificationDeliveryStatusDto = (typeof NOTIFICATION_DELIVERY_STATUSES)[number];

/** The six senders. Two of them — `NewWebsiteLead` and `AngebotDecision` — are anonymous. */
export type NotificationTypeDto =
  | 'NewWebsiteLead'
  | 'AngebotSubmittedForReview'
  | 'AngebotChangesRequested'
  | 'AngebotReady'
  | 'InvoiceReady'
  | 'AngebotDecision';

/**
 * The twelve persisted columns, flat.
 *
 * `entityType`/`entityId` are **not** resolved to a title or a link: the reference is polymorphic
 * with no foreign key, so there is nothing to join against. A null `recipient` is meaningful — it
 * is precisely the case where recipient resolution itself failed, which is what retry most needs
 * to serve.
 */
export interface NotificationDeliveryDto {
  readonly id: number;
  readonly notificationType: NotificationTypeDto;
  readonly entityType: string;
  readonly entityId: number;
  readonly status: NotificationDeliveryStatusDto;
  readonly recipient: string | null;
  readonly createdAt: string;
  readonly lastAttemptAt: string | null;
  readonly attemptCount: number;
  readonly sentAt: string | null;
  readonly failureType: string | null;
  readonly failureMessage: string | null;
}
