namespace RenoTrack.Infrastructure.TokenLinks;

/// <summary>
/// Token-link settings, bound from the <c>TokenLink</c> configuration section. SRS FR-6.4 requires
/// the validity period to be "a configurable period (e.g. 30 days)" — so the 30 lives in
/// <c>appsettings.json</c> as a tracked, reviewable default rather than as a literal compiled into
/// the generator, and an operator can shorten it without a rebuild.
/// </summary>
public sealed class TokenLinkOptions
{
    public const string SectionName = "TokenLink";

    /// <summary>
    /// How long a newly issued link stays valid. No compiled-in fallback: an absent or nonsensical
    /// value fails startup naming the exact key, the same shape as the connection-string, JWT and
    /// file-storage checks. Silently defaulting would let a typo hand out links with a lifetime
    /// nobody chose — and for a credential-shaped value, "longer than intended" is the dangerous
    /// direction.
    /// </summary>
    public int LifetimeDays { get; init; }

    /// <summary>
    /// The public Website origin a customer's token link points at, e.g. <c>https://www.example.de</c>
    /// (D4.1). Interpolated into <c>{base}/angebot/{token}</c> and <c>{base}/invoice/{token}</c>,
    /// exactly the paths <c>Sequence Diagram.md</c> §6 and §9 write.
    ///
    /// <para><b>It lives here rather than under <c>Email</c> on purpose.</b> The value describes the
    /// public token-link system; email is only its first consumer, and the same links may later be
    /// produced for the Website or another channel. <b>The consequence is that this one key's
    /// requiredness is governed by <c>Email:Enabled</c></b> — nothing composes a URL while delivery
    /// is off — which is why it is validated by <see cref="ValidatePublicBaseUrl"/> rather than by
    /// <see cref="Validate"/>.</para>
    ///
    /// <para>Stored normalized (no trailing slash) so callers concatenate a path without having to
    /// know whether an operator typed one.</para>
    /// </summary>
    public string? PublicBaseUrl { get; init; }

    public void Validate()
    {
        if (LifetimeDays <= 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(LifetimeDays)}' is required and must be greater than zero.");
        }
    }

    /// <summary>
    /// Called only when email delivery is enabled, since nothing else composes a link today.
    /// Requires an absolute <c>https</c> origin: <c>Architecture.md</c> §12 states "HTTPS enforced
    /// everywhere (website, dashboard, API, token links)", and the link carries the credential that
    /// grants access to an Angebot or Invoice, so downgrading it to plaintext would put that
    /// credential on the wire.
    /// </summary>
    /// <exception cref="InvalidOperationException">The value is absent, relative, or not HTTPS.</exception>
    public void ValidatePublicBaseUrl()
    {
        if (string.IsNullOrWhiteSpace(PublicBaseUrl))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(PublicBaseUrl)}' is required when '{Email.EmailOptions.EnabledKey}' " +
                "is true: a customer's token-link email cannot be composed without the public Website origin.");
        }

        if (!Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(PublicBaseUrl)}' has value '{PublicBaseUrl}', which is not an " +
                "absolute URL. Expected the public Website origin, e.g. 'https://www.example.de'.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(PublicBaseUrl)}' has scheme '{uri.Scheme}', but HTTPS is required " +
                "(Architecture.md §12). A token link is a credential and must never be sent over plaintext HTTP.");
        }
    }

    /// <summary>
    /// The validated origin without a trailing slash. Callers append <c>/angebot/{token}</c> or
    /// <c>/invoice/{token}</c> directly.
    /// </summary>
    public string NormalizedPublicBaseUrl =>
        PublicBaseUrl is null ? string.Empty : PublicBaseUrl.TrimEnd('/');
}
