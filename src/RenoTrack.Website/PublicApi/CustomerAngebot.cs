namespace RenoTrack.Website.PublicApi;

/// <summary>
/// What happened when the Website asked the API for the Angebot behind a customer's token link.
/// </summary>
/// <remarks>
/// <para>
/// <b>Four outcomes, because four are all a customer can usefully be told</b> — and deliberately
/// not "the HTTP status code", which is a detail of how the Website and the API talk to each other
/// and has no business reaching a Razor page.
/// </para>
/// <para>
/// <b>There is no "already used" outcome, and that is correct rather than missing.</b> BR-4 makes a
/// token single-use for *decisions* only and says outright that "viewing (read-only) remains
/// allowed"; `PermissionMatrix.md` §7 says the same, and
/// <c>GetPublicAngebotByTokenQueryHandler</c> deliberately does not check <c>UsedAt</c>. A customer
/// who has already answered can still re-read what they agreed to, so a consumed link reads exactly
/// like an unconsumed one. The *decision* surface, where consumption does matter, is Slice 4's.
/// </para>
/// </remarks>
public enum CustomerAngebotOutcome
{
    /// <summary>The API returned the Angebot. 200.</summary>
    Available,

    /// <summary>
    /// No such link. The API answers 404 for an unknown token and for a token belonging to something
    /// other than an Angebot, deliberately indistinguishably, and the Website preserves that: telling
    /// an anonymous caller which of the two it was would confirm a token's existence for no benefit
    /// to anyone legitimately holding a link.
    /// </summary>
    NotFound,

    /// <summary>The link's validity window has closed. 410.</summary>
    Expired,

    /// <summary>
    /// The API could not answer — a 5xx, a network failure, a timeout, or a response the Website
    /// could not parse. Distinct from <see cref="NotFound"/> on purpose: "we cannot reach your quote
    /// right now" invites the customer to come back, while "this link is not valid" tells them to
    /// stop trying. Reporting an outage as a bad link would be a lie with a real cost.
    /// </summary>
    Unavailable,
}

/// <summary>
/// The Angebot as this Website needs it. **Deliberately the Website's own type**, mirroring the
/// API's JSON contract rather than sharing a class with it — the Website references no backend
/// project (<c>CLAUDE.md</c> §1), so the API's <c>PublicAngebotDto</c> is not reachable here and
/// must not be made reachable.
/// </summary>
/// <remarks>
/// <b>One field, because the skeleton needs one field.</b> Slice 2 exists to prove the round trip
/// and establish the boundary; the sections, items, totals and VAT breakdown Wireframe A3 renders
/// arrive in Slice 3, when a page actually displays them. This is the same growth-on-demand
/// discipline <c>CLAUDE.md</c> §7 applies to DTOs and §4 to repositories: a field is added when a
/// real use case reads it, never "while we are here".
/// </remarks>
public sealed record CustomerAngebot(string AngebotNumber);

/// <summary>
/// The outcome of one lookup, and the Angebot when there is one.
/// </summary>
/// <remarks>
/// <b>Nothing the API said ever crosses this type.</b> No status code, no ProblemDetails
/// <c>detail</c>, no exception message, no internal id. Every mapped exception in the API is
/// authored for an API caller in English and may name an aggregate or an id (D59); the customer
/// gets the Website's own German wording, chosen from <see cref="Outcome"/>. This mirrors the rule
/// <c>CLAUDE.md</c> §23 already sets for the Dashboard — map the *outcome*, never render the
/// backend's <c>detail</c> — and matters more here, because this audience is not staff.
/// </remarks>
public sealed record CustomerAngebotResult(CustomerAngebotOutcome Outcome, CustomerAngebot? Angebot)
{
    public static CustomerAngebotResult Available(CustomerAngebot angebot) =>
        new(CustomerAngebotOutcome.Available, angebot);

    public static CustomerAngebotResult NotFound() => new(CustomerAngebotOutcome.NotFound, null);

    public static CustomerAngebotResult Expired() => new(CustomerAngebotOutcome.Expired, null);

    public static CustomerAngebotResult Unavailable() => new(CustomerAngebotOutcome.Unavailable, null);
}
