using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Invoices.Dtos;

/// <summary>
/// The shape <c>POST /api/v1/projects/{id}/invoices</c> returns — every column ERD.md's
/// <c>Invoices</c> defines, plus the per-rate VAT lines the Invoice aggregate calculated (D111).
///
/// <para>
/// <b>No <c>Payments</c> list.</b> A freshly created Invoice has none, nothing in this slice can
/// create one, and CLAUDE.md §7 adds a nested DTO when a real use case returns it — not before.
/// </para>
/// <para>
/// <b><see cref="VatLines"/> is a read of what the system calculated, never an input.</b> No request
/// shape anywhere accepts it.
/// </para>
/// <para>
/// Monetary values are unwrapped from <see cref="RenoTrack.Domain.ValueObjects.Money"/> to plain
/// <c>decimal</c>; <see cref="InvoiceStatus"/> and <see cref="VatRate"/> pass through as-is,
/// serialized as their names (D61).
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
    string? VoidReason,
    string Description,
    DateOnly? ServicePeriodStart,
    DateOnly? ServicePeriodEnd,
    IReadOnlyList<InvoiceVatLineDto> VatLines);

/// <summary>One rate's share of an Invoice, as <c>Invoice.Create</c> calculated it.</summary>
public sealed record InvoiceVatLineDto(VatRate Rate, decimal NetAmount, decimal VatAmount);

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
        invoice.VoidReason,
        invoice.Description,
        invoice.ServicePeriodStart,
        invoice.ServicePeriodEnd,
        invoice.VatLines
            .Select(line => new InvoiceVatLineDto(line.Rate, line.NetAmount.Amount, line.VatAmount.Amount))
            .ToList());
}
