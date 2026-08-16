namespace RenoTrack.Infrastructure.Identity;

/// <summary>One staff member, as a name to render beside the ids the business DTOs carry.</summary>
public sealed record UserSummaryDto(int Id, string Name, string Role, bool IsActive);

/// <summary>
/// Who the staff are — the read that lets a screen print "T. Hoffmann" instead of "1003".
/// </summary>
/// <remarks>
/// <para>
/// <b>Infrastructure-owned end to end, and consumed directly by its controller</b> — the same shape
/// as <c>INotificationDeliveryQueries</c> (D69) and for the same reason D77 gives: Identity is
/// Infrastructure-only by D53, so an Application-side query would have to reach into a layer
/// Application deliberately cannot see. There is no aggregate here, no Domain invariant, no state
/// transition and no audit milestone — D60's test, applied again.
/// </para>
/// <para>
/// <b>This does not generalise.</b> <c>ILeadQueries</c>, <c>IAngebotQueries</c>,
/// <c>IInvoiceQueries</c> and the rest return Domain-derived DTOs and correctly live in Application.
/// The boundary is <em>whose data is it</em>, not <em>is it a read</em>.
/// </para>
/// <para>
/// <b>Why this matters beyond convenience:</b> without it the UI can only render a raw integer id.
/// That is worse than rendering nothing, because an id looks like data and so nobody reports it as
/// missing — a defect that reached a running screen once already during this phase's earlier work.
/// </para>
/// <para>
/// Because <c>DependencyInjectionTests</c> reflects only over the Application assembly, this
/// registration needs its own explicit resolution test — exactly as D77 requires.
/// </para>
/// </remarks>
public interface IUserDirectoryQueries
{
    /// <summary>
    /// Staff members, optionally narrowed to one role and to active accounts only.
    /// </summary>
    /// <remarks>
    /// Unpaged, deliberately: this is a company's internal staff list — two roles and a handful of
    /// people — not an open collection. It is the source for an assignment dropdown, which needs the
    /// whole set to be useful at all.
    /// </remarks>
    /// <param name="role">
    /// An Identity role name, or <see langword="null"/> for every role. Not validated against the
    /// known roles: an unknown name yields an empty list, which is the honest answer.
    /// </param>
    Task<IReadOnlyList<UserSummaryDto>> GetStaffAsync(
        string? role,
        bool activeOnly,
        CancellationToken cancellationToken);
}
