namespace RenoTrack.Website.Content;

/// <summary>
/// The Impressum and the Datenschutzerklärung a German public site must offer (SRS FR-1.4), bound
/// from the <c>Legal</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// <b>Structure, never content (Phase 11 Q7, <b>D100</b>).</b> No legal text, company name, address
/// or contact detail is committed here or anywhere else in this repository. The real wording is
/// supplied per deployment, exactly as <c>Email:FromAddress</c>, <c>Email:AdminRecipients</c> and
/// <c>TokenLink:PublicBaseUrl</c> already are (SRS OQ-3b), and the company is the author (SRS §5).
/// </para>
/// <para>
/// <b>Absence means the route does not exist</b> — <c>/impressum</c> and <c>/datenschutz</c> answer
/// 404 and the customer layout renders no link to them. It deliberately does not mean an empty
/// page: <c>Pages/Privacy.cshtml</c> is the standing demonstration of why, having served an English
/// "Use this page to detail your site's privacy policy" placeholder on the customer-facing origin
/// since the project was scaffolded. A page that says nothing while looking like a legal page is
/// worse than a 404, and it is the variant that screenshots as correct.
/// </para>
/// <para>
/// <b>A constrained structure rather than stored markup.</b> Legal text needs headings, paragraphs
/// and links, which is the usual argument for rendering a stored HTML blob raw. <c>CLAUDE.md</c>
/// §24 forbids <c>Html.Raw</c> on a customer page and that rule is kept: every value below is a
/// plain string rendered through Razor's encoding <c>@@</c>-expressions. A deployment-supplied file
/// is trusted in *provenance* and not in *risk* — an operator pasting from a lawyer's document is
/// still a paste, and the first exception is what makes the second arguable. The accepted cost is
/// that the copy cannot express arbitrary markup.
/// </para>
/// <para>
/// <b>The text lives in configuration, so a deployment may keep it in its own file</b> — an
/// additional JSON source, an environment variable per key, or a mounted secret — without this code
/// growing a file path, a reader, or a set of I/O failures to handle. That is a deployment choice,
/// not a code change.
/// </para>
/// </remarks>
public sealed class LegalContentOptions
{
    public const string SectionName = "Legal";

    /// <summary>§5 DDG's Impressum. Empty until a deployment supplies it.</summary>
    public LegalDocumentOptions Impressum { get; init; } = new();

    /// <summary>The Datenschutzerklärung. Empty until a deployment supplies it.</summary>
    public LegalDocumentOptions Datenschutz { get; init; } = new();

    /// <summary>
    /// Fails startup naming the offending key when supplied content is malformed.
    /// </summary>
    /// <remarks>
    /// <b>Absent content is valid; broken content is not.</b> The two are different failures and get
    /// different answers: nothing configured is the expected state until the company writes its
    /// text, while a half-written link is a mistake that would otherwise reach a customer as a dead
    /// or dangerous anchor. This is the same stance <c>TrustedForwardersOptions</c> takes — a
    /// malformed entry fails startup rather than being silently skipped, because a trust list that
    /// quietly shrinks looks configured and is not.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A link is half-specified or uses a refused scheme.</exception>
    public void Validate()
    {
        Impressum.Validate($"{SectionName}:{nameof(Impressum)}");
        Datenschutz.Validate($"{SectionName}:{nameof(Datenschutz)}");
    }
}

/// <summary>One legal document: an ordered list of sections.</summary>
public sealed class LegalDocumentOptions
{
    public IReadOnlyList<LegalSectionOptions> Sections { get; init; } = [];

    /// <summary>
    /// Whether this document has anything to say. <b>This is what decides 200 versus 404</b>, so it
    /// asks for real text rather than for the section list merely existing: a document of empty
    /// sections is exactly the empty legal page D100 refuses to serve.
    /// </summary>
    public bool HasContent => Sections.Any(section => section.HasContent);

    /// <summary>The sections worth rendering, in configured order.</summary>
    public IEnumerable<LegalSectionOptions> ContentSections => Sections.Where(section => section.HasContent);

    internal void Validate(string path)
    {
        for (var index = 0; index < Sections.Count; index++)
        {
            Sections[index].Validate($"{path}:{nameof(Sections)}:{index}");
        }
    }
}

/// <summary>A headed group of paragraphs. The heading is optional; the paragraphs carry the text.</summary>
public sealed class LegalSectionOptions
{
    public string? Heading { get; init; }

    public IReadOnlyList<LegalParagraphOptions> Paragraphs { get; init; } = [];

    public bool HasContent => Paragraphs.Any(paragraph => paragraph.HasContent);

    public IEnumerable<LegalParagraphOptions> ContentParagraphs =>
        Paragraphs.Where(paragraph => paragraph.HasContent);

    public bool HasHeading => !string.IsNullOrWhiteSpace(Heading);

    internal void Validate(string path)
    {
        for (var index = 0; index < Paragraphs.Count; index++)
        {
            Paragraphs[index].Validate($"{path}:{nameof(Paragraphs)}:{index}");
        }
    }
}

/// <summary>
/// One paragraph: text, an optional trailing link, or both.
/// </summary>
/// <remarks>
/// A link is genuinely needed — a real Impressum commonly points at the EU online dispute-resolution
/// platform, and a Datenschutzerklärung at a supervisory authority — so the model carries one rather
/// than forcing an operator to paste a bare URL as prose.
/// </remarks>
public sealed class LegalParagraphOptions
{
    public string? Text { get; init; }

    /// <summary>The link's visible label. Required whenever <see cref="LinkUrl"/> is set.</summary>
    public string? LinkText { get; init; }

    /// <summary>The link's target. Required whenever <see cref="LinkText"/> is set.</summary>
    public string? LinkUrl { get; init; }

    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    public bool HasLink => !string.IsNullOrWhiteSpace(LinkText) && !string.IsNullOrWhiteSpace(LinkUrl);

    public bool HasContent => HasText || HasLink;

    /// <summary>
    /// The URL schemes a configured link may use.
    /// </summary>
    /// <remarks>
    /// <b>Razor encodes an attribute's value; it does not judge its scheme.</b> A
    /// <c>javascript:</c> href would therefore survive encoding intact and run, which would put
    /// script execution back on a page whose whole design excludes it. An allowlist is the control:
    /// <c>http</c>/<c>https</c> for the outside world, <c>mailto</c>/<c>tel</c> because an Impressum
    /// legally carries a contact address and a phone number and both read better as links.
    /// </remarks>
    private static readonly string[] AllowedSchemes = ["http", "https", "mailto", "tel"];

    internal void Validate(string path)
    {
        var hasUrl = !string.IsNullOrWhiteSpace(LinkUrl);
        var hasLabel = !string.IsNullOrWhiteSpace(LinkText);

        // Half a link is a mistake with two bad renderings — an invisible anchor, or a label that
        // goes nowhere — so it is refused rather than guessed at.
        if (hasUrl != hasLabel)
        {
            var missing = hasUrl ? nameof(LinkText) : nameof(LinkUrl);
            var supplied = hasUrl ? nameof(LinkUrl) : nameof(LinkText);
            throw new InvalidOperationException(
                $"Configuration '{path}' sets '{supplied}' without '{missing}'. A legal link needs " +
                "both its target and its visible label; supply the missing key or remove both.");
        }

        if (!hasUrl)
        {
            return;
        }

        // A site-relative link is allowed and needs no scheme check — it cannot leave this origin.
        if (LinkUrl!.StartsWith('/'))
        {
            return;
        }

        if (!Uri.TryCreate(LinkUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"Configuration '{path}:{nameof(LinkUrl)}' has value '{LinkUrl}', which is neither an " +
                "absolute URL nor a site-relative path beginning with '/'.");
        }

        if (!AllowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Configuration '{path}:{nameof(LinkUrl)}' uses scheme '{uri.Scheme}', which is not " +
                $"allowed on a customer page. Permitted schemes are: {string.Join(", ", AllowedSchemes)}. " +
                "Razor encodes an attribute's value but does not refuse its scheme, so a 'javascript:' " +
                "link would execute on a page that deliberately runs no script at all.");
        }
    }
}
