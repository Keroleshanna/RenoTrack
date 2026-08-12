using RenoTrack.Application.Common;
using RenoTrack.Infrastructure.Persistence.Entities;

namespace RenoTrack.Infrastructure.Persistence.Queries;

/// <summary>
/// The Admin's read over <c>NotificationDeliveries</c> (`PermissionMatrix.md` §9).
///
/// <para><b>Declared in Infrastructure, unlike <c>ILeadQueries</c>/<c>ICatalogItemQueries</c></b>,
/// which live in Application beside the DTOs they return. The difference is not stylistic: this
/// interface names two Infrastructure-owned enums, so it could not compile in Application without
/// moving them (D69 forbids it). It is still an interface rather than a concrete class for the same
/// two reasons <c>OwnershipValidator</c> is (<c>CLAUDE.md</c> §9): consistent DI registration, and a
/// seam a test could substitute at — not because a second implementation is expected.</para>
///
/// <para>Narrow on purpose (<c>CLAUDE.md</c> §4): one method, for the one screen that exists. No
/// <c>entityType</c>/<c>entityId</c> filter is offered even though the table is indexed on that pair
/// — the index answers "what happened to this Angebot's notifications" for whoever needs it next,
/// and a filter is added when a real caller asks, not because an index makes it cheap.</para>
/// </summary>
public interface INotificationDeliveryQueries
{
    /// <param name="status">
    /// Optional. When omitted, <b>every</b> status is returned including <c>Sent</c>. §9's wording is
    /// "failed/pending notifications", but hiding successful rows would make "did my retry actually
    /// work?" unanswerable from the only screen that shows delivery state at all.
    /// </param>
    Task<PagedResult<NotificationDeliveryDto>> GetPagedAsync(
        NotificationDeliveryStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
