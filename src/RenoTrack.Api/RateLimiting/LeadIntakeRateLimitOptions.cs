namespace RenoTrack.Api.RateLimiting;

/// <summary>
/// Abuse protection for the one anonymous, state-creating endpoint in the API — the public contact
/// form, <c>POST /api/v1/leads</c> (Phase 13 Slice 7; <c>Architecture.md</c> §12).
/// </summary>
/// <remarks>
/// <para>
/// <b>A separate policy from <see cref="PublicRateLimitOptions"/>, deliberately.</b> That one
/// protects the token-link surface, which a real customer uses repeatedly in one sitting — they
/// open a quote, read it, and decide. This one protects a form a real customer submits once.
/// Sharing a bucket would let form spam throttle a customer reading their own quote, and a customer
/// reading their quote consume the form's allowance. Separate buckets mean trouble in one surface
/// cannot close the other.
/// </para>
/// <para>
/// <b>Why this endpoint needs its own limit at all:</b> it is anonymous and it <em>writes</em>.
/// Every other anonymous route reads. An unthrottled writer is how a public form becomes a way to
/// fill a company's pipeline with rubbish, and the Admin's inbox with notifications about it
/// (FR-9.2).
/// </para>
/// <para>
/// <b>The numbers are a policy decision, not an inferred requirement</b>, exactly as
/// <b>D65</b>'s are: no document states a limit. Five submissions per ten minutes per client is far
/// above what a real enquiry needs — one submission, or two after a typo — and far below what
/// automated abuse is worth attempting with.
/// </para>
/// <para>
/// <b>Per <em>client</em> only where the deployment says who the proxies are.</b> The partition is
/// the connection's address (<see cref="PublicRateLimitPartition"/>), so behind a reverse proxy
/// whose address is not named in <c>TrustedForwarders</c> every visitor collapses into one bucket
/// and five submissions close the form for everyone. That configuration is a deployment
/// prerequisite rather than a code gap (<b>D97</b>), and it is listed as such in
/// <c>DEPLOYMENT_CONFIGURATION.md</c>.
/// </para>
/// <para>
/// Compiled-in defaults, like <see cref="PublicRateLimitOptions"/>: a throttle's default <em>is</em>
/// the documented policy, so a deployment that has expressed no opinion gets the policy rather than
/// a startup failure.
/// </para>
/// </remarks>
public sealed class LeadIntakeRateLimitOptions
{
    public const string SectionName = "RateLimiting:LeadIntake";

    /// <summary>The named policy applied via <c>[EnableRateLimiting]</c> on the contact-form action.</summary>
    public const string PolicyName = "lead-intake";

    public const int DefaultPermitLimit = 5;
    public const int DefaultWindowSeconds = 600;

    public int PermitLimit { get; init; } = DefaultPermitLimit;

    public int WindowSeconds { get; init; } = DefaultWindowSeconds;

    public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);

    /// <summary>
    /// Fails startup naming the offending key. A zero or negative value is not a stricter policy —
    /// it would refuse every submission, taking the company's contact form offline in a way that
    /// looks like an outage rather than a misconfiguration.
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
