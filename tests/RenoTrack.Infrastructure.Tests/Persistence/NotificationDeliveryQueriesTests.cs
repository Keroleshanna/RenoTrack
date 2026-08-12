using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Common;
using RenoTrack.Infrastructure.Persistence.Entities;
using RenoTrack.Infrastructure.Persistence.Queries;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// <see cref="NotificationDeliveryQueries"/> against real LocalDB (D40) — which is what makes the
/// claims here worth asserting at all: that the DTO projection is genuinely translatable by EF Core
/// rather than silently evaluated client-side, that a <c>NULL</c> recipient survives projection as
/// <c>null</c>, and that the ordering SQL Server actually applies is the one paging depends on.
/// </summary>
/// <remarks>
/// This class shares the "Infrastructure Database" collection with every other Infrastructure test,
/// so <c>NotificationDeliveries</c> already holds rows written elsewhere in the run. Assertions are
/// therefore written against this test's own rows (located by a unique <c>EntityId</c>) and against
/// count <em>deltas</em> — never against the table being empty beforehand.
/// </remarks>
[Collection("Infrastructure Database")]
public sealed class NotificationDeliveryQueriesTests(RenoTrackDbContextFixture fixture)
{
    private static int NextEntityId() => Random.Shared.Next(100_000, 999_999);

    [Fact]
    public async Task GetPagedAsync_projects_every_persisted_column()
    {
        var entityId = NextEntityId();
        var sentAt = DateTime.UtcNow;

        var delivery = new NotificationDelivery(NotificationType.InvoiceReady, "Invoice", entityId);
        delivery.RecordRecipient("kundin@example.invalid");
        delivery.MarkSent(sentAt);
        await SaveAsync(delivery);

        var dto = await FindAsync(delivery.Id);

        Assert.Equal(NotificationType.InvoiceReady, dto.NotificationType);
        Assert.Equal("Invoice", dto.EntityType);
        Assert.Equal(entityId, dto.EntityId);
        Assert.Equal(NotificationDeliveryStatus.Sent, dto.Status);
        Assert.Equal("kundin@example.invalid", dto.Recipient);
        Assert.Equal(1, dto.AttemptCount);
        Assert.NotNull(dto.SentAt);
        Assert.NotNull(dto.LastAttemptAt);
        Assert.NotEqual(default, dto.CreatedAt);
        Assert.Null(dto.FailureType);
        Assert.Null(dto.FailureMessage);
    }

    /// <summary>
    /// The approved default: omitting the filter returns <c>Sent</c> rows too. §9's wording is
    /// "failed/pending", but an Admin also needs to confirm a delivery eventually succeeded.
    /// </summary>
    [Fact]
    public async Task GetPagedAsync_withoutAStatusFilter_returnsEveryStatusIncludingSent()
    {
        var pending = new NotificationDelivery(NotificationType.NewWebsiteLead, "Lead", NextEntityId());

        var sent = new NotificationDelivery(NotificationType.AngebotReady, "Angebot", NextEntityId());
        sent.MarkSent(DateTime.UtcNow);

        var failed = new NotificationDelivery(NotificationType.AngebotDecision, "Angebot", NextEntityId());
        failed.MarkFailed(DateTime.UtcNow, nameof(InvalidOperationException), "The notification could not be prepared.");

        await SaveAsync(pending, sent, failed);

        // Newest first, and these three were written last, so they occupy the top of page one.
        var page = await QueryAsync(status: null, Pagination.FirstPage, Pagination.MaxPageSize);
        var ids = page.Items.Select(i => i.Id).ToList();

        Assert.Contains(pending.Id, ids);
        Assert.Contains(sent.Id, ids);
        Assert.Contains(failed.Id, ids);
    }

    [Fact]
    public async Task GetPagedAsync_withAStatusFilter_returnsOnlyThatStatus()
    {
        var pending = new NotificationDelivery(NotificationType.NewWebsiteLead, "Lead", NextEntityId());

        var failed = new NotificationDelivery(NotificationType.AngebotChangesRequested, "Angebot", NextEntityId());
        failed.MarkFailed(DateTime.UtcNow, nameof(TimeoutException), "The mail server could not be reached or rejected the message.");

        await SaveAsync(pending, failed);

        var page = await QueryAsync(NotificationDeliveryStatus.Failed, Pagination.FirstPage, Pagination.MaxPageSize);

        Assert.Contains(failed.Id, page.Items.Select(i => i.Id));
        Assert.DoesNotContain(pending.Id, page.Items.Select(i => i.Id));

        // Not just "mine is excluded" — nothing of another status may leak through at all.
        Assert.All(page.Items, item => Assert.Equal(NotificationDeliveryStatus.Failed, item.Status));
    }

    [Fact]
    public async Task GetPagedAsync_totalCountDescribesTheWholeFilteredSet_notThePage()
    {
        var before = (await QueryAsync(status: null, Pagination.FirstPage, pageSize: 1)).TotalCount;

        await SaveAsync(
            new NotificationDelivery(NotificationType.NewWebsiteLead, "Lead", NextEntityId()),
            new NotificationDelivery(NotificationType.NewWebsiteLead, "Lead", NextEntityId()));

        var after = await QueryAsync(status: null, Pagination.FirstPage, pageSize: 1);

        Assert.Single(after.Items);
        Assert.Equal(before + 2, after.TotalCount);
        Assert.Equal(Pagination.FirstPage, after.Page);
        Assert.Equal(1, after.PageSize);
    }

    /// <summary>
    /// The tiebreaker is the point. Three rows are forced to share one <c>CreatedAt</c> — which is
    /// not contrived: <c>CreatedAt</c> is <c>DateTime.UtcNow</c> at construction, and several
    /// notifications written in the same burst can land on the same <c>datetime2</c> value. Without
    /// <c>ThenByDescending(Id)</c> the order across a page boundary is undefined, so a row could
    /// appear on both pages or on neither.
    /// </summary>
    [Fact]
    public async Task GetPagedAsync_ordersByCreatedAtThenIdDescending_acrossAPageBoundary()
    {
        var first = new NotificationDelivery(NotificationType.NewWebsiteLead, "Lead", NextEntityId());
        var second = new NotificationDelivery(NotificationType.NewWebsiteLead, "Lead", NextEntityId());
        var third = new NotificationDelivery(NotificationType.NewWebsiteLead, "Lead", NextEntityId());
        await SaveAsync(first, second, third);

        // A far-future timestamp, identical across all three: it pins them to the top of a
        // CreatedAt-descending ordering regardless of what else this shared database holds, and no
        // real row can collide with it.
        var sharedCreatedAt = new DateTime(2999, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        int[] ids = [first.Id, second.Id, third.Id];

        await using (var updateContext = fixture.CreateContext())
        {
            await updateContext.NotificationDeliveries
                .Where(d => ids.Contains(d.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.CreatedAt, sharedCreatedAt));
        }

        var pageOne = await QueryAsync(status: null, page: 1, pageSize: 2);
        var pageTwo = await QueryAsync(status: null, page: 2, pageSize: 2);

        // Descending id within the shared timestamp.
        Assert.Equal([third.Id, second.Id], pageOne.Items.Select(i => i.Id));
        Assert.Equal(first.Id, pageTwo.Items[0].Id);

        // The pages partition the set rather than overlapping it.
        Assert.Empty(pageOne.Items.Select(i => i.Id).Intersect(pageTwo.Items.Select(i => i.Id)));
    }

    /// <summary>
    /// A null recipient means "delivery failed before a recipient could be resolved" — a real
    /// answer, so the projection must carry it through as <c>null</c> rather than normalising it to
    /// an empty string or a sentinel.
    /// </summary>
    [Fact]
    public async Task GetPagedAsync_projectsAnUnresolvedRecipientAsNull()
    {
        var delivery = new NotificationDelivery(NotificationType.AngebotChangesRequested, "Angebot", NextEntityId());
        delivery.MarkFailed(DateTime.UtcNow, nameof(InvalidOperationException), "The notification could not be prepared.");
        await SaveAsync(delivery);

        var dto = await FindAsync(delivery.Id);

        Assert.Null(dto.Recipient);
        Assert.NotEqual(string.Empty, dto.Recipient);
    }

    [Fact]
    public async Task GetPagedAsync_exposesTheSanitizedFailureTypeAndMessage()
    {
        const string sanitized = "The mail server could not be reached or rejected the message.";

        var delivery = new NotificationDelivery(NotificationType.AngebotSubmittedForReview, "Angebot", NextEntityId());
        delivery.RecordRecipient($"buero@example.invalid{NotificationDelivery.RecipientSeparator}inhaber@example.invalid");
        delivery.MarkFailed(DateTime.UtcNow, nameof(TimeoutException), sanitized);
        await SaveAsync(delivery);

        var dto = await FindAsync(delivery.Id);

        Assert.Equal(NotificationDeliveryStatus.Failed, dto.Status);
        Assert.Equal(nameof(TimeoutException), dto.FailureType);
        Assert.Equal(sanitized, dto.FailureMessage);
        Assert.Null(dto.SentAt);

        // The complete recipient *set*, joined by the shared separator — the whole reason the column
        // is sized for more than one address (S3-5).
        Assert.Equal("buero@example.invalid, inhaber@example.invalid", dto.Recipient);
    }

    // ---------- helpers ----------

    private async Task SaveAsync(params NotificationDelivery[] deliveries)
    {
        await using var writeContext = fixture.CreateContext();
        writeContext.NotificationDeliveries.AddRange(deliveries);
        await writeContext.SaveChangesAsync();
    }

    private async Task<PagedResult<NotificationDeliveryDto>> QueryAsync(
        NotificationDeliveryStatus? status, int page, int pageSize)
    {
        await using var readContext = fixture.CreateContext();
        var queries = new NotificationDeliveryQueries(readContext);

        return await queries.GetPagedAsync(status, page, pageSize, CancellationToken.None);
    }

    private async Task<NotificationDeliveryDto> FindAsync(int id)
    {
        var page = await QueryAsync(status: null, Pagination.FirstPage, Pagination.MaxPageSize);

        return Assert.Single(page.Items, item => item.Id == id);
    }
}
