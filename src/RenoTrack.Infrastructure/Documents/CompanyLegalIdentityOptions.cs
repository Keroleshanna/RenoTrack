namespace RenoTrack.Infrastructure.Documents;

/// <summary>
/// The issuing company's own legal identity, as it must appear on every invoice the system produces
/// (<b>BR-5</b>, §14 UStG). Bound from the <c>CompanyLegalIdentity</c> configuration section
/// (Phase 14, <b>D110</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>No value is committed and none is invented.</b> A company's legal name, address and tax
/// number are its own facts, supplied per deployment exactly as <c>Email:FromAddress</c> and
/// <c>TokenLink:PublicBaseUrl</c> are (<b>D100</b>). A plausible-looking placeholder here would not
/// be a development convenience — it would be a fabricated tax identity on a legal document sent to
/// a real customer.
/// </para>
/// <para>
/// <b>Absence warns at startup and refuses at generation, deliberately.</b> Failing startup would
/// take an otherwise healthy deployment offline over a document nobody has asked for yet; silently
/// producing an invoice without a tax number would publish a legally deficient document. So the
/// system starts, says plainly what is missing, and refuses to generate the invoice PDF naming the
/// key — the same split <c>LegalContentOptions</c> uses for the legal pages, for the same reason.
/// </para>
/// <para>
/// <b>§14 UStG asks for a tax number <em>or</em> a VAT identification number</b>, not both, so
/// validation requires at least one. Which of the two a company uses is its accountant's decision,
/// and the template prints whichever is supplied.
/// </para>
/// <para>
/// <b>Deliberately not here:</b> bank details. §14 does not require them, and an invoice carrying
/// payment instructions nobody verified is worse than one without. They are a known gap in
/// <c>NEXT_STEPS.md</c> rather than an invented field.
/// </para>
/// </remarks>
public sealed class CompanyLegalIdentityOptions
{
    public const string SectionName = "CompanyLegalIdentity";

    internal const int MaxTextLength = 200;

    /// <summary>The company's full legal name, including its legal form (e.g. "… GmbH", "… e.K.").</summary>
    public string? LegalName { get; init; }

    public string? StreetAddress { get; init; }

    public string? PostalCode { get; init; }

    public string? Locality { get; init; }

    /// <summary>The German tax number (<i>Steuernummer</i>), if the company invoices under one.</summary>
    public string? TaxNumber { get; init; }

    /// <summary>The VAT identification number (<i>USt-IdNr.</i>), if the company holds one.</summary>
    public string? VatId { get; init; }

    /// <summary>Whether enough is configured to issue a legally complete invoice.</summary>
    public bool IsComplete => MissingKeys().Count == 0;

    /// <summary>
    /// The configuration keys §14 UStG needs that are currently absent, in the order they appear on
    /// the document. Empty when the identity is complete.
    /// </summary>
    public IReadOnlyList<string> MissingKeys()
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(LegalName)) missing.Add($"{SectionName}:{nameof(LegalName)}");
        if (string.IsNullOrWhiteSpace(StreetAddress)) missing.Add($"{SectionName}:{nameof(StreetAddress)}");
        if (string.IsNullOrWhiteSpace(PostalCode)) missing.Add($"{SectionName}:{nameof(PostalCode)}");
        if (string.IsNullOrWhiteSpace(Locality)) missing.Add($"{SectionName}:{nameof(Locality)}");

        // One of the two, never both: §14 UStG accepts either.
        if (string.IsNullOrWhiteSpace(TaxNumber) && string.IsNullOrWhiteSpace(VatId))
        {
            missing.Add($"{SectionName}:{nameof(TaxNumber)}' or '{SectionName}:{nameof(VatId)}");
        }

        return missing;
    }

    /// <summary>
    /// Fails startup naming the offending key when a supplied value is malformed. Absence is not
    /// malformed — see the remarks.
    /// </summary>
    /// <exception cref="InvalidOperationException">A supplied value is too long or has control characters.</exception>
    public void Validate()
    {
        EnsureWellFormed(LegalName, nameof(LegalName));
        EnsureWellFormed(StreetAddress, nameof(StreetAddress));
        EnsureWellFormed(PostalCode, nameof(PostalCode));
        EnsureWellFormed(Locality, nameof(Locality));
        EnsureWellFormed(TaxNumber, nameof(TaxNumber));
        EnsureWellFormed(VatId, nameof(VatId));
    }

    private static void EnsureWellFormed(string? value, string key)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length > MaxTextLength)
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{key}' must be at most {MaxTextLength} characters.");
        }

        // A control character would reach a rendered PDF and a customer's copy of it.
        if (value.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{key}' must not contain control characters.");
        }
    }
}
