using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Identity;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Real SQL constraints behind <c>ProjectConfiguration</c>, against real LocalDB (D40): the unique
/// index on <c>AngebotId</c>, both FKs, the string-stored status, and — the one that matters most
/// for a legal document — that <c>decimal(18,2)</c> round-trips <c>AgreedTotal</c> exactly.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class ProjectPersistenceTests(RenoTrackDbContextFixture fixture)
{
    private async Task<int> SeedLeadAsync()
    {
        var lead = Lead.Create("M. Klein", "0176 1234567", "m.klein@example.com", LeadSource.Phone);
        await using var writeContext = fixture.CreateContext();
        writeContext.Leads.Add(lead);
        await writeContext.SaveChangesAsync();
        return lead.Id;
    }

    private async Task<int> SeedCustomerAsync()
    {
        var leadId = await SeedLeadAsync();
        var customer = Customer.Create(leadId, "M. Klein", "m.klein@example.com", "0176 1234567");
        await using var writeContext = fixture.CreateContext();
        writeContext.Customers.Add(customer);
        await writeContext.SaveChangesAsync();
        return customer.Id;
    }

    /// <summary>Project.AngebotId is a real FK — needs an actually-persisted Angebot row.</summary>
    private async Task<int> SeedAngebotAsync()
    {
        var leadId = await SeedLeadAsync();

        var user = new ApplicationUser { Name = "Test Inspector" };
        await using var writeContext = fixture.CreateContext();
        writeContext.Users.Add(user);
        await writeContext.SaveChangesAsync();

        var angebot = Angebot.Create(leadId, inspectionId: null, angebotNumber: $"ANG-{Guid.NewGuid():N}"[..18], createdByInspectorId: user.Id);
        writeContext.Angebote.Add(angebot);
        await writeContext.SaveChangesAsync();
        return angebot.Id;
    }

    [Fact]
    public async Task AProjectRoundTripsWithEveryField()
    {
        var customerId = await SeedCustomerAsync();
        var angebotId = await SeedAngebotAsync();
        var project = Project.Create(customerId, angebotId, Money.FromExact(25_673.36m));

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Projects.Add(project);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Projects.SingleAsync(p => p.Id == project.Id);

        Assert.Equal(customerId, reloaded.CustomerId);
        Assert.Equal(angebotId, reloaded.AngebotId);
        Assert.Equal(ProjectStatus.Active, reloaded.Status);
        Assert.Equal(Money.FromExact(25_673.36m), reloaded.AgreedTotal);
        Assert.Null(reloaded.CompletedAt);
    }

    /// <summary>
    /// <c>AgreedTotal</c> is a snapshot of a figure the customer has legally agreed to, so a
    /// column type that silently truncated or re-rounded it would corrupt the one value this
    /// aggregate exists to preserve. Two decimal places must survive the round trip exactly.
    /// </summary>
    [Fact]
    public async Task AgreedTotalRoundTripsAtFullPrecision()
    {
        var customerId = await SeedCustomerAsync();
        var angebotId = await SeedAngebotAsync();
        var project = Project.Create(customerId, angebotId, Money.FromExact(12_345.67m));

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Projects.Add(project);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var stored = await readContext.Database
            .SqlQuery<decimal>($"SELECT AgreedTotal AS Value FROM Projects WHERE Id = {project.Id}")
            .SingleAsync();

        Assert.Equal(12_345.67m, stored);
    }

    /// <summary>
    /// Status is stored as its name, not its ordinal — ERD.md's readability reason, and it is why
    /// reordering <see cref="ProjectStatus"/> could never silently reinterpret existing rows. Read
    /// through raw SQL, since EF's own converter would hide an ordinal.
    /// </summary>
    [Fact]
    public async Task StatusIsStoredAsAString()
    {
        var customerId = await SeedCustomerAsync();
        var angebotId = await SeedAngebotAsync();
        var project = Project.Create(customerId, angebotId, Money.FromExact(100m));
        project.PutOnHold();

        await using var context = fixture.CreateContext();
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var stored = await context.Database
            .SqlQuery<string>($"SELECT Status AS Value FROM Projects WHERE Id = {project.Id}")
            .SingleAsync();

        Assert.Equal(nameof(ProjectStatus.OnHold), stored);
    }

    /// <summary>
    /// ERD.md's <c>ANGEBOT ||--o| PROJECT</c>: one Angebot converts to at most one Project. The
    /// Application layer checks this first (D62) so the ordinary case is a 409; this pins that the
    /// database refuses regardless.
    /// </summary>
    [Fact]
    public async Task TwoProjectsCannotShareAnAngebot()
    {
        var angebotId = await SeedAngebotAsync();

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Projects.Add(Project.Create(await SeedCustomerAsync(), angebotId, Money.FromExact(100m)));
            await writeContext.SaveChangesAsync();
        }

        await using var duplicateContext = fixture.CreateContext();
        duplicateContext.Projects.Add(Project.Create(await SeedCustomerAsync(), angebotId, Money.FromExact(200m)));

        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
    }

    /// <summary>
    /// ERD.md §4: "one Customer can have many Projects". <c>CustomerId</c> must therefore not be
    /// unique — pinned so the repeat-customer limitation recorded in ERD.md is not accidentally
    /// baked into the schema, which would make resolving it later a breaking change.
    /// </summary>
    [Fact]
    public async Task OneCustomerCanHaveManyProjects()
    {
        var customerId = await SeedCustomerAsync();

        await using var context = fixture.CreateContext();
        context.Projects.Add(Project.Create(customerId, await SeedAngebotAsync(), Money.FromExact(100m)));
        context.Projects.Add(Project.Create(customerId, await SeedAngebotAsync(), Money.FromExact(200m)));

        await context.SaveChangesAsync();

        var count = await context.Projects.CountAsync(p => p.CustomerId == customerId);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task AProjectReferencingNoRealCustomerIsRejected()
    {
        var angebotId = await SeedAngebotAsync();

        await using var context = fixture.CreateContext();
        context.Projects.Add(Project.Create(999_999_999, angebotId, Money.FromExact(100m)));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task AProjectReferencingNoRealAngebotIsRejected()
    {
        var customerId = await SeedCustomerAsync();

        await using var context = fixture.CreateContext();
        context.Projects.Add(Project.Create(customerId, 999_999_999, Money.FromExact(100m)));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    /// <summary>
    /// <c>Complete()</c> on an entity loaded from the database persists through
    /// <c>SaveChangesAsync</c> alone — there is no <c>UpdateAsync</c> anywhere in this project, so
    /// Phase 8's <c>CompleteProjectCommand</c> will depend entirely on the change tracker seeing
    /// both the status move and <c>CompletedAt</c>.
    /// </summary>
    [Fact]
    public async Task CompleteOnALoadedProjectPersistsViaSaveChangesAlone()
    {
        var project = Project.Create(await SeedCustomerAsync(), await SeedAngebotAsync(), Money.FromExact(100m));
        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Projects.Add(project);
            await writeContext.SaveChangesAsync();
        }

        await using (var mutateContext = fixture.CreateContext())
        {
            var loaded = await mutateContext.Projects.SingleAsync(p => p.Id == project.Id);
            loaded.Complete();
            await mutateContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Projects.SingleAsync(p => p.Id == project.Id);

        Assert.Equal(ProjectStatus.Completed, reloaded.Status);
        Assert.NotNull(reloaded.CompletedAt);
    }
}
