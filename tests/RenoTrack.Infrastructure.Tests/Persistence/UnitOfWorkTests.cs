using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Infrastructure.Tests.Persistence;

[Collection("Infrastructure Database")]
public sealed class UnitOfWorkTests(RenoTrackDbContextFixture fixture)
{
    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChangesTrackedByTheSameDbContext()
    {
        await using var context = fixture.CreateContext();
        var unitOfWork = new UnitOfWork(context);
        var lead = Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Website);
        context.Leads.Add(lead);

        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Leads.SingleAsync(l => l.Id == lead.Id);
        Assert.Equal("Jane Doe", reloaded.Name);
    }

    [Fact]
    public async Task SaveChangesAsync_WithNoPendingChanges_DoesNotThrow()
    {
        await using var context = fixture.CreateContext();
        var unitOfWork = new UnitOfWork(context);

        var exception = await Record.ExceptionAsync(() => unitOfWork.SaveChangesAsync(CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SaveChangesAsync_AlreadyCancelledToken_ThrowsOperationCanceledException()
    {
        // EF Core short-circuits SaveChangesAsync when nothing is tracked, without ever
        // checking the token — a real pending change is required to exercise cancellation.
        await using var context = fixture.CreateContext();
        var unitOfWork = new UnitOfWork(context);
        context.Leads.Add(Lead.Create("Jane Doe", "0176 1234567", "jane@example.com", LeadSource.Website));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unitOfWork.SaveChangesAsync(cts.Token));
    }
}
