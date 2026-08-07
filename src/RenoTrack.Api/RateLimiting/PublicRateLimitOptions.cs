namespace RenoTrack.Api.RateLimiting;

/// <summary>
/// The public-surface abuse-protection policy required by <c>Architecture.md</c> §12 ("rate
/// limiting / basic abuse protection on public endpoints … to prevent scraping or brute-forcing
/// token guesses") and assigned to Phase 6 by <c>PROJECT_ROADMAP.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The numbers here are a policy decision, not an inferred requirement.</b> No project document
/// states a limit, window, algorithm or partition key — only the threat to defend against. 30
/// requests per minute per client IP was chosen explicitly (see <c>ARCHITECTURE_DECISIONS.md</c>
/// D65): far above what a real customer generates, since they open one link and click one button,
/// and far below what token enumeration needs to be worth attempting against a 256-bit secret.
/// They live here, named, rather than as literals in <c>Program.cs</c> and again in tests.
/// </para>
/// <para>
/// <b>Unlike <c>TokenLinkOptions</c>, this one has compiled-in defaults</b>, and the difference is
/// deliberate. A token lifetime has no safe default — silently guessing "longer than intended" on a
/// credential is dangerous, so absence must fail startup. A throttle's default *is* the documented
/// policy, and a deployment that has expressed no opinion should get the policy rather than a
/// startup failure. Configuration overrides it for operators who need to tune it, and for tests
/// that need a small limit without waiting out a real window.
/// </para>
/// </remarks>
public sealed class PublicRateLimitOptions
{
    public const string SectionName = "RateLimiting:Public";

    /// <summary>The named policy applied via <c>[EnableRateLimiting]</c> on the public controller.</summary>
    public const string PolicyName = "public";

    public const int DefaultPermitLimit = 30;
    public const int DefaultWindowSeconds = 60;

    public int PermitLimit { get; init; } = DefaultPermitLimit;

    public int WindowSeconds { get; init; } = DefaultWindowSeconds;

    public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);

    /// <summary>
    /// Fails startup naming the offending key, the same shape as every other options type here. A
    /// zero or negative limit would not be a stricter policy — it would refuse every public request,
    /// taking the customer-facing surface offline in a way that looks like an outage rather than a
    /// misconfiguration.
    /// </summary>
    public void Validate()
    {
        if (PermitLimit <= 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(PermitLimit)}' must be greater than zero.");
        }

        if (WindowSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:{nameof(WindowSeconds)}' must be greater than zero.");
        }
    }
}
