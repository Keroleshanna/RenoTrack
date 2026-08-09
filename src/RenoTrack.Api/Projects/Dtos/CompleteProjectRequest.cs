namespace RenoTrack.Api.Projects.Dtos;

/// <summary>
/// The body of <c>POST /api/v1/projects/{id}/complete</c> — exactly the two fields Sequence Diagram
/// §10 sends (<c>{ forceOverride: true, reason }</c>).
///
/// <para>
/// A strict subset of <c>CompleteProjectCommand</c>, which is what justifies the record existing at
/// all (D61): the Project id comes from the route and the completing Admin from the token's subject
/// claim. Neither is accepted from the caller.
/// </para>
/// <para>
/// <b>The body is optional as a whole.</b> The ordinary case — completing a Project whose Invoices
/// are all settled — has nothing to say, so omitting the body entirely means
/// <c>forceOverride = false</c> and no reason. The defaults below are what an absent body resolves
/// to, so there is one representation of "no override" rather than two.
/// </para>
/// <para>
/// <b>A reason without <c>forceOverride</c> is rejected, not ignored</b> (Phase 8 Slice 6, decision
/// K-4). It buys nothing and bypasses nothing, and accepting-then-discarding it would repeat the
/// pattern Phase 6 refused for the FR-6.3 rejection reason.
/// </para>
/// </summary>
public sealed record CompleteProjectRequest(bool ForceOverride = false, string? Reason = null);
