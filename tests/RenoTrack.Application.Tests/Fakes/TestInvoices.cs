using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Fakes;

/// <summary>
/// Builds Invoices for tests that need one but are not about how it is built (Phase 14 Slice 2,
/// D111). <c>Invoice.Create</c> now takes the originating Angebot's rate mix and calculates the
/// split itself; these tests only need "an invoice of this gross", so a single 19 % mix stands in
/// for the Angebot. 8,000.00 gross therefore splits into 6,722.69 net and 1,277.31 VAT.
/// </summary>
public static class TestInvoices
{
    public const string Description = "Abschlag 1: Malerarbeiten Erdgeschoss";

    public static readonly IReadOnlyList<VatBreakdownLine> StandardRateMix =
        [new VatBreakdownLine(VatRate.Standard, Money.FromExact(1_000.00m), Money.FromExact(190.00m))];

    public static Invoice AtStandardRate(int projectId, string invoiceNumber, DateTime dueDate, decimal gross) =>
        Invoice.Create(
            projectId,
            invoiceNumber,
            DateTime.UtcNow,
            dueDate,
            Money.FromExact(gross),
            StandardRateMix,
            Description,
            servicePeriodStart: null,
            servicePeriodEnd: null);
}
