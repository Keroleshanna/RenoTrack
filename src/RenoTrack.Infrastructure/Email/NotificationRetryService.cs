using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Entities;
using RenoTrack.Infrastructure.Persistence.Queries;

namespace RenoTrack.Infrastructure.Email;

/// <summary>
/// Manual, Admin-triggered, synchronous retry of one notification (D70, Phase 9 Slice 5).
/// </summary>
public interface INotificationRetryService
{
    /// <summary>
    /// Retries one delivery and returns the row in its resulting terminal state.
    /// </summary>
    /// <exception cref="NotFoundException">No delivery with this id exists.</exception>
    /// <exception cref="ConflictException">
    /// Every refusal, without exception (S5-9): email delivery is disabled, the delivery is already
    /// <c>Sent</c>, another request claimed it first, or the business state makes re-sending unsafe.
    /// One status for every refusal keeps the contract uniform for a caller.
    /// </exception>
    Task<NotificationDeliveryDto> RetryAsync(int deliveryId, CancellationToken cancellationToken);
}

/// <inheritdoc />
/// <remarks>
/// <para><b>Registered unconditionally, and that is forced rather than tidy.</b> <c>Email:Enabled</c>
/// is <c>false</c> in <c>appsettings.json</c>, in both test projects and on every non-production
/// host, and in that state <see cref="SmtpEmailSender"/>, <see cref="EmailMessageFactory"/> and
/// <see cref="Identity.InspectorEmailLookup"/> are not in the container at all. A conditionally
/// registered retry service would therefore make <c>NotificationDeliveriesController</c>
/// unconstructable and take Slice 4's working <c>GET</c> endpoint down with it — along with
/// <c>ValidateOnBuild</c>. So this half is always present and refuses politely; only the machinery
/// behind it is conditional. <b>No fake or second sender was introduced to achieve that</b>
/// (S5-9).</para>
///
/// <para><b>On the <see cref="IServiceProvider"/>:</b> exactly one named type is resolved from it,
/// and only after <see cref="EmailOptions.Enabled"/> has proven that type is registered. That is the
/// narrow, fixed, visible resolution <c>CLAUDE.md</c> §21 sanctions (the <c>IdentityRoleSeeder</c>
/// precedent), not a service locator — no type is chosen at runtime, and the set cannot grow without
/// editing this line.</para>
///
/// <para><b>There is no lease, timeout, sweeper or worker here</b>, and none is coming: every state
/// change on this path begins with an Admin's HTTP request (D69/D70).</para>
/// </remarks>
public sealed class NotificationRetryService(
    RenoTrackDbContext dbContext,
    EmailOptions emailOptions,
    IServiceProvider serviceProvider) : INotificationRetryService
{
    /// <summary>
    /// The states a retry may claim. <c>Sent</c> is deliberately absent — it is terminal, and
    /// re-sending a delivered notification is the one duplicate this system does not accept.
    ///
    /// <para><c>Sending</c> is present <b>because</b> nothing recovers it automatically (S5-3): with
    /// no lease and no background process, a crash mid-attempt strands a row there forever, and an
    /// Admin clicking retry again is the only thing that can rescue it.</para>
    /// </summary>
    private static readonly NotificationDeliveryStatus[] RetryableStatuses =
    [
        NotificationDeliveryStatus.Failed,
        NotificationDeliveryStatus.Pending,
        NotificationDeliveryStatus.Sending,
    ];

    public async Task<NotificationDeliveryDto> RetryAsync(int deliveryId, CancellationToken cancellationToken)
    {
        // A projection, not the entity — deliberately (S5-2). Nothing tracked may be loaded before
        // the claim, or the terminal SaveChangesAsync could write a stale AttemptCount back over it.
        // This carries only what the pre-claim checks need: which notification, and which business
        // record it belongs to.
        var target = await dbContext.NotificationDeliveries
            .AsNoTracking()
            .Where(d => d.Id == deliveryId)
            .Select(d => new { d.NotificationType, d.EntityId })
            .FirstOrDefaultAsync(cancellationToken);

        if (target is null)
        {
            throw new NotFoundException(nameof(NotificationDelivery), deliveryId);
        }

        // Before any SMTP work, and before the claim: refusing after claiming would inflate
        // AttemptCount for an attempt that never happened (S5-9).
        if (!emailOptions.Enabled)
        {
            throw new ConflictException(
                "Email delivery is disabled for this deployment, so notifications cannot be retried. " +
                "Enable 'Email:Enabled' and restart before retrying.");
        }

        var executor = serviceProvider.GetRequiredService<NotificationRetryExecutor>();

        // S5-10: staleness is decided here, read-only, *before* the claim — so a refused retry leaves
        // the row byte-for-byte as it was. An earlier implementation claimed first and then marked a
        // refused row Failed, which quietly gave Failed a second meaning ("refused", not "attempted
        // and undelivered"), persisted a message outside S3-2's three approved categories, and left a
        // permanently-invalid notification permanently retryable with an AttemptCount that climbed on
        // every refusal. None of that was approved; this ordering removes the need for it entirely.
        var refusal = await executor.ValidateAsync(target.NotificationType, target.EntityId, cancellationToken);

        if (refusal is not null)
        {
            throw new ConflictException(refusal);
        }

        var attemptedAt = DateTime.UtcNow;
        var claimed = await ClaimAsync(deliveryId, attemptedAt, cancellationToken);

        if (!claimed)
        {
            throw new ConflictException(
                $"Notification delivery {deliveryId} is not in a retryable state. It has either already been " +
                "sent, or another retry claimed it first.");
        }

        // Loaded only *after* the claim (S5-2). Loading first would leave this DbContext tracking a
        // stale AttemptCount, and the terminal SaveChangesAsync below would write that stale value
        // back — silently undoing the increment the claim just made. Pinned by a test, not trusted
        // to this comment.
        var delivery = await dbContext.NotificationDeliveries
            .FirstAsync(d => d.Id == deliveryId, cancellationToken);

        // Past this point every outcome is a real delivery outcome, recorded by the Slice 2 boundary
        // as Sent or Failed. There is no second refusal path after the claim, by design: business
        // state that has changed since ValidateAsync ran surfaces inside the boundary as an ordinary
        // preparation failure, using the approved category message and the real exception type.
        await executor.ExecuteAsync(delivery, cancellationToken);

        return ToDto(delivery);
    }

    /// <summary>
    /// The atomic claim (S5-2): one conditional <c>UPDATE</c> that both selects the winner and
    /// records the attempt.
    /// </summary>
    /// <remarks>
    /// <para>Set-based <c>ExecuteUpdateAsync</c>, so the status test and the write are a single
    /// statement at the database rather than a read followed by a write with a race between them.
    /// Two Admins double-clicking produce one winner: the loser's <c>WHERE</c> matches nothing,
    /// because the winner has already moved the row to <c>Sending</c>. Same shape and same
    /// justification as <c>TokenService.RevokeAllForUserAsync</c> — still EF Core LINQ, so D52's
    /// narrowly-scoped raw-SQL exception does not apply.</para>
    ///
    /// <para><c>AttemptCount</c> is incremented <b>from the column</b>, never from a value this
    /// process read earlier, which is what makes the increment correct under concurrency rather than
    /// merely usually correct.</para>
    /// </remarks>
    private async Task<bool> ClaimAsync(int deliveryId, DateTime attemptedAt, CancellationToken cancellationToken)
    {
        var rowsAffected = await dbContext.NotificationDeliveries
            .Where(d => d.Id == deliveryId && RetryableStatuses.Contains(d.Status))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(d => d.Status, NotificationDeliveryStatus.Sending)
                    .SetProperty(d => d.AttemptCount, d => d.AttemptCount + 1)
                    .SetProperty(d => d.LastAttemptAt, attemptedAt),
                cancellationToken);

        return rowsAffected == 1;
    }

    private static NotificationDeliveryDto ToDto(NotificationDelivery delivery) =>
        new(
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
            delivery.FailureMessage);
}
