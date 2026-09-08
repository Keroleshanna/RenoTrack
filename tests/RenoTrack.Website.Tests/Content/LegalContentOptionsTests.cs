using RenoTrack.Website.Content;

namespace RenoTrack.Website.Tests.Content;

/// <summary>
/// What counts as configured legal content, and which malformed content refuses to start.
/// </summary>
/// <remarks>
/// <c>HasContent</c> is not a convenience property: it is what decides 200 versus 404 on a legally
/// required page (<b>D100</b> Part 1), so its edge cases are tested rather than assumed.
/// </remarks>
public sealed class LegalContentOptionsTests
{
    private static LegalParagraphOptions Paragraph(string? text = null, string? linkText = null, string? linkUrl = null) =>
        new() { Text = text, LinkText = linkText, LinkUrl = linkUrl };

    private static LegalDocumentOptions DocumentOf(params LegalParagraphOptions[] paragraphs) =>
        new() { Sections = [new LegalSectionOptions { Heading = "Überschrift", Paragraphs = paragraphs }] };

    // ---- HasContent: the 200-versus-404 decision ---------------------------

    [Fact]
    public void An_unconfigured_document_has_no_content()
    {
        Assert.False(new LegalContentOptions().Impressum.HasContent);
        Assert.False(new LegalContentOptions().Datenschutz.HasContent);
    }

    /// <summary>
    /// The exact shape D100 refuses to serve: structure present, nothing to read. A heading alone
    /// would render a legal page that says only "Angaben gemäß § 5 DDG" and nothing else.
    /// </summary>
    [Fact]
    public void A_section_with_a_heading_but_no_paragraphs_is_not_content()
    {
        var document = new LegalDocumentOptions
        {
            Sections = [new LegalSectionOptions { Heading = "Angaben", Paragraphs = [] }],
        };

        Assert.False(document.HasContent);
        Assert.Empty(document.ContentSections);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n ")]
    public void Whitespace_is_not_content(string? text)
    {
        Assert.False(DocumentOf(Paragraph(text)).HasContent);
    }

    [Fact]
    public void Real_text_is_content()
    {
        Assert.True(DocumentOf(Paragraph("Ein Satz.")).HasContent);
    }

    /// <summary>A paragraph that is only a link still says something, so it counts.</summary>
    [Fact]
    public void A_link_alone_is_content()
    {
        var document = DocumentOf(Paragraph(linkText: "Aufsichtsbehörde", linkUrl: "https://example.test"));

        Assert.True(document.HasContent);
    }

    /// <summary>
    /// An empty entry between two real ones must not render a blank paragraph in the middle of a
    /// legal document.
    /// </summary>
    [Fact]
    public void Empty_entries_are_dropped_rather_than_rendered_blank()
    {
        var document = DocumentOf(Paragraph("Erster Satz."), Paragraph("  "), Paragraph("Zweiter Satz."));

        var paragraphs = Assert.Single(document.ContentSections).ContentParagraphs.ToList();

        Assert.Equal(2, paragraphs.Count);
        Assert.All(paragraphs, paragraph => Assert.True(paragraph.HasContent));
    }

    // ---- Validation: absent is fine, broken is not -------------------------

    /// <summary>
    /// The state this repository is actually in. Startup must not fail on it, or nobody could run
    /// the site until the company's lawyer has finished writing (<b>D100</b> Part 1).
    /// </summary>
    [Fact]
    public void Unconfigured_content_is_valid()
    {
        new LegalContentOptions().Validate();
    }

    [Fact]
    public void A_link_url_without_a_label_fails_startup_naming_the_key()
    {
        var options = new LegalContentOptions { Impressum = DocumentOf(Paragraph(linkUrl: "https://example.test")) };

        var error = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Legal:Impressum:Sections:0:Paragraphs:0", error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(LegalParagraphOptions.LinkText), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_label_without_a_url_fails_startup_naming_the_key()
    {
        var options = new LegalContentOptions { Datenschutz = DocumentOf(Paragraph(linkText: "Mehr")) };

        var error = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Legal:Datenschutz:Sections:0:Paragraphs:0", error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(LegalParagraphOptions.LinkUrl), error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The one that matters.</b> Razor encodes an attribute's value but does not refuse its
    /// scheme, so without this allowlist a configured <c>javascript:</c> href would execute on a
    /// page whose entire design excludes script.
    /// </summary>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///etc/passwd")]
    public void A_dangerous_link_scheme_fails_startup(string url)
    {
        var options = new LegalContentOptions { Impressum = DocumentOf(Paragraph(linkText: "Klick", linkUrl: url)) };

        var error = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("scheme", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://ec.europa.eu/consumers/odr/")]
    [InlineData("http://example.test")]
    [InlineData("mailto:kontakt@example.test")]
    [InlineData("tel:+490000000000")]
    [InlineData("/angebot")]
    public void An_allowed_link_scheme_starts(string url)
    {
        var options = new LegalContentOptions { Impressum = DocumentOf(Paragraph(linkText: "Klick", linkUrl: url)) };

        options.Validate();
    }

    [Fact]
    public void A_link_that_is_neither_absolute_nor_site_relative_fails_startup()
    {
        var options = new LegalContentOptions { Impressum = DocumentOf(Paragraph(linkText: "Klick", linkUrl: "example.test/impressum")) };

        var error = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Legal:Impressum", error.Message, StringComparison.Ordinal);
    }

    /// <summary>The key names the offending entry, not just the document.</summary>
    [Fact]
    public void Validation_reports_the_index_of_the_offending_paragraph()
    {
        var options = new LegalContentOptions
        {
            Impressum = new LegalDocumentOptions
            {
                Sections =
                [
                    new LegalSectionOptions { Paragraphs = [Paragraph("Erster Satz.")] },
                    new LegalSectionOptions
                    {
                        Paragraphs = [Paragraph("Zweiter Satz."), Paragraph(linkUrl: "https://example.test")],
                    },
                ],
            },
        };

        var error = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Legal:Impressum:Sections:1:Paragraphs:1", error.Message, StringComparison.Ordinal);
    }
}
