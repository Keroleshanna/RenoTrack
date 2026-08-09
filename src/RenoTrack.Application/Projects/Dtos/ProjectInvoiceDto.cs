using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Projects.Dtos;

/// <summary>
/// One row of Wireframe E1's Invoices table, as served inside
/// <see cref="ProjectDetailDto"/> (SRS FR-7.4: the Project detail page shows "all associated
/// Invoices in one place").
///
/// <para>
/// <b>The fields are E1's four columns plus the id.</b> E1 renders
/// <c>RE-2026-00017 │ € 8,000 │ Sent │ Due 15.08 │ [Mark Paid]</c>; the id is what that button
/// targets (<c>POST /api/v1/invoices/{id}/mark-paid</c>), so without it the row is undrawable.
/// </para>
/// <para>
/// <b>Deliberately absent:</b> <c>NetAmount</c>/<c>VatAmount</c> (E1 renders neither — the
/// financial split it shows is the Project-level Agreed/Invoiced/Remaining line),
/// <c>IssueDate</c>, <c>VoidReason</c>, <c>ProjectId</c> (redundant inside its parent) and
/// <c>Payments</c>. CLAUDE.md §7: a field is added when a real use case returns it, not before.
/// </para>
/// <para>
/// <b>This is the internal <see cref="InvoiceStatus"/>, not the public one.</b> The Project detail
/// read is a staff surface; <c>PublicInvoiceStatus</c> exists to *withhold* internal states from a
/// customer and has no business here.
/// </para>
/// <para>
/// <b>A separate record from <c>InvoiceDto</c>, not a projection of it.</b> <c>InvoiceDto</c> is
/// what an Invoice command returns — every ERD column. This is a list row on someone else's page,
/// and the two answer different questions.
/// </para>
/// </summary>
public sealed record ProjectInvoiceDto(
    int Id,
    string InvoiceNumber,
    decimal GrossAmount,
    InvoiceStatus Status,
    DateTime DueDate);
