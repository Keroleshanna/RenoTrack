using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Documents;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Documents;
using UglyToad.PdfPig;

namespace RenoTrack.Documents.Tests;

/// <summary>
/// The rendered Invoice PDF (Phase 14 Slice 2, <b>D111</b>), asserted on the text a reader actually
/// gets — the same approach, for the same reasons, as <see cref="AngebotPdfTests"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is proved is presence, not legal sufficiency.</b> These tests show that every field
/// BR-5 enumerates reaches the page. Whether the document satisfies §14 UStG is a legal reviewer's
/// judgement (SRS Q20) — in particular, an Invoice carries one description and no quantity, and
/// nothing here claims the description stands in for one.
/// </para>
/// </remarks>
public sealed class InvoicePdfTests
{
    private static CompanyLegalIdentityOptions CompleteIdentity() => new()
    {
        // Fictional, as every fixture in this repository is (D100).
        LegalName = "Musterbetrieb Beispiel GmbH (Testdaten)",
        StreetAddress = "Teststraße 1",
        PostalCode = "00000",
        Locality = "Testort",
        TaxNumber = "00/000/00000",
    };

    private static InvoiceDocument SampleDocument(DateOnly? servicePeriodStart = null, DateOnly? servicePeriodEnd = null) => new(
        InvoiceNumber: "RE-2026-00017",
        IssuedOn: new DateOnly(2026, 10, 1),
        DueOn: new DateOnly(2026, 10, 15),
        ServicePeriodStart: servicePeriodStart,
        ServicePeriodEnd: servicePeriodEnd,
        Customer: new DocumentRecipient("Testkundin Beispiel", "Musterweg 7\n00001 Testort"),
        Description: "Abschlag 1: Malerarbeiten Wände Erdgeschoss",
        VatBreakdown:
        [
            new DocumentVatLine("7 %", "1.000,00 €", "70,00 €"),
            new DocumentVatLine("19 %", "5.000,00 €", "950,00 €"),
        ],
        NetTotal: "6.000,00 €",
        VatTotal: "1.020,00 €",
        GrossTotal: "7.020,00 €");

    private static string TextOf(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        using var document = PdfDocument.Open(stream);

        return string.Join(" ", document.GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text));
    }

    private static string Render(InvoiceDocument? document = null, CompanyLegalIdentityOptions? identity = null) =>
        TextOf(new MigraDocPdfGenerator(identity ?? CompleteIdentity()).RenderInvoice(document ?? SampleDocument()));

    [Fact]
    public void The_rendered_bytes_are_a_pdf_file()
    {
        var pdf = new MigraDocPdfGenerator(CompleteIdentity()).RenderInvoice(SampleDocument());

        Assert.True(pdf.Length > 0);
        Assert.Equal("%PDF-"u8.ToArray(), pdf.Take(5).ToArray());
    }

    /// <summary>BR-5: the issuing company's name, address and tax number.</summary>
    [Fact]
    public void The_invoice_carries_the_companys_own_legal_identity()
    {
        var text = Render();

        Assert.Contains("Musterbetrieb", text, StringComparison.Ordinal);
        Assert.Contains("Teststraße", text, StringComparison.Ordinal);
        Assert.Contains("00000", text, StringComparison.Ordinal);
        Assert.Contains("Testort", text, StringComparison.Ordinal);
        Assert.Contains("00/000/00000", text, StringComparison.Ordinal);
    }

    /// <summary>BR-5: the customer's name and address, the invoice date and the unique number.</summary>
    [Fact]
    public void The_invoice_carries_its_number_date_and_recipient()
    {
        var text = Render();

        Assert.Contains("Rechnung", text, StringComparison.Ordinal);
        Assert.Contains("RE-2026-00017", text, StringComparison.Ordinal);
        Assert.Contains("Rechnungsdatum:", text, StringComparison.Ordinal);
        Assert.Contains("01.10.2026", text, StringComparison.Ordinal);
        Assert.Contains("Testkundin", text, StringComparison.Ordinal);
        Assert.Contains("Musterweg", text, StringComparison.Ordinal);
        Assert.Contains("00001", text, StringComparison.Ordinal);
    }

    /// <summary>BR-5: what is billed — the Admin's description, verbatim.</summary>
    [Fact]
    public void The_description_is_printed_verbatim()
    {
        var text = Render();

        Assert.Contains("Abschlag", text, StringComparison.Ordinal);
        Assert.Contains("Malerarbeiten", text, StringComparison.Ordinal);
        Assert.Contains("Erdgeschoss", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// BR-5 and BR-6: each applicable rate with its own net and VAT amount, then the net, VAT and
    /// gross totals — every figure printed as given, the renderer computing nothing.
    /// </summary>
    [Fact]
    public void Every_vat_rate_and_every_total_is_printed_as_given()
    {
        var text = Render();

        Assert.Contains("7", text, StringComparison.Ordinal);
        Assert.Contains("70,00", text, StringComparison.Ordinal);
        Assert.Contains("1.000,00", text, StringComparison.Ordinal);
        Assert.Contains("19", text, StringComparison.Ordinal);
        Assert.Contains("950,00", text, StringComparison.Ordinal);
        Assert.Contains("5.000,00", text, StringComparison.Ordinal);
        Assert.Contains("6.000,00", text, StringComparison.Ordinal);
        Assert.Contains("1.020,00", text, StringComparison.Ordinal);
        Assert.Contains("7.020,00", text, StringComparison.Ordinal);
        Assert.Contains("Rechnungsbetrag:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_due_date_is_printed()
    {
        var text = Render();

        Assert.Contains("Zahlbar", text, StringComparison.Ordinal);
        Assert.Contains("15.10.2026", text, StringComparison.Ordinal);
    }

    /// <summary>A period is printed as a period, when one was given.</summary>
    [Fact]
    public void A_service_period_is_printed_as_a_period()
    {
        var text = Render(SampleDocument(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));

        Assert.Contains("Leistungszeitraum:", text, StringComparison.Ordinal);
        Assert.Contains("01.09.2026", text, StringComparison.Ordinal);
        Assert.Contains("30.09.2026", text, StringComparison.Ordinal);
    }

    /// <summary>A start alone — or a period of one day — is a service date.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_single_day_is_printed_as_a_service_date(bool endEqualsStart)
    {
        var day = new DateOnly(2026, 9, 15);

        var text = Render(SampleDocument(day, endEqualsStart ? day : null));

        Assert.Contains("Leistungsdatum:", text, StringComparison.Ordinal);
        Assert.Contains("15.09.2026", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Leistungszeitraum", text, StringComparison.Ordinal);
    }

    /// <summary>No service date is assumed when none was given — nothing is printed for it at all.</summary>
    [Fact]
    public void An_absent_service_period_prints_nothing()
    {
        var text = Render();

        Assert.DoesNotContain("Leistungszeitraum", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Leistungsdatum", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// D111: no quantity column (an Invoice has one description) and no bank details (D110) — the
    /// document prints nothing the company did not supply.
    /// </summary>
    [Fact]
    public void No_quantity_column_and_no_bank_details_are_printed()
    {
        var text = Render();

        Assert.DoesNotContain("Menge", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IBAN", text, StringComparison.Ordinal);
        Assert.DoesNotContain("BIC", text, StringComparison.Ordinal);
    }

    [Fact]
    public void German_characters_and_the_euro_sign_survive_into_the_document()
    {
        var text = Render();

        Assert.Contains("Wände", text, StringComparison.Ordinal);
        Assert.Contains("Teststraße", text, StringComparison.Ordinal);
        Assert.Contains("€", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_incomplete_company_identity_refuses_to_render_an_invoice()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new MigraDocPdfGenerator(new CompanyLegalIdentityOptions { LegalName = "Nur ein Name (Testdaten)" })
                .RenderInvoice(SampleDocument()));

        Assert.Contains("CompanyLegalIdentity:StreetAddress", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// End to end through the real pieces: an Invoice the aggregate split across a mixed-rate
    /// Angebot, assembled by <see cref="InvoiceDocumentFactory"/>, rendered and read back — so the
    /// figures on paper are provably the aggregate's own.
    /// </summary>
    [Fact]
    public void An_invoice_assembled_from_the_aggregate_prints_the_aggregates_figures()
    {
        var invoice = Invoice.Create(
            projectId: 7,
            invoiceNumber: "RE-2026-00018",
            issuedAt: new DateTime(2026, 12, 31, 23, 30, 0, DateTimeKind.Utc),
            dueDate: new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc),
            grossAmount: Money.FromExact(7_020.00m),
            rateMix:
            [
                new VatBreakdownLine(VatRate.Reduced, Money.FromExact(1_000.00m), Money.FromExact(70.00m)),
                new VatBreakdownLine(VatRate.Standard, Money.FromExact(5_000.00m), Money.FromExact(950.00m)),
            ],
            description: "Schlussrechnung Badsanierung",
            servicePeriodStart: new DateOnly(2026, 9, 1),
            servicePeriodEnd: new DateOnly(2026, 9, 30));
        var customer = Customer.Create(1, "Testkundin Beispiel", "kundin@example.test", "+49 000 1234567", "Musterweg 7");

        var text = Render(InvoiceDocumentFactory.Create(invoice, customer, InvoiceCalendar.ForEuropeBerlin()));

        Assert.Contains("RE-2026-00018", text, StringComparison.Ordinal);
        // Issued 23:30 UTC on New Year's Eve: 1 January in Berlin, where the invoice is dated (D111).
        Assert.Contains("Rechnungsdatum: 01.01.2027", text, StringComparison.Ordinal);
        Assert.Contains("Badsanierung", text, StringComparison.Ordinal);
        Assert.Contains("1.000,00", text, StringComparison.Ordinal);
        Assert.Contains("950,00", text, StringComparison.Ordinal);
        Assert.Contains("1.020,00", text, StringComparison.Ordinal);
        Assert.Contains("7.020,00", text, StringComparison.Ordinal);
        Assert.Contains("30.09.2026", text, StringComparison.Ordinal);
    }
}
