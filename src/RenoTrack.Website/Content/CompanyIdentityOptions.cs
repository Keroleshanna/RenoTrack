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

    public bool HasDisplayName => !string.IsNullOrWhiteSpace(DisplayName);

    public bool HasContactDetails =>
        !string.IsNullOrWhiteSpace(ContactEmail) || !string.IsNullOrWhiteSpace(ContactPhone);
}
