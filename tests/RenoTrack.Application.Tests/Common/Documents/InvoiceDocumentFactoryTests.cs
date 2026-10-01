using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Documents;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;

namespace RenoTrack.Application.Tests.Common.Documents;

/// <summary>
/// Phase 14 Slice 2 (D111): what the Invoice document says is exactly what the Invoice aggregate
/// and its Customer hold, formatted once — and an invoice the document would misrepresent is refused
/// rather than printed around. The historical-row refusals (empty description, no VAT lines) need a
/// row only a real database can hold, so they are proved in <c>InvoiceDocumentLegacyRowTests</c>.
/// </summary>
public class InvoiceDocumentFactoryTests
{
    private const string Address = "Musterstraße 1\n12345 Musterstadt";

    private static readonly InvoiceCalendar Calendar = InvoiceCalendar.ForEuropeBerlin();

    private static readonly DateTime IssuedAt = new(2026, 10, 1, 8, 15, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<VatBreakdownLine> MixedRateMix =
    [
        new VatBreakdownLine(VatRate.Reduced, Money.FromExact(1_000.00m), Money.FromExact(70.00m)),
        new VatBreakdownLine(VatRate.Standard, Money.FromExact(5_000.00m), Money.FromExact(950.00m)),
    ];

    private static Customer CustomerWith(string? address) =>
        Customer.Create(leadId: 1, "Erika Mustermann", "erika@example.test", "+49 000 1234567", address);

    private static Invoice MixedRateInvoice(
        decimal gross = 7_020.00m,
        DateOnly? servicePeriodStart = null,
        DateOnly? servicePeriodEnd = null,
        DateTime? issuedAt = null) =>
        Invoice.Create(
            projectId: 7,
            invoiceNumber: "RE-2026-00017",
            issuedAt: issuedAt ?? IssuedAt,
            dueDate: new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc),
            grossAmount: Money.FromExact(gross),
            rateMix: MixedRateMix,
            description: "Malerarbeiten Erdgeschoss",
            servicePeriodStart: servicePeriodStart,
            servicePeriodEnd: servicePeriodEnd);

    [Fact]
    public void TheDocumentCarriesTheInvoicesOwnFields()
    {
        var invoice = MixedRateInvoice(servicePeriodStart: new DateOnly(2026, 9, 1), servicePeriodEnd: new DateOnly(2026, 9, 30));

        var document = InvoiceDocumentFactory.Create(invoice, CustomerWith(Address), Calendar);

        Assert.Equal("RE-2026-00017", document.InvoiceNumber);
        Assert.Equal(new DateOnly(2026, 10, 1), document.IssuedOn);
        Assert.Equal(new DateOnly(2026, 10, 15), document.DueOn);
        Assert.Equal(new DateOnly(2026, 9, 1), document.ServicePeriodStart);
        Assert.Equal(new DateOnly(2026, 9, 30), document.ServicePeriodEnd);
        Assert.Equal("Malerarbeiten Erdgeschoss", document.Description);
        Assert.Equal("Erika Mustermann", document.Customer.Name);
        Assert.Equal(Address, document.Customer.Address);
    }

    /// <summary>
    /// Every figure is the Invoice aggregate's stored figure, formatted — the factory computes
    /// nothing (D78 applied to paper). 7,020.00 across 7 % and 19 % is 1,000 + 70 and 5,000 + 950.
    /// </summary>
    [Fact]
    public void TheFiguresAreTheInvoicesStoredLinesAndTotalsFormatted()
    {
        var document = InvoiceDocumentFactory.Create(MixedRateInvoice(), CustomerWith(Address), Calendar);

        Assert.Collection(
            document.VatBreakdown,
            line =>
            {
                Assert.Equal("7 %", line.VatRate);
                Assert.Equal("1.000,00 €", line.NetAmount);
                Assert.Equal("70,00 €", line.VatAmount);
            },
            line =>
            {
                Assert.Equal("19 %", line.VatRate);
                Assert.Equal("5.000,00 €", line.NetAmount);
                Assert.Equal("950,00 €", line.VatAmount);
            });
        Assert.Equal("6.000,00 €", document.NetTotal);
        Assert.Equal("1.020,00 €", document.VatTotal);
        Assert.Equal("7.020,00 €", document.GrossTotal);
    }

    [Fact]
    public void AnAbsentServicePeriodStaysAbsent()
    {
        var document = InvoiceDocumentFactory.Create(MixedRateInvoice(), CustomerWith(Address), Calendar);

        Assert.Null(document.ServicePeriodStart);
        Assert.Null(document.ServicePeriodEnd);
    }

    /// <summary>
    /// BR-5 requires the customer's address, and <c>Customer.Address</c> is optional because the
    /// website contact form does not collect one. Printing around the gap would publish a deficient
    /// invoice, so the document is refused, naming what is missing.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ACustomerWithoutAnAddressIsRefused(string? address)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => InvoiceDocumentFactory.Create(MixedRateInvoice(), CustomerWith(address), Calendar));

        Assert.Contains("the customer's address", ex.Message);
        Assert.Contains("RE-2026-00017", ex.Message);
    }

    /// <summary>The refusal names the missing fields and never discloses a value.</summary>
    [Fact]
    public void TheRefusalNamesNoCustomerData()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => InvoiceDocumentFactory.Create(MixedRateInvoice(), CustomerWith(null), Calendar));

        Assert.DoesNotContain("Erika", ex.Message);
        Assert.DoesNotContain("example.test", ex.Message);
        Assert.DoesNotContain("1234567", ex.Message);
    }

    /// <summary>
    /// A zero-gross invoice has nothing to split and so no lines; that is its correct state, not a
    /// missing one, and is not refused here (sending it is refused by the aggregate).
    /// </summary>
    [Fact]
    public void AZeroGrossInvoiceIsNotRefusedForHavingNoLines()
    {
        var document = InvoiceDocumentFactory.Create(MixedRateInvoice(gross: 0m), CustomerWith(Address), Calendar);

        Assert.Empty(document.VatBreakdown);
        Assert.Equal("0,00 €", document.GrossTotal);
    }
    /// <summary>
    /// D111 Part 6: the printed invoice date is the issue instant's date in Berlin. 22:30 UTC on
    /// 1 October is 00:30 on 2 October there; printing the UTC date would put it on the 1st.
    /// </summary>
    [Fact]
    public void TheInvoiceDateIsTheIssueInstantsDateInBerlin()
    {
        var invoice = MixedRateInvoice(issuedAt: new DateTime(2026, 10, 1, 22, 30, 0, DateTimeKind.Utc));

        var document = InvoiceDocumentFactory.Create(invoice, CustomerWith(Address), Calendar);

        Assert.Equal(new DateOnly(2026, 10, 2), document.IssuedOn);
    }

    /// <summary>
    /// The New Year case the number year depends on: an invoice issued at 00:30 on 1 January in Berlin
    /// is dated 1 January, matching the number the handler reserved in the new year.
    /// </summary>
    [Fact]
    public void AnInvoiceIssuedAfterBerlinMidnightOnNewYearsEveIsDatedTheFirstOfJanuary()
    {
        var invoice = MixedRateInvoice(issuedAt: new DateTime(2026, 12, 31, 23, 30, 0, DateTimeKind.Utc));

        var document = InvoiceDocumentFactory.Create(invoice, CustomerWith(Address), Calendar);

        Assert.Equal(new DateOnly(2027, 1, 1), document.IssuedOn);
    }
}
