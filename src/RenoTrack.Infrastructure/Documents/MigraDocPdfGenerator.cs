using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using RenoTrack.Application.Common.Documents;
using RenoTrack.Application.Common.Interfaces;

namespace RenoTrack.Infrastructure.Documents;

/// <summary>
/// Renders the company's documents with MigraDoc/PDFsharp (Phase 14, <b>D110</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A document model, not HTML.</b> <c>Architecture.md</c> §4 anticipated an HTML→PDF step; the
/// library actually chosen composes the document directly. The reasoning is recorded in D110 — in
/// short, every HTML→PDF route either ships a browser into the deployment or carries a licence with
/// a revenue trigger, and this product is sold to companies.
/// </para>
/// <para>
/// <b>It prints what it is given and computes nothing.</b> Every figure arrives formatted
/// (<see cref="AngebotDocument"/>), because BR-11's rounding is the Domain's and a renderer that
/// re-derives a total is a second opinion about what the company charges.
/// </para>
/// <para>
/// <b>The company's legal identity is read here</b>, from configuration, and an incomplete identity
/// is refused rather than printed around (D110).
/// </para>
/// </remarks>
public sealed class MigraDocPdfGenerator(CompanyLegalIdentityOptions companyIdentity) : IPdfGenerator
{
    private const double TitleFontSize = 16;

    public byte[] RenderAngebot(AngebotDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        EmbeddedFontResolver.EnsureInstalled();

        var pdf = BuildAngebot(document);
        return Render(pdf);
    }

    private Document BuildAngebot(AngebotDocument document)
    {
        var pdf = NewDocument($"Angebot {document.AngebotNumber}");
        var section = pdf.LastSection;

        AddCompanyHeader(section);
        AddRecipient(section, document.Customer);

        var title = section.AddParagraph($"Angebot {document.AngebotNumber}");
        title.Format.Font.Bold = true;
        title.Format.Font.Size = Unit.FromPoint(TitleFontSize);
        title.Format.SpaceBefore = Unit.FromCentimeter(1);

        var date = section.AddParagraph(
            $"Datum: {document.IssuedOn.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}");
        date.Format.SpaceAfter = Unit.FromCentimeter(0.6);

        foreach (var documentSection in document.Sections)
        {
            AddSection(section, documentSection);
        }

        AddTotals(section, document);

        return pdf;
    }

    /// <summary>
    /// The company block every document carries. Refuses rather than printing a document missing
    /// what identifies its issuer.
    /// </summary>
    private void AddCompanyHeader(Section section)
    {
        if (!companyIdentity.IsComplete)
        {
            throw new InvalidOperationException(
                "A document cannot be generated without the company's own legal identity. Missing configuration: "
                + string.Join("', '", companyIdentity.MissingKeys().Select(key => $"'{key}'"))
                + ". These are the issuing company's own facts and are never invented; supply them per deployment "
                + "(see DEPLOYMENT_CONFIGURATION.md).");
        }

        var name = section.AddParagraph(companyIdentity.LegalName!);
        name.Format.Font.Bold = true;

        section.AddParagraph(companyIdentity.StreetAddress!);
        section.AddParagraph($"{companyIdentity.PostalCode} {companyIdentity.Locality}");

        if (!string.IsNullOrWhiteSpace(companyIdentity.TaxNumber))
        {
            section.AddParagraph($"Steuernummer: {companyIdentity.TaxNumber}");
        }

        if (!string.IsNullOrWhiteSpace(companyIdentity.VatId))
        {
            section.AddParagraph($"USt-IdNr.: {companyIdentity.VatId}");
        }
    }

    private static void AddRecipient(Section section, DocumentRecipient recipient)
    {
        var block = section.AddParagraph(recipient.Name);
        block.Format.SpaceBefore = Unit.FromCentimeter(1);
        block.Format.Font.Bold = true;

        if (!string.IsNullOrWhiteSpace(recipient.Address))
        {
            // The address is one stored field; its own line breaks are the company's.
            foreach (var line in recipient.Address.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                section.AddParagraph(line.Trim());
            }
        }
    }

    private static void AddSection(Section section, DocumentSection documentSection)
    {
        var heading = section.AddParagraph(documentSection.Title);
        heading.Format.Font.Bold = true;
        heading.Format.SpaceBefore = Unit.FromCentimeter(0.5);
        heading.Format.SpaceAfter = Unit.FromCentimeter(0.2);

        var table = NewTable(section);

        var header = table.AddRow();
        header.Format.Font.Bold = true;
        Fill(header, "Pos.", "Leistung", "Menge", "Einzelpreis", "MwSt.", "Gesamt");

        foreach (var line in documentSection.Lines)
        {
            var row = table.AddRow();
            Fill(row, line.Position, line.Description, line.Quantity, line.UnitPrice, line.VatRate, line.LineTotal);

            if (!string.IsNullOrWhiteSpace(line.Specification))
            {
                // A second row under the description, so a long specification never squeezes the
                // figures' columns.
                var detail = table.AddRow();
                detail.Cells[1].AddParagraph(line.Specification).Format.Font.Size = Unit.FromPoint(8);
                detail.Cells[1].MergeRight = 4;
            }
        }

        var subtotal = table.AddRow();
        subtotal.Format.Font.Bold = true;
        subtotal.Cells[0].AddParagraph("Zwischensumme");
        subtotal.Cells[0].MergeRight = 4;
        subtotal.Cells[5].AddParagraph(documentSection.Subtotal);
        subtotal.Cells[5].Format.Alignment = ParagraphAlignment.Right;
    }

    private static void AddTotals(Section section, AngebotDocument document)
    {
        var net = section.AddParagraph($"Nettosumme: {document.NetTotal}");
        net.Format.SpaceBefore = Unit.FromCentimeter(0.6);

        // One line per rate present — BR-6 allows several rates in one document, and §14 UStG wants
        // each of them shown with its own amount.
        foreach (var vatLine in document.VatBreakdown)
        {
            section.AddParagraph($"MwSt. {vatLine.VatRate} auf {vatLine.NetAmount}: {vatLine.VatAmount}");
        }

        var gross = section.AddParagraph($"Gesamtsumme: {document.GrossTotal}");
        gross.Format.Font.Bold = true;
    }

    private static Document NewDocument(string title)
    {
        var pdf = new Document { Info = { Title = title } };
        pdf.Styles["Normal"]!.Font.Name = EmbeddedFontResolver.FontFamily;
        pdf.Styles["Normal"]!.Font.Size = Unit.FromPoint(10);

        var section = pdf.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(2);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(2.5);
        section.PageSetup.RightMargin = Unit.FromCentimeter(2);

        return pdf;
    }

    private static Table NewTable(Section section)
    {
        var table = section.AddTable();
        table.Borders.Width = 0.25;

        table.AddColumn(Unit.FromCentimeter(1.2));  // Pos.
        table.AddColumn(Unit.FromCentimeter(6.5));  // Leistung
        table.AddColumn(Unit.FromCentimeter(2.2));  // Menge
        table.AddColumn(Unit.FromCentimeter(2.4));  // Einzelpreis
        table.AddColumn(Unit.FromCentimeter(1.6));  // MwSt.
        table.AddColumn(Unit.FromCentimeter(2.6));  // Gesamt

        return table;
    }

    private static void Fill(Row row, params string[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            var paragraph = row.Cells[index].AddParagraph(values[index]);

            // Figures right-aligned, text left: a column of prices is read by its last digit.
            if (index >= 2)
            {
                paragraph.Format.Alignment = ParagraphAlignment.Right;
            }
        }
    }

    private static byte[] Render(Document document)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }
}
