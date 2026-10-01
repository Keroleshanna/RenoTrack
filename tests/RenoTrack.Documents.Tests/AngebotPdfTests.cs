using RenoTrack.Application.Common.Documents;
using RenoTrack.Infrastructure.Documents;
using UglyToad.PdfPig;

namespace RenoTrack.Documents.Tests;

/// <summary>
/// The rendered Angebot PDF (Phase 14, <b>D110</b>), asserted on the text a reader actually gets.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the text and not the bytes:</b> a PDF embeds its creation time, so two renders of the same
/// document differ byte for byte — measured, not assumed. Comparing bytes would pin the clock
/// instead of the content. The document is therefore opened and read back with PdfPig, the same way
/// a customer's reader would, which is also what proves the embedded font resolved: unresolved text
/// does not come back as text at all.
/// </para>
/// <para>
/// <b>Why this project exists at all:</b> <c>RenoTrack.Infrastructure.Tests</c> needs SQL Server
/// LocalDB and therefore runs only in CI's Windows job (D40, D56). PDF rendering has no database and
/// its whole risk is cross-platform — PDFsharp resolves no font on its own, on any OS — so these
/// tests belong where Linux runs them. Same reasoning as <c>RenoTrack.MediaPrep.Tests</c> (D106).
/// </para>
/// </remarks>
public sealed class AngebotPdfTests
{
    private static CompanyLegalIdentityOptions CompleteIdentity() => new()
    {
        // Fictional, as every fixture in this repository is: a real tax number is company data and
        // is never committed (D100).
        LegalName = "Musterbetrieb Beispiel GmbH (Testdaten)",
        StreetAddress = "Teststraße 1",
        PostalCode = "00000",
        Locality = "Testort",
        TaxNumber = "00/000/00000",
    };

    private static AngebotDocument SampleDocument() => new(
        AngebotNumber: "ANG-2026-00042",
        IssuedOn: new DateOnly(2026, 10, 1),
        Customer: new DocumentRecipient("Testkundin Beispiel", "Musterweg 7\n00001 Testort"),
        Sections:
        [
            new DocumentSection(
                "Badezimmer",
                [
                    new DocumentLine("1.1", "Fliesenarbeiten Wände", "Feinsteinzeug, 30x60", "10,5 m²", "45,00 €", "19 %", "472,50 €"),
                    new DocumentLine("1.2", "Bodenaufbau", null, "10,5 m²", "20,00 €", "19 %", "210,00 €"),
                ],
                "682,50 €"),
        ],
        VatBreakdown: [new DocumentVatLine("19 %", "682,50 €", "129,68 €")],
        NetTotal: "682,50 €",
        GrossTotal: "812,18 €");

    private static string TextOf(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        using var document = PdfDocument.Open(stream);

        // Word by word: a page's raw text concatenates without spaces, which would make every
        // assertion below depend on glyph positioning rather than on content.
        return string.Join(" ", document.GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text));
    }

    private static byte[] Render(CompanyLegalIdentityOptions? identity = null) =>
        new MigraDocPdfGenerator(identity ?? CompleteIdentity()).RenderAngebot(SampleDocument());

    [Fact]
    public void The_rendered_bytes_are_a_pdf_file()
    {
        var pdf = Render();

        Assert.True(pdf.Length > 0);
        Assert.Equal("%PDF-"u8.ToArray(), pdf.Take(5).ToArray());
    }

    [Fact]
    public void The_document_carries_the_companys_own_legal_identity()
    {
        var text = TextOf(Render());

        Assert.Contains("Musterbetrieb", text, StringComparison.Ordinal);
        Assert.Contains("Teststraße", text, StringComparison.Ordinal);
        Assert.Contains("00000", text, StringComparison.Ordinal);
        Assert.Contains("Testort", text, StringComparison.Ordinal);
        Assert.Contains("00/000/00000", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_document_carries_its_number_date_and_recipient()
    {
        var text = TextOf(Render());

        Assert.Contains("ANG-2026-00042", text, StringComparison.Ordinal);
        Assert.Contains("01.10.2026", text, StringComparison.Ordinal);
        Assert.Contains("Testkundin", text, StringComparison.Ordinal);
        Assert.Contains("Musterweg", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_line_of_every_section_is_printed_with_its_figures()
    {
        var text = TextOf(Render());

        Assert.Contains("Badezimmer", text, StringComparison.Ordinal);
        Assert.Contains("Fliesenarbeiten", text, StringComparison.Ordinal);
        Assert.Contains("Feinsteinzeug,", text, StringComparison.Ordinal);
        Assert.Contains("1.1", text, StringComparison.Ordinal);
        Assert.Contains("10,5", text, StringComparison.Ordinal);
        Assert.Contains("472,50", text, StringComparison.Ordinal);
        Assert.Contains("Zwischensumme", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// BR-6: rates are per line and summarised per rate, and §14 UStG wants each rate's own amount
    /// shown. The figures are the Domain's, printed verbatim — the renderer computes nothing.
    /// </summary>
    [Fact]
    public void The_vat_breakdown_and_totals_are_printed_as_given()
    {
        var text = TextOf(Render());

        Assert.Contains("19", text, StringComparison.Ordinal);
        Assert.Contains("129,68", text, StringComparison.Ordinal);
        Assert.Contains("682,50", text, StringComparison.Ordinal);
        Assert.Contains("812,18", text, StringComparison.Ordinal);
        Assert.Contains("Gesamtsumme", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// German text is the whole point of embedding a font: umlauts, ß and € must survive into the
    /// file rather than being dropped or replaced by a fallback glyph.
    /// </summary>
    [Fact]
    public void German_characters_and_the_euro_sign_survive_into_the_document()
    {
        var text = TextOf(Render());

        Assert.Contains("Wände", text, StringComparison.Ordinal);
        Assert.Contains("Teststraße", text, StringComparison.Ordinal);
        Assert.Contains("m²", text, StringComparison.Ordinal);
        Assert.Contains("€", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A document missing what identifies its issuer is refused, naming every missing key. Printing
    /// around the gap would publish a legally deficient invoice; inventing a value would fabricate a
    /// tax identity (D100, D110).
    /// </summary>
    [Fact]
    public void An_incomplete_company_identity_refuses_to_render_and_names_every_missing_key()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            Render(new CompanyLegalIdentityOptions { LegalName = "Nur ein Name (Testdaten)" }));

        Assert.Contains("CompanyLegalIdentity:StreetAddress", error.Message, StringComparison.Ordinal);
        Assert.Contains("CompanyLegalIdentity:PostalCode", error.Message, StringComparison.Ordinal);
        Assert.Contains("CompanyLegalIdentity:Locality", error.Message, StringComparison.Ordinal);
        Assert.Contains("CompanyLegalIdentity:TaxNumber", error.Message, StringComparison.Ordinal);
        Assert.Contains("CompanyLegalIdentity:VatId", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("LegalName", error.Message, StringComparison.Ordinal);
    }

    /// <summary>§14 UStG accepts a tax number or a VAT identification number; either alone suffices.</summary>
    [Fact]
    public void A_vat_identification_number_alone_satisfies_the_identity()
    {
        var identity = new CompanyLegalIdentityOptions
        {
            LegalName = "Musterbetrieb Beispiel GmbH (Testdaten)",
            StreetAddress = "Teststraße 1",
            PostalCode = "00000",
            Locality = "Testort",
            VatId = "DE000000000",
        };

        var text = TextOf(new MigraDocPdfGenerator(identity).RenderAngebot(SampleDocument()));

        Assert.Contains("DE000000000", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Steuernummer", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// An optional detail line is printed when present and leaves no empty row when absent — the
    /// second line of the sample has none.
    /// </summary>
    [Fact]
    public void A_line_without_a_specification_prints_no_empty_detail_row()
    {
        var text = TextOf(Render());

        Assert.Contains("Bodenaufbau", text, StringComparison.Ordinal);
        Assert.Single(text.Split("Feinsteinzeug", StringSplitOptions.None).Skip(1));
    }
}
