using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RenoTrack.Application.Common;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Proves AuditService's Best-Effort Audit strategy (D50) against real LocalDB: LogAsync commits
/// its own write independently (no IUnitOfWork involved — nothing else would ever persist it),
/// and a failure during that write is caught and logged rather than propagated, so it can never
/// invalidate an already-committed business operation.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class AuditServiceTests(RenoTrackDbContextFixture fixture)
{
    [Fact]
    public async Task LogAsync_PersistsAllFieldsCorrectly()
    {
        await using var context = fixture.CreateContext();
        var service = new AuditService(context, NullLogger<AuditService>.Instance);

        await service.LogAsync("Lead", 42, AuditAction.LeadCreated, performedByUserId: 7, details: "Created via Website form", CancellationToken.None);

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.AuditLogs.SingleAsync(a => a.EntityType == "Lead" && a.EntityId == 42);

        Assert.Equal(AuditAction.LeadCreated, reloaded.Action);
        Assert.Equal(7, reloaded.PerformedByUserId);
        Assert.Equal("Created via Website form", reloaded.Details);
        Assert.True(reloaded.CreatedAt > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task LogAsync_WithNoPerformingUserAndNoDetails_PersistsBothAsNull()
    {
        await using var context = fixture.CreateContext();
        var service = new AuditService(context, NullLogger<AuditService>.Instance);

        await service.LogAsync("Invoice", 99, AuditAction.LeadCreated, performedByUserId: null, details: null, CancellationToken.None);

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.AuditLogs.SingleAsync(a => a.EntityType == "Invoice" && a.EntityId == 99);

        Assert.Null(reloaded.PerformedByUserId);
        Assert.Null(reloaded.Details);
    }

    [Fact]
    public async Task LogAsync_CommitsIndependently_WithNoUnitOfWorkInvolved()
    {
        // No UnitOfWork.SaveChangesAsync is called anywhere in this test — proving LogAsync
        // persists its own write, since no handler calls SaveChangesAsync again after step 6.
        await using var context = fixture.CreateContext();
        var service = new AuditService(context, NullLogger<AuditService>.Instance);

        await service.LogAsync("Angebot", 5, AuditAction.AngebotCreated, 3, null, CancellationToken.None);

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.AuditLogs.SingleOrDefaultAsync(a => a.EntityType == "Angebot" && a.EntityId == 5);
        Assert.NotNull(reloaded);
    }

    [Fact]
    public async Task LogAsync_WhenTheUnderlyingWriteFails_DoesNotThrow()
    {
        // Best-Effort Audit (D50): a disposed DbContext makes the write fail deterministically —
        // LogAsync must swallow that failure, never propagate it to the caller.
        var context = fixture.CreateContext();
        await context.DisposeAsync();
        var service = new AuditService(context, NullLogger<AuditService>.Instance);

        var exception = await Record.ExceptionAsync(() =>
            service.LogAsync("Lead", 1, AuditAction.LeadCreated, null, null, CancellationToken.None));

        Assert.Null(exception);
    }
}
