using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Builds Invoices for tests that need one but are not about how it is built (Phase 14 Slice 2,
/// D111). <c>Invoice.Create</c> takes the originating Angebot's rate mix and calculates the split
/// itself; a single 19 % mix stands in for the Angebot, so the stored net is the gross / 1.19
/// rounded per BR-11, exactly what these tests computed by hand before.
/// </summary>
internal static class TestInvoices
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
