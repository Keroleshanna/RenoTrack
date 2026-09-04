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
/// Whether the customer has already answered this Angebot, and how.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the API's <c>PublicAngebotDecision</c>. Three values, because three are all a customer
/// can meaningfully be told about their own decision — and deliberately *not* the internal
/// <c>AngebotStatus</c>, which the public contract never exposes.
/// </para>
/// <para>
/// <b>An unrecognised value deserialises to nothing and the page reports an outage</b>, rather than
/// defaulting to <see cref="Pending"/>. Silently calling a decided Angebot "pending" would tell a
/// customer their answer was never recorded — a wrong statement about their own decision is worse
/// than an honest "not available right now".
/// </para>
/// </remarks>
public enum CustomerAngebotDecision
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>One priced line, as the customer sees it (Wireframe A3's item row).</summary>
/// <remarks>
/// <paramref name="Unit"/> is the unit's <i>code</i> (<c>m2</c>, <c>Stk</c>, <c>lfm</c>,
/// <c>pauschal</c>, <c>m</c>, or a custom label). <c>ItemUnit</c> is an open value object, so an
/// unrecognised code is a legitimate custom unit and must reach the page unchanged.
/// </remarks>
public sealed record CustomerItem(
    string Description,
    string? Specification,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal LineTotal);

/// <summary>One "Pos. N" block with its <c>Zwischensumme</c> (Wireframe A3).</summary>
public sealed record CustomerSection(
    string Title,
    decimal Subtotal,
    IReadOnlyList<CustomerItem> Items);

/// <summary>
/// One <c>zzgl. N% MwSt</c> line. <paramref name="Rate"/> is the percentage itself (0/7/16/19),
/// never an internal enum name.
/// </summary>
public sealed record CustomerVatLine(decimal Rate, decimal VatAmount);

/// <summary>
/// The Angebot as this Website needs it. **Deliberately the Website's own type**, mirroring the
/// API's JSON contract rather than sharing a class with it — the Website references no backend
/// project (<c>CLAUDE.md</c> §1), so the API's <c>PublicAngebotDto</c> is not reachable here and
/// must not be made reachable.
/// </summary>
/// <remarks>
/// <para>
/// Slice 2 carried one field, because a skeleton needed one field. Slice 3 renders the document, so
/// the shape now mirrors what the API has returned since Phase 6 — no API change was required for
/// any of it.
/// </para>
/// <para>
/// <b><c>decisionAt</c> is deliberately absent.</b> The API returns it, but the status message says
/// what the customer did without a timestamp, and rendering a UTC value as a German date raises a
/// timezone-policy question no project document answers. Unknown JSON properties are ignored on
/// deserialization, so omitting it costs nothing; Slice 4 adds it if Slice 4 needs it — the
/// growth-on-demand discipline of <c>CLAUDE.md</c> §7, applied here rather than waived because the
/// field happens to be nearby.
/// </para>
/// <para>
/// <b>What the API deliberately never sends is therefore absent here too</b>: internal ids,
/// <c>leadId</c>, <c>inspectionId</c>, the staff who priced and approved the quote,
/// <c>catalogItemId</c>, <c>sortOrder</c>, per-item VAT rates, per-rate net amounts, and every Lead
/// field. This type must never grow one of them.
/// </para>
/// </remarks>
public sealed record CustomerAngebot(
    string AngebotNumber,
    CustomerAngebotDecision Decision,
    decimal NetTotal,
    IReadOnlyList<CustomerVatLine> VatBreakdown,
    decimal GrossTotal,
    IReadOnlyList<CustomerSection> Sections);

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
