using RenoTrack.Domain.Enums;

namespace RenoTrack.Api.Invoices.Dtos;

/// <summary>
/// The body of <c>POST /api/v1/projects/{projectId}/invoices</c> — the two fields Sequence Diagram
/// §8 sends (<c>{ grossAmount, dueDate }</c>) and Wireframe E2 collects, plus the description and
/// optional service period Phase 14 Slice 2 added for the Invoice document (D111, BR-5).
///
/// <para>
/// <b>No VAT rate, VAT amount, net amount or VAT line is accepted, deliberately (D111).</b> The Admin
/// decides how much this invoice bills (<c>grossAmount</c>, FR-8.1's splitting); the Invoice
/// aggregate decides how that splits by rate, from the originating Angebot. A client that sends any
/// such field has it ignored by model binding — it is neither stored nor echoed, and a test pins it.
/// </para>
/// <para>
/// <c>Description</c> is nullable here only so that an absent field and a blank one reach the same
/// validator rule and produce the same field-keyed 400, rather than one of them being caught earlier
/// by model binding with a different message.
/// </para>
///
/// <para>
/// A strict subset of <c>CreateInvoiceCommand</c>, which is what justifies the record existing at
/// all (D61): the Project id comes from the route, and the creating Admin from the token's subject
/// claim. Neither is accepted from the caller.
/// </para>
/// <para>
/// <b>The invoice number is absent, deliberately</b> — it is reserved server-side from the
/// <c>NumberSequences</c> table (Architecture.md §8). A caller-supplied number could collide with a
/// reserved one or reuse a voided one, which BR-9 forbids outright.
/// </para>
/// </summary>
public sealed record CreateInvoiceRequest(
    decimal GrossAmount,
    DateTime DueDate,
    string? Description,
    DateOnly? ServicePeriodStart,
    DateOnly? ServicePeriodEnd);

/// <summary>
/// The body of <c>POST /api/v1/invoices/{id}/mark-paid</c> — exactly the two fields Sequence Diagram
/// §9 sends (<c>{ paidAt, method }</c>) and Wireframe E3 collects.
///
/// <para>
/// <b>No amount, deliberately.</b> Phase 8 records full payment only: the Payment always carries the
/// Invoice's own gross. FR-8.4, §9's body and E3 all offer no amount to supply, so accepting one
/// would invent partial-payment semantics no document defines.
/// </para>
/// <para>
/// The recording Admin comes from the token's subject claim, never the body (D61).
/// </para>
/// </summary>
public sealed record RecordPaymentRequest(DateTime PaidAt, PaymentMethod Method);

/// <summary>
/// The body of <c>POST /api/v1/invoices/{id}/void</c>. `PermissionMatrix.md` §5 requires a reason
/// without qualification, so this is the whole body — the Invoice id comes from the route and the
/// acting Admin from the token (D61).
/// </summary>
public sealed record VoidInvoiceRequest(string Reason);
