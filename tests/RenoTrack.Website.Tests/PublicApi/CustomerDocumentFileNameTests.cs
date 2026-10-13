using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Tests.PublicApi;

/// <summary>
/// The name a customer's downloaded invoice is saved under (Phase 14 Slice 3b, D114). It is
/// re-derived from the API's declared name through the same allowlist the API uses, so nothing in
/// it can break the response header or name a path, and a real invoice keeps the email's name.
/// </summary>
public sealed class CustomerDocumentFileNameTests
{
    [Theory]
    [InlineData("Rechnung-RE-2026-00017.pdf", "Rechnung-RE-2026-00017.pdf")]
    [InlineData("\"Rechnung-RE-2026-00017.pdf\"", "Rechnung-RE-2026-00017.pdf")]
    [InlineData("Rechnung-RE-2026-00017.PDF", "Rechnung-RE-2026-00017.pdf")]
    [InlineData("Rechnung", "Rechnung.pdf")]
    public void A_real_invoice_keeps_the_name_the_email_used(string declared, string expected)
    {
        Assert.Equal(expected, CustomerDocumentFileName.From(declared));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    [InlineData(".pdf")]
    [InlineData("....")]
    [InlineData("äöü")]
    public void Nothing_usable_falls_back_to_a_plain_name(string? declared)
    {
        Assert.Equal(CustomerDocumentFileName.Fallback, CustomerDocumentFileName.From(declared));
    }

    /// <summary>
    /// Separators, quotes, control characters, header-breaking characters and non-ASCII all become
    /// <c>-</c>. The result is always <c>[A-Za-z0-9-]+\.pdf</c>.
    /// </summary>
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows\\win.ini")]
    [InlineData("Rechnung\r\nSet-Cookie: x=1.pdf")]
    [InlineData("Rechnung\"; filename=evil.exe")]
    [InlineData("Rechnung;evil.pdf")]
    [InlineData("Rëchnung ünd Söhne.pdf")]
    [InlineData("Rechnung\u0000.pdf")]
    [InlineData("<script>.pdf")]
    [InlineData("rechnung.exe")]
    public void Every_result_is_letters_digits_and_hyphens_with_a_pdf_extension(string declared)
    {
        var name = CustomerDocumentFileName.From(declared);

        Assert.Matches("^[A-Za-z0-9-]+\\.pdf$", name);
        Assert.DoesNotContain("..", name, StringComparison.Ordinal);
        Assert.False(name.StartsWith('-'));
    }

    [Fact]
    public void An_executable_extension_never_survives()
    {
        Assert.Equal("rechnung-exe.pdf", CustomerDocumentFileName.From("rechnung.exe"));
    }

    [Fact]
    public void An_over_long_name_is_bounded()
    {
        var name = CustomerDocumentFileName.From(new string('A', 5_000) + ".pdf");

        Assert.Equal(CustomerDocumentFileName.MaxStemLength + ".pdf".Length, name.Length);
    }
}
