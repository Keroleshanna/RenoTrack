using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Queries;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Proves the Project detail projection is genuinely SQL-translatable — the thing a fake cannot
/// establish, and the reason `ICatalogItemQueries` got the same treatment in Phase 3 Slice 9. The
/// three-table join exists because `Project` deliberately holds no navigation property to
/// `Customer` or `Angebot`, so there is nothing to `Include`.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class ProjectQueriesTests(RenoTrackDbContextFixture fixture)
{
    private async Task<(int ProjectId, int LeadId, int AngebotId, string AngebotNumber)> SeedProjectAsync(
        RenoTrackDbContext context,
        ProjectStatus status = ProjectStatus.Active)
    {
        var lead = Lead.Create("M. Klein", "0176 1234567", $"{Guid.NewGuid():N}@example.com", LeadSource.Phone);
        context.Leads.Add(lead);
        await context.SaveChangesAsync();

        var user = new ApplicationUser { Name = "Test Inspector" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var angebotNumber = $"ANG-{Guid.NewGuid():N}"[..18];
        var angebot = Angebot.Create(lead.Id, inspectionId: null, angebotNumber, user.Id);
        context.Angebote.Add(angebot);
        await context.SaveChangesAsync();

        var customer = Customer.Create(lead.Id, "M. Klein", "m.klein@example.com", "0176 1234567");
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var project = Project.Create(customer.Id, angebot.Id, Money.FromExact(25_673.36m));
        if (status == ProjectStatus.OnHold)
        {
            project.PutOnHold();
        }

        context.Projects.Add(project);
        await context.SaveChangesAsync();

        return (project.Id, lead.Id, angebot.Id, angebotNumber);
    }

    [Fact]
    public async Task GetByIdAsyncProjectsEveryFieldAcrossThreeTables()
    {
        await using var context = fixture.CreateContext();
        var seeded = await SeedProjectAsync(context);

        var result = await new ProjectQueries(context).GetByIdAsync(seeded.ProjectId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(seeded.ProjectId, result.Id);
        Assert.Equal(ProjectStatus.Active, result.Status);
        Assert.Equal(25_673.36m, result.AgreedTotal);
        Assert.Null(result.CompletedAt);
        Assert.Equal("M. Klein", result.CustomerName);
        Assert.Equal(seeded.LeadId, result.LeadId);
        Assert.Null(result.InspectionId);
        Assert.Equal(seeded.AngebotId, result.AngebotId);
        Assert.Equal(seeded.AngebotNumber, result.AngebotNumber);
    }

    [Fact]
    public async Task GetByIdAsyncReturnsNullForAnUnknownProject()
    {
        await using var context = fixture.CreateContext();

        var result = await new ProjectQueries(context).GetByIdAsync(999_999_999, CancellationToken.None);

        Assert.Null(result);
    }

    /// <summary>
    /// Status round-trips through the string column and the projection together — the projection
    /// reads the converted property, so a broken converter would surface here as well as in
    /// `ProjectPersistenceTests`.
    /// </summary>
    [Fact]
    public async Task GetByIdAsyncReflectsANonDefaultStatus()
    {
        await using var context = fixture.CreateContext();
        var seeded = await SeedProjectAsync(context, ProjectStatus.OnHold);

        var result = await new ProjectQueries(context).GetByIdAsync(seeded.ProjectId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ProjectStatus.OnHold, result.Status);
    }

    // ---- FR-7.4's invoice portion (Phase 8 Slice 6) -------------------------

    private static async Task<Invoice> AddInvoiceAsync(
        RenoTrackDbContext context,
        int projectId,
        decimal gross,
        DateTime issueDateShift,
        InvoiceStatus status = InvoiceStatus.Draft)
    {
        var net = Money.RoundedPerBR11(gross / 1.19m);
        var invoice = Invoice.Create(
            projectId,
            $"RE-{Guid.NewGuid():N}"[..17],
            DateTime.UtcNow.AddDays(14),
            net,
            Money.FromExact(gross) - net,
            Money.FromExact(gross));

        if (status == InvoiceStatus.Void)
            invoice.Void("Superseded.");

        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        // IssueDate is set at creation and has no mutator, so ordering is exercised by writing the
        // column directly — the alternative would be sleeping, which proves nothing and is slow.
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE Invoices SET IssueDate = {0} WHERE Id = {1}",
            issueDateShift,
            invoice.Id);

        return invoice;
    }

    [Fact]
    public async Task AProjectWithNoInvoicesReportsAnEmptyListAndTheFullRemainder()
    {
        await using var context = fixture.CreateContext();
        var seeded = await SeedProjectAsync(context);

        var result = await new ProjectQueries(context).GetByIdAsync(seeded.ProjectId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.Invoices);
        Assert.Equal(0m, result.AlreadyInvoiced);
        Assert.Equal(25_673.36m, result.Remaining);
    }

    /// <summary>Wireframe E1's own worked example: 25,673.36 agreed, 8,000 invoiced.</summary>
    [Fact]
    public async Task TheInvoiceListAndTheFiguresAreProjectedTogether()
    {
        await using var context = fixture.CreateContext();
        var seeded = await SeedProjectAsync(context);
        var invoice = await AddInvoiceAsync(context, seeded.ProjectId, 8_000.00m, DateTime.UtcNow);

        var result = await new ProjectQueries(context).GetByIdAsync(seeded.ProjectId, CancellationToken.None);

        Assert.NotNull(result);
        var row = Assert.Single(result.Invoices);
        Assert.Equal(invoice.Id, row.Id);
        Assert.Equal(invoice.InvoiceNumber, row.InvoiceNumber);
        Assert.Equal(8_000.00m, row.GrossAmount);
        Assert.Equal(InvoiceStatus.Draft, row.Status);
        Assert.Equal(invoice.DueDate.Date, row.DueDate.Date);
        Assert.Equal(8_000.00m, result.AlreadyInvoiced);
        Assert.Equal(17_673.36m, result.Remaining);
    }

    /// <summary>
    /// K-3, and the one rule most easily broken by "tidying" the projection: a voided Invoice stays
    /// in the list (BR-9 — it is a numbered record, not a deleted one) while leaving the arithmetic
    /// (StateMachine.md §3.3).
    /// </summary>
    [Fact]
    public async Task AVoidInvoiceStaysInTheListButLeavesTheFigures()
    {
        await using var context = fixture.CreateContext();
        var seeded = await SeedProjectAsync(context);
        await AddInvoiceAsync(context, seeded.ProjectId, 8_000.00m, DateTime.UtcNow);
        await AddInvoiceAsync(context, seeded.ProjectId, 5_000.00m, DateTime.UtcNow, InvoiceStatus.Void);

        var result = await new ProjectQueries(context).GetByIdAsync(seeded.ProjectId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result.Invoices.Count);
        Assert.Contains(result.Invoices, row => row.Status == InvoiceStatus.Void);
        Assert.Equal(8_000.00m, result.AlreadyInvoiced);
        Assert.Equal(17_673.36m, result.Remaining);
    }

    /// <summary>
    /// The detail read and the standalone balance endpoint compute the same two figures by
    /// different routes — an in-memory sum over the projected rows here, a SQL <c>SUM</c> there.
    /// FR-7.4 requires both to exist, so this is what stops them drifting apart.
    /// </summary>
    [Fact]
    public async Task TheDetailFiguresAgreeWithTheStandaloneBalanceEndpoint()
    {
        await using var context = fixture.CreateContext();
        var seeded = await SeedProjectAsync(context);
        await AddInvoiceAsync(context, seeded.ProjectId, 8_000.00m, DateTime.UtcNow);
        await AddInvoiceAsync(context, seeded.ProjectId, 12_345.67m, DateTime.UtcNow);
        await AddInvoiceAsync(context, seeded.ProjectId, 5_000.00m, DateTime.UtcNow, InvoiceStatus.Void);

        var queries = new ProjectQueries(context);
        var detail = await queries.GetByIdAsync(seeded.ProjectId, CancellationToken.None);
        var balance = await queries.GetInvoiceBalanceAsync(seeded.ProjectId, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.NotNull(balance);
        Assert.Equal(balance.AlreadyInvoiced, detail.AlreadyInvoiced);
        Assert.Equal(balance.Remaining, detail.Remaining);
        Assert.Equal(balance.AgreedTotal, detail.AgreedTotal);
    }

    /// <summary>
    /// BR-3 warns rather than blocks, so an over-invoiced Project reports a negative remainder on
    /// the detail read exactly as it does on the balance read. Never clamped.
    /// </summary>
    [Fact]
    public async Task OverInvoicingLeavesANegativeRemainderOnTheDetailRead()
    {
        await using var context = fixture.CreateContext();
        var seeded = await SeedProjectAsync(context);
        await AddInvoiceAsync(context, seeded.ProjectId, 30_000.00m, DateTime.UtcNow);

        var result = await new ProjectQueries(context).GetByIdAsync(seeded.ProjectId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(-4_326.64m, result.Remaining);
    }

    /// <summary>
    /// Ordered <c>IssueDate</c> then <c>Id</c>. The two invoices sharing a date are what make the
    /// tiebreaker load-bearing — without it their relative order is whatever SQL Server happens to
    /// return.
    /// </summary>
    [Fact]
    public async Task InvoicesAreOrderedByIssueDateThenId()
    {
        await using var context = fixture.CreateContext();
        var seeded = await SeedProjectAsync(context);
        var sameDay = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        var newest = await AddInvoiceAsync(context, seeded.ProjectId, 1_000m, new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        var firstOfTheDay = await AddInvoiceAsync(context, seeded.ProjectId, 2_000m, sameDay);
        var secondOfTheDay = await AddInvoiceAsync(context, seeded.ProjectId, 3_000m, sameDay);

        var result = await new ProjectQueries(context).GetByIdAsync(seeded.ProjectId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(
            [firstOfTheDay.Id, secondOfTheDay.Id, newest.Id],
            result.Invoices.Select(row => row.Id).ToArray());
    }

    /// <summary>Another Project's Invoices never appear on this one's page.</summary>
    [Fact]
    public async Task InvoicesAreIsolatedBetweenProjects()
    {
        await using var context = fixture.CreateContext();
        var mine = await SeedProjectAsync(context);
        var theirs = await SeedProjectAsync(context);
        await AddInvoiceAsync(context, mine.ProjectId, 8_000.00m, DateTime.UtcNow);
        await AddInvoiceAsync(context, theirs.ProjectId, 999.00m, DateTime.UtcNow);

        var result = await new ProjectQueries(context).GetByIdAsync(mine.ProjectId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(8_000.00m, Assert.Single(result.Invoices).GrossAmount);
        Assert.Equal(8_000.00m, result.AlreadyInvoiced);
    }
}
