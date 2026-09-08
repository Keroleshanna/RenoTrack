namespace RenoTrack.Website.Content;

/// <summary>
/// The company's own identity as it appears to a customer, bound from the <c>CompanyIdentity</c>
/// configuration section.
/// </summary>
/// <remarks>
/// <para>
/// <b>No value is committed and none is invented</b> — this is the structure, not the content. The
/// real company name, legal details, contact address and Impressum text are supplied before the
/// completion gate (Phase 11 Q7), exactly as <c>Email</c>'s <c>FromAddress</c>/<c>AdminRecipients</c>
/// and <c>TokenLink:PublicBaseUrl</c> are supplied per deployment rather than compiled in (SRS
/// OQ-3b). Inventing a plausible-looking company name would put fabricated identity on a page a
/// real customer reads.
/// </para>
/// <para>
/// <b>Optional, unlike <c>PublicApi:BaseUrl</c>, and the difference is deliberate.</b> The API
/// origin is wiring: without it nothing works, so absence must fail startup. This is content:
/// without it the page is plainer but entirely functional, and failing startup would block
/// development on copy that has not been written yet. Absence is reported once at startup as a
/// warning naming the key, so it is visible rather than silent — the page then omits the heading
/// rather than showing a placeholder a customer might mistake for a real name.
/// </para>
/// </remarks>
public sealed class CompanyIdentityOptions
{
    public const string SectionName = "CompanyIdentity";

    /// <summary>The name shown to the customer, e.g. in the page heading and the document title.</summary>
    public string? DisplayName { get; init; }

    /// <summary>An address a customer may reply to with a question about their quote.</summary>
    public string? ContactEmail { get; init; }

    /// <summary>A number a customer may call with a question about their quote.</summary>
    public string? ContactPhone { get; init; }

    /// <summary>
    /// The company's logo, as a path under <see cref="LogoPathPrefix"/> — Wireframe A3's
    /// <c>[Company Logo]</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The deployment places the file in a <c>brand</c> directory beside the application</b>
    /// (<see cref="BrandAssetsDirectoryName"/>), which <c>Program.cs</c> mounts at
    /// <see cref="LogoPathPrefix"/>, and names it here as <c>/brand/&lt;file&gt;</c>. <b>Not
    /// <c>wwwroot</c></b>: <c>MapStaticAssets</c> serves only the endpoints in its build-time
    /// manifest, so a file copied into <c>wwwroot</c> after publishing is on disk and still answers
    /// 404 — see <see cref="LogoPathPrefix"/> for how that was found.
    /// </para>
    /// <para>
    /// <b>Same-origin only, enforced rather than documented.</b> An absolute URL is refused even
    /// when it is HTTPS: this site loads nothing off-origin and must not start (<c>CLAUDE.md</c>
    /// §24), because a third-party request from a customer's browser tells that party which
    /// customer opened which quote and when. A site-relative path outside the prefix is refused
    /// too, since nothing would serve it.
    /// </para>
    /// <para>
    /// <b>No image is committed to this repository</b>, and none is invented — a logo is company
    /// identity exactly as the name and address are (Phase 11 Q7, <b>D100</b>).
    /// </para>
    /// </remarks>
    public string? LogoPath { get; init; }

    /// <summary>
    /// The one URL prefix a configured logo may use.
    /// </summary>
    /// <remarks>
    /// <b>Not <c>wwwroot</c>, and that is the whole point.</b> <c>MapStaticAssets</c> serves only the
    /// endpoints in its <em>build-time</em> manifest, so a file an operator drops into <c>wwwroot</c>
    /// after publishing is on disk and still answers 404. A deployment-supplied asset therefore needs
    /// a serving path that is resolved at run time, which <c>Program.cs</c> mounts at this prefix from
    /// a <c>brand/</c> directory beside the application. Found by serving a real file and getting a
    /// 404, not by review.
    /// </remarks>
    public const string LogoPathPrefix = "/brand/";

    /// <summary>
    /// The directory beside the application from which <see cref="LogoPathPrefix"/> is served.
    /// </summary>
    public const string BrandAssetsDirectoryName = "brand";

    public bool HasDisplayName => !string.IsNullOrWhiteSpace(DisplayName);

    public bool HasContactDetails =>
        !string.IsNullOrWhiteSpace(ContactEmail) || !string.IsNullOrWhiteSpace(ContactPhone);

    public bool HasLogo => !string.IsNullOrWhiteSpace(LogoPath);

    /// <summary>
    /// Fails startup naming the offending key when supplied identity is malformed.
    /// </summary>
    /// <remarks>
    /// <b>Absent is valid; broken is not</b> — the same split <c>LegalContentOptions.Validate</c>
    /// applies to legal text. Nothing configured is the expected state until the company supplies
    /// its identity, so absence only warns; a logo that would reach a third party, or one no
    /// screen-reader can announce, is a mistake and is refused.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The logo path is not site-relative, is protocol-relative, or has no name to caption it.
    /// </exception>
    public void Validate()
    {
        if (!HasLogo)
        {
            return;
        }

        var path = LogoPath!.Trim();

        // "//cdn.example/logo.png" and "/\cdn.example/logo.png" both begin with '/' and both leave
        // this origin — the first is protocol-relative, and browsers normalise the backslash form to
        // the same thing. A naive StartsWith('/') check passes them, which is precisely why this
        // check is written out rather than assumed.
        var isOffOrigin = path.StartsWith("//", StringComparison.Ordinal)
            || path.StartsWith("/\\", StringComparison.Ordinal);

        if (!path.StartsWith('/') || isOffOrigin)
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(LogoPath)}' has value '{LogoPath}', which is not a " +
                $"site-relative path. Expected a path under '{LogoPathPrefix}', e.g. " +
                $"'{LogoPathPrefix}logo.svg'. An absolute or protocol-relative URL is refused even " +
                "over HTTPS: a customer page loads nothing off-origin, because a third-party request " +
                "would disclose which customer opened which quote and when.");
        }

        // Site-relative is necessary but not sufficient: only this prefix is actually served at run
        // time. Without this check a '/img/logo.svg' would validate, start cleanly, and render a
        // broken image on every customer's quote — which is exactly the plausible-looking breakage
        // this slice keeps designing against.
        if (!path.StartsWith(LogoPathPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(LogoPath)}' has value '{LogoPath}', which is not " +
                $"under '{LogoPathPrefix}'. Deployment-supplied assets are served from a 'brand' " +
                "directory beside the application, because MapStaticAssets serves only files present " +
                "at build time — a file added to wwwroot afterwards is on disk and still answers 404.");
        }

        // The logo replaces the company's name in the header, so the name is what captions it. A
        // logo with no accessible name is an unlabelled image where the brand should be.
        if (!HasDisplayName)
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(LogoPath)}' is set without " +
                $"'{SectionName}:{nameof(DisplayName)}'. The logo replaces the company name in the " +
                "page header and uses it as the image's alternative text, so it cannot be announced " +
                "without one; supply the name or remove the logo.");
        }
    }
}
