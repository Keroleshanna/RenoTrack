using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence.Queries;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// BR-3's running total, against real LocalDB — the only place the <c>SUM</c> is proved to be
/// genuinely SQL-translatable through <c>MoneyConverter</c>, and the only place the <c>Void</c>
/// exclusion (StateMachine.md §3.3) is proved against real rows rather than a fake.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class ProjectInvoiceBalanceQueriesTests(RenoTrackDbContextFixture fixture)
{
    private async Task<(int ProjectId, int AngebotId)> SeedProjectAsync(decimal agreedTotal)
    {
        var lead = Lead.Create("M. Klein", "0176 1234567", "m.klein@example.com", LeadSource.Phone);
        var user = new ApplicationUser { Name = "Test Admin" };

        await using var context = fixture.CreateContext();
        context.Leads.Add(lead);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var customer = Customer.Create(lead.Id, "M. Klein", "m.klein@example.com", "0176 1234567");
        var angebot = Angebot.Create(lead.Id, null, $"ANG-{Guid.NewGuid():N}"[..18], user.Id);
        context.Customers.Add(customer);
        context.Angebote.Add(angebot);
        await context.SaveChangesAsync();

        var project = Project.Create(customer.Id, angebot.Id, Money.FromExact(agreedTotal));
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        return (project.Id, angebot.Id);
    }

    private async Task AddInvoiceAsync(int projectId, decimal gross, bool voided = false)
    {
        var net = Money.RoundedPerBR11(gross / 1.19m);
        var invoice = Invoice.Create(
            projectId,
            $"RE-{Guid.NewGuid():N}"[..17],
            DateTime.UtcNow.AddDays(14),
            net,
            Money.FromExact(gross) - net,
            Money.FromExact(gross));

        if (voided)
            invoice.Void("Superseded.");

        await using var context = fixture.CreateContext();
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();
    }

    private async Task<Application.Projects.Dtos.ProjectInvoiceBalanceDto?> BalanceAsync(int projectId)
    {
        await using var context = fixture.CreateContext();
        return await new ProjectQueries(context).GetInvoiceBalanceAsync(projectId, CancellationToken.None);
    }

    /// <summary>
    /// A Project with no invoices must report zero invoiced, not null — SQL's <c>SUM</c> over no
    /// rows returns <c>NULL</c>, so the coalesce is load-bearing rather than defensive.
    /// </summary>
    [Fact]
    public async Task AProjectWithNoInvoicesReportsZeroInvoicedAndTheFullRemainder()
    {
        var (projectId, _) = await SeedProjectAsync(25_673.36m);

        var balance = await BalanceAsync(projectId);

        Assert.NotNull(balance);
        Assert.Equal(25_673.36m, balance.AgreedTotal);
        Assert.Equal(0m, balance.AlreadyInvoiced);
        Assert.Equal(25_673.36m, balance.Remaining);
    }

    /// <summary>Sequence Diagram §8's own worked example: 25,673.36 agreed, 8,000 invoiced.</summary>
    [Fact]
    public async Task InvoicesAreSummedAndSubtractedFromTheAgreedTotal()
    {
        var (projectId, _) = await SeedProjectAsync(25_673.36m);
        await AddInvoiceAsync(projectId, 8_000.00m);

        var balance = await BalanceAsync(projectId);

        Assert.Equal(8_000.00m, balance!.AlreadyInvoiced);
        Assert.Equal(17_673.36m, balance.Remaining);
    }

    [Fact]
    public async Task SeveralInvoicesAccumulate()
    {
        var (projectId, _) = await SeedProjectAsync(25_673.36m);
        await AddInvoiceAsync(projectId, 8_000.00m);
        await AddInvoiceAsync(projectId, 10_000.00m);
        await AddInvoiceAsync(projectId, 2_500.50m);

        var balance = await BalanceAsync(projectId);

        Assert.Equal(20_500.50m, balance!.AlreadyInvoiced);
        Assert.Equal(5_172.86m, balance.Remaining);
    }

    /// <summary>
    /// StateMachine.md §3.3: a voided invoice is "excluded from 'remaining balance' math going
    /// forward". Every other status counts — <c>Draft</c> included, since no document excludes it.
    /// </summary>
    [Fact]
    public async Task VoidInvoicesAreExcludedAndEveryOtherStatusCounts()
    {
        var (projectId, _) = await SeedProjectAsync(10_000.00m);
        await AddInvoiceAsync(projectId, 3_000.00m);                 // Draft — counts
        await AddInvoiceAsync(projectId, 5_000.00m, voided: true);   // Void  — excluded

        var balance = await BalanceAsync(projectId);

        Assert.Equal(3_000.00m, balance!.AlreadyInvoiced);
        Assert.Equal(7_000.00m, balance.Remaining);
    }

    /// <summary>
    /// <b>BR-3 warns rather than blocks, so an over-invoiced Project reports a negative
    /// remainder — and that negative is the warning.</b> Clamping it at zero, in SQL or anywhere
    /// else, would delete the only signal BR-3 asks the system to produce.
    /// </summary>
    [Fact]
    public async Task OverInvoicingProducesANegativeRemaining()
    {
        var (projectId, _) = await SeedProjectAsync(10_000.00m);
        await AddInvoiceAsync(projectId, 12_500.00m);

        var balance = await BalanceAsync(projectId);

        Assert.Equal(12_500.00m, balance!.AlreadyInvoiced);
        Assert.Equal(-2_500.00m, balance.Remaining);
    }

    /// <summary>Invoices belonging to another Project must not leak into this one's total.</summary>
    [Fact]
    public async Task InvoicesOfOtherProjectsAreNotCounted()
    {
        var (projectId, _) = await SeedProjectAsync(10_000.00m);
        var (otherProjectId, _) = await SeedProjectAsync(50_000.00m);
        await AddInvoiceAsync(projectId, 1_000.00m);
        await AddInvoiceAsync(otherProjectId, 40_000.00m);

        var balance = await BalanceAsync(projectId);

        Assert.Equal(1_000.00m, balance!.AlreadyInvoiced);
    }

    [Fact]
    public async Task AnUnknownProjectReturnsNull()
    {
        Assert.Null(await BalanceAsync(999_999_999));
    }

    /// <summary>
    /// Full cent precision must survive the SQL <c>SUM</c> — this is a figure an Admin reconciles
    /// against a legally agreed total, so a rounded aggregate would be silent corruption.
    /// </summary>
    [Fact]
    public async Task TheSumKeepsFullCentPrecision()
    {
        var (projectId, _) = await SeedProjectAsync(1_000.00m);
        await AddInvoiceAsync(projectId, 333.33m);
        await AddInvoiceAsync(projectId, 333.33m);
        await AddInvoiceAsync(projectId, 333.33m);

        var balance = await BalanceAsync(projectId);

        Assert.Equal(999.99m, balance!.AlreadyInvoiced);
        Assert.Equal(0.01m, balance.Remaining);
    }
}
