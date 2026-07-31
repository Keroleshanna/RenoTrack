namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// Read-side questions the Application layer needs to ask about staff accounts.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all, given D60 kept authentication out of the Application layer.</b> That
/// decision was about authentication — logging in has no aggregate, no invariant, and no business
/// rule, so it stays in the API layer. This is the opposite case: "an Inspection may only be
/// assigned to an active Inspector" <em>is</em> a business rule, so the Application layer must be
/// able to enforce it. It cannot do that against <c>UserManager</c> directly, because Identity types
/// are Infrastructure-only (D53, forced by D1's zero-reference rule) — hence an abstraction here,
/// implemented over Identity in Infrastructure. The two decisions are consistent: business rules
/// live in Application, mechanisms live in Infrastructure.
/// </para>
/// <para>
/// Lives in <c>Common.Interfaces</c> rather than a feature folder because it returns no feature
/// DTO — the constraint that placed <c>ILeadQueries</c>/<c>ICatalogItemQueries</c> in their feature
/// folders (D23) does not apply.
/// </para>
/// </remarks>
public interface IUserQueries
{
    /// <summary>
    /// Whether the given user exists, is active, and holds the Inspector role — the exact business
    /// question "may work be assigned to this person?", rather than three generic lookups the
    /// caller would have to combine correctly (CLAUDE.md §4).
    /// </summary>
    /// <remarks>
    /// One method rather than separate existence/role/active checks, deliberately: every caller
    /// wants the same conjunction, and splitting it would invite a caller to check two of the three
    /// and quietly permit the case the third would have caught.
    /// </remarks>
    Task<bool> IsActiveInspectorAsync(int userId, CancellationToken cancellationToken);
}
