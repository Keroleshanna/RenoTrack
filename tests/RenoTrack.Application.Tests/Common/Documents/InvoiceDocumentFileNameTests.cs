using RenoTrack.Application.Common.Documents;

namespace RenoTrack.Application.Tests.Common.Documents;

/// <summary>
/// The one name an issued invoice's PDF carries, in the Admin's download and the email attachment
/// alike (D113). It reaches a <c>Content-Disposition</c> header and a MIME part, so it is
/// allowlist-shaped even though the invoice number is server-generated.
/// </summary>
public class InvoiceDocumentFileNameTests
{
    [Fact]
    public void ARealInvoiceNumberIsUsedAsItIs()
    {
        Assert.Equal("Rechnung-RE-2026-00017.pdf", InvoiceDocumentFileName.For("RE-2026-00017"));
    }

    [Theory]
    [InlineData("RE\"2026", "Rechnung-RE-2026.pdf")]
    [InlineData("RE;2026", "Rechnung-RE-2026.pdf")]
    [InlineData("RE\r\n2026", "Rechnung-RE--2026.pdf")]
    [InlineData("../RE/2026", "Rechnung----RE-2026.pdf")]
    [InlineData("RÉ 2026", "Rechnung-R--2026.pdf")]
    public void EveryCharacterOutsideTheAllowlistIsReplaced(string invoiceNumber, string expected)
    {
        Assert.Equal(expected, InvoiceDocumentFileName.For(invoiceNumber));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyNumberStillYieldsAUsableName(string invoiceNumber)
    {
        Assert.Equal("Rechnung.pdf", InvoiceDocumentFileName.For(invoiceNumber));
    }

    [Fact]
    public void TheContentTypeIsPdf()
    {
        Assert.Equal("application/pdf", InvoiceDocumentFileName.PdfContentType);
    }
}
