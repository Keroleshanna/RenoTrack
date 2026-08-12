using Microsoft.EntityFrameworkCore;
using RenoTrack.Infrastructure.Persistence.Entities;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Real SQL Server LocalDB (D40): the claims here — string-converted enums, column lengths, a
/// nullable recipient, both indexes, and a polymorphic reference with no foreign key — are exactly
/// the ones the EF Core InMemory provider would not enforce.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class NotificationDeliveryPersistenceTests(RenoTrackDbContextFixture fixture)
{
    private static int NextEntityId() => Random.Shared.Next(100_000, 999_999);

    [Fact]
    public async Task A_delivery_round_trips_with_every_column()
    {
        var entityId = NextEntityId();
        var delivery = new NotificationDelivery(NotificationType.InvoiceReady, "Invoice", entityId);
        delivery.RecordRecipient("klein@example.invalid");
        delivery.MarkSent(DateTime.UtcNow);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.NotificationDeliveries.Add(delivery);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var stored = await readContext.NotificationDeliveries.SingleAsync(d => d.EntityId == entityId);

        Assert.Equal(NotificationType.InvoiceReady, stored.NotificationType);
        Assert.Equal("Invoice", stored.EntityType);
        Assert.Equal(NotificationDeliveryStatus.Sent, stored.Status);
        Assert.Equal("klein@example.invalid", stored.Recipient);
        Assert.Equal(1, stored.AttemptCount);
        Assert.NotNull(stored.SentAt);
        Assert.NotNull(stored.LastAttemptAt);
        Assert.Null(stored.FailureType);
        Assert.Null(stored.FailureMessage);
    }

    /// <summary>
    /// CLAUDE.md §21's string-enum convention, verified against the real column rather than the model:
    /// a row must be readable in raw SQL during support.
    /// </summary>
    [Fact]
    public async Task Enums_are_stored_as_strings()
    {
        var entityId = NextEntityId();

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.NotificationDeliveries.Add(
                new NotificationDelivery(NotificationType.AngebotChangesRequested, "Angebot", entityId));
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();

        var row = await readContext.Database
            .SqlQuery<StoredEnums>(
                $"SELECT NotificationType, Status FROM NotificationDeliveries WHERE EntityId = {entityId}")
            .SingleAsync();

        Assert.Equal("AngebotChangesRequested", row.NotificationType);
        Assert.Equal("Pending", row.Status);
    }

    /// <summary>S3-3: null is a legitimate, meaningful value — delivery failed before resolution.</summary>
    [Fact]
    public async Task A_null_recipient_is_accepted()
    {
        var entityId = NextEntityId();
        var delivery = new NotificationDelivery(NotificationType.AngebotChangesRequested, "Angebot", entityId);
        delivery.MarkFailed(DateTime.UtcNow, "InvalidOperationException", "The notification could not be prepared.");

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.NotificationDeliveries.Add(delivery);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var stored = await readContext.NotificationDeliveries.SingleAsync(d => d.EntityId == entityId);

        Assert.Null(stored.Recipient);
        Assert.Equal(NotificationDeliveryStatus.Failed, stored.Status);
        Assert.Equal("InvalidOperationException", stored.FailureType);
    }

    /// <summary>
    /// The polymorphic reference has no foreign key — one column cannot point at Leads, Angebote and
    /// Invoices at once. An EntityId matching no real row must therefore be accepted, exactly as the
    /// equivalent test pins for TokenLinks.
    /// </summary>
    [Fact]
    public async Task An_entity_id_matching_no_row_is_accepted_because_there_is_no_foreign_key()
    {
        var entityId = NextEntityId();

        await using var context = fixture.CreateContext();
        context.NotificationDeliveries.Add(new NotificationDelivery(NotificationType.NewWebsiteLead, "Lead", entityId));

        await context.SaveChangesAsync();

        Assert.True(await context.NotificationDeliveries.AnyAsync(d => d.EntityId == entityId));
    }

    /// <summary>
    /// The column is the backstop, not the guard. <c>Email:AdminRecipients</c> is validated at startup
    /// against this same limit (S3-5), so an over-long recipient set cannot reach here through the
    /// real code path. This pins that if one ever did, the database <b>rejects</b> the row rather than
    /// silently truncating it — a shortened recipient list would misreport who a notification was
    /// sent to, which is worse than no row at all.
    /// </summary>
    [Fact]
    public async Task A_recipient_longer_than_the_column_is_rejected_by_the_database()
    {
        var delivery = new NotificationDelivery(NotificationType.AngebotReady, "Angebot", NextEntityId());
        delivery.RecordRecipient(new string('a', NotificationDelivery.MaxRecipientLength + 1));

        await using var context = fixture.CreateContext();
        context.NotificationDeliveries.Add(delivery);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Both_approved_indexes_exist()
    {
        await using var context = fixture.CreateContext();

        var indexes = await context.Database
            .SqlQuery<string>($"SELECT name AS Value FROM sys.indexes WHERE object_id = OBJECT_ID('NotificationDeliveries') AND name IS NOT NULL")
            .ToListAsync();

        Assert.Contains("IX_NotificationDeliveries_Status", indexes);
        Assert.Contains("IX_NotificationDeliveries_EntityType_EntityId", indexes);
    }

    [Fact]
    public async Task The_table_has_no_foreign_keys()
    {
        await using var context = fixture.CreateContext();

        var foreignKeyCount = await context.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('NotificationDeliveries')")
            .SingleAsync();

        Assert.Equal(0, foreignKeyCount);
    }

    private sealed record StoredEnums(string NotificationType, string Status);
}
