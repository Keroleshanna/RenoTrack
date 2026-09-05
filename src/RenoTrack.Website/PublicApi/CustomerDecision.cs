namespace RenoTrack.Website.PublicApi;

/// <summary>
/// What the customer chose to do about their Angebot (Wireframe A3's two buttons, SRS FR-6.3).
/// </summary>
/// <remarks>
/// <b>Two values, never three.</b> This is an <i>action</i>, while
/// <see cref="CustomerAngebotDecision"/> is a <i>state</i> and needs a third value
/// (<c>Pending</c>) that would be meaningless as an input. Collapsing the two types would let a
/// caller "decide" Pending. The API's own <c>CustomerDecision</c> draws the same line for the same
/// reason, and this type mirrors it across the boundary rather than sharing it — the Website
/// references no backend project (§1).
/// </remarks>
public enum CustomerDecisionChoice
{
    Approve = 1,
    Reject = 2,
}

/// <summary>
/// What happened when the Website tried to record the customer's decision.
/// </summary>
/// <remarks>
/// <para>
/// <b>An outcome, not an HTTP status</b> — the same reasoning as
/// <see cref="CustomerAngebotOutcome"/>: how the Website and the API talk to each other has no
/// business reaching a Razor page.
/// </para>
/// <para>
/// <b>There is no payload, deliberately.</b> Every outcome ends in a redirect to the document page,
/// which re-reads the Angebot from the API — so a DTO returned here would be a second, immediately
/// staler copy of what the next request fetches authoritatively. The API and the database are the
/// source of truth about what was recorded; what this Website attempted is not.
/// </para>
/// </remarks>
public enum CustomerDecisionOutcome
{
    /// <summary>The decision was recorded. 200.</summary>
    Recorded,

    /// <summary>
    /// No such link — an unknown token, or one belonging to something other than an Angebot. 404,
    /// conflated by the API deliberately and preserved here.
    /// </summary>
    NotFound,

    /// <summary>The link's validity window has closed. 410.</summary>
    Expired,

    /// <summary>
    /// The link has already been used for a decision, so this one changes nothing. 409.
    /// <para>
    /// <b>This is the one outcome the read surface has no equivalent for, and that asymmetry is
    /// correct.</b> BR-4 makes a token single-use for <i>decisions</i> only and leaves viewing open,
    /// so a consumed link reads exactly like an unconsumed one — which is why
    /// <see cref="CustomerAngebotOutcome"/> has no "already used" value. Consumption only becomes
    /// observable here.
    /// </para>
    /// <para>
    /// It is also reachable without anybody doing anything wrong: an honest double-click, a
    /// retrying proxy, or two people sharing one link. The customer is never blamed for it — they
    /// are shown what was actually recorded.
    /// </para>
    /// </summary>
    AlreadyDecided,

    /// <summary>
    /// The API refused the submission itself — in practice an over-length rejection reason (D98).
    /// <para>
    /// <b>Distinct from <see cref="Unavailable"/>, and that distinction arrived with Slice 5.</b>
    /// Before the reason existed, a 400 could only mean the two sides disagreed about the contract,
    /// which is this Website's fault and correctly read as an outage. Now the customer's own input
    /// can cause one, and reporting that as "we cannot reach your quote" would be both wrong and
    /// destructive — the page re-offers the form with what they typed still in it.
    /// </para>
    /// </summary>
    Invalid,

    /// <summary>
    /// The API could not answer — a 5xx, a network failure, or a timeout. Distinct from
    /// <see cref="NotFound"/> for the reason <see cref="CustomerAngebotOutcome.Unavailable"/>
    /// records: telling a customer their link is broken when the fault is ours sends them away for
    /// good.
    /// </summary>
    Unavailable,
}
