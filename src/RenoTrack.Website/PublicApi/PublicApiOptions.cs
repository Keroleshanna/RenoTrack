namespace RenoTrack.Website.PublicApi;

/// <summary>
/// Where this Website reaches the RenoTrack API, bound from the <c>PublicApi</c> configuration
/// section.
/// </summary>
/// <remarks>
/// <para>
/// <b>The Website talks to the API over HTTP and references no backend project</b>
/// (<c>CLAUDE.md</c> §1). That is why this is a plain URL rather than a project reference, and why
/// nothing here names an Application or Domain type.
/// </para>
/// <para>
/// <b>No compiled-in default</b>, the same shape as <c>TokenLinkOptions</c>, the connection string,
/// the JWT settings and the file-storage root: an absent or nonsensical value fails startup naming
/// the exact key. A silently-defaulted API address would let a deployment mistake surface as a
/// customer seeing "quote unavailable" rather than as an operator seeing a failed start.
/// </para>
/// </remarks>
public sealed class PublicApiOptions
{
    public const string SectionName = "PublicApi";

    /// <summary>
    /// The API origin, e.g. <c>https://api.example.de</c>. The Website appends
    /// <c>/api/v1/public/...</c> paths to it.
    /// </summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// How long a single call to the API may take before the customer is shown the "temporarily
    /// unavailable" page instead of a spinner that never resolves.
    /// </summary>
    /// <remarks>
    /// <b>This one does have a compiled-in default, deliberately</b> — the same reasoning
    /// <c>PublicRateLimitOptions</c> records. A timeout's default *is* the policy, and a deployment
    /// with no opinion should get the policy rather than a startup failure. <c>HttpClient</c>'s own
    /// default is 100 seconds, which is not a policy anyone chose for a page a customer is waiting
    /// on.
    /// </remarks>
    public int TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;

    public const int DefaultTimeoutSeconds = 10;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    /// <summary>
    /// The validated origin with no trailing slash, so callers append a path without needing to
    /// know whether an operator typed one.
    /// </summary>
    public string NormalizedBaseUrl => BaseUrl is null ? string.Empty : BaseUrl.TrimEnd('/');

    /// <summary>
    /// Fails startup naming the offending key, matching every other options type in this solution.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The value is absent, relative, not HTTPS, or the timeout is not positive.
    /// </exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(BaseUrl)}' is required: the customer Website " +
                "cannot reach the RenoTrack API without its origin.");
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(BaseUrl)}' has value '{BaseUrl}', which is not an " +
                "absolute URL. Expected the API origin, e.g. 'https://api.example.de'.");
        }

        // Architecture.md §12 ("HTTPS enforced everywhere") applies with particular force on this
        // call: the request carries the customer's token link in its path, so a plaintext hop would
        // put a live credential on the wire between the Website and the API. This is the same rule,
        // and the same wording, TokenLinkOptions.ValidatePublicBaseUrl already enforces on the URL
        // the customer is emailed.
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(BaseUrl)}' has scheme '{uri.Scheme}', but HTTPS is " +
                "required (Architecture.md §12). The customer's token travels in this request's path " +
                "and must never be sent over plaintext HTTP — including in Development, where " +
                "'dotnet dev-certs https --trust' is the prerequisite.");
        }

        if (TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(TimeoutSeconds)}' must be greater than zero.");
        }
    }
}
