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

    public void Validate()
    {
        if (LifetimeDays <= 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(LifetimeDays)}' is required and must be greater than zero.");
        }
    }
}
