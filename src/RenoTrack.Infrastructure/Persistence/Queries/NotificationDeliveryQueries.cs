using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Common;
using RenoTrack.Infrastructure.Persistence.Entities;

namespace RenoTrack.Infrastructure.Persistence.Queries;

/// <inheritdoc />
/// <remarks>
/// Deliberately the same shape as <see cref="LeadQueries"/> — project inside <c>Select(...)</c>
/// rather than materializing entities and mapping afterwards, count before paging, and
/// <c>AsNoTracking</c> throughout, since nothing on this path is ever mutated.
/// </remarks>
public sealed class NotificationDeliveryQueries(RenoTrackDbContext dbContext) : INotificationDeliveryQueries
{
    public async Task<PagedResult<NotificationDeliveryDto>> GetPagedAsync(
        NotificationDeliveryStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = dbContext.NotificationDeliveries.AsNoTracking();

        if (status.HasValue)
        {
            // Served by the (Status) index migration #9 already created for exactly this read.
            query = query.Where(delivery => delivery.Status == status.Value);
        }

        // Counted before paging, so TotalCount describes the whole filtered set rather than the page.
        var totalCount = await query.CountAsync(cancellationToken);

        // Newest first: this is a triage list, where the most recent failure is the one that still
        // matters. Ordering is not optional — Skip/Take over an unordered query has no defined
        // result, so pages could silently repeat or omit rows. Id is the tiebreaker because
        // CreatedAt is not unique, and here it is genuinely load-bearing rather than defensive: six
        // notifications can be written within one datetime2 tick of each other during a burst.
        var items = await query
            .OrderByDescending(delivery => delivery.CreatedAt)
            .ThenByDescending(delivery => delivery.Id)
            .Skip((page - Pagination.FirstPage) * pageSize)
            .Take(pageSize)
            .Select(delivery => new NotificationDeliveryDto(
                delivery.Id,
                delivery.NotificationType,
                delivery.EntityType,
                delivery.EntityId,
                delivery.Status,
                delivery.Recipient,
                delivery.CreatedAt,
                delivery.LastAttemptAt,
                delivery.AttemptCount,
                delivery.SentAt,
                delivery.FailureType,
                delivery.FailureMessage))
            .ToListAsync(cancellationToken);

        return new PagedResult<NotificationDeliveryDto>(items, page, pageSize, totalCount);
    }
}
