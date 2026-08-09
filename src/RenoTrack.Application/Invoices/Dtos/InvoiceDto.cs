using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Invoices.Dtos;

/// <summary>
/// The shape <c>POST /api/v1/projects/{id}/invoices</c> returns — every column ERD.md's
/// <c>Invoices</c> defines, and nothing else.
///
/// <para>
/// <b>No <c>Payments</c> list.</b> A freshly created Invoice has none, nothing in this slice can
/// create one, and CLAUDE.md §7 adds a nested DTO when a real use case returns it — not before.
/// </para>
/// <para>
/// Monetary values are unwrapped from <see cref="RenoTrack.Domain.ValueObjects.Money"/> to plain
/// <c>decimal</c>; <see cref="InvoiceStatus"/> passes through as-is, serialized as its name (D61).
/// </para>
/// </summary>
public sealed record InvoiceDto(
    int Id,
    int ProjectId,
    string InvoiceNumber,
    DateTime IssueDate,
    DateTime DueDate,
    InvoiceStatus Status,
    decimal NetAmount,
    decimal VatAmount,
    decimal GrossAmount,
    string? VoidReason);

public static class InvoiceMappingExtensions
{
    public static InvoiceDto ToDto(this Invoice invoice) => new(
        invoice.Id,
        invoice.ProjectId,
        invoice.InvoiceNumber,
        invoice.IssueDate,
        invoice.DueDate,
        invoice.Status,
        invoice.NetAmount.Amount,
        invoice.VatAmount.Amount,
        invoice.GrossAmount.Amount,
        invoice.VoidReason);
}
