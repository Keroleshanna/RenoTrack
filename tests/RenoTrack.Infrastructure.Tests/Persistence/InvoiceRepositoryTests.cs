using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence.Repositories;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// <c>InvoiceRepository</c> against real LocalDB (D40). The point of interest is CLAUDE.md §4's
/// "a repository returns the full aggregate": <c>GetByIdAsync</c> must eagerly load
/// <c>Payments</c>, and a mutation made on a loaded Invoice must persist through
/// <c>SaveChangesAsync</c> alone — there is no <c>UpdateAsync</c> anywhere in this project, so
/// <c>SendInvoiceCommand</c> depends entirely on the change tracker.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class InvoiceRepositoryTests(RenoTrackDbContextFixture fixture)
{
    private async Task<(int ProjectId, int AdminId)> SeedProjectAsync()
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

        var project = Project.Create(customer.Id, angebot.Id, Money.FromExact(25_673.36m));
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        return (project.Id, user.Id);
    }

    private static Invoice NewInvoice(int projectId) => Invoice.Create(
        projectId,
        $"RE-{Guid.NewGuid():N}"[..17],
        DateTime.UtcNow.AddDays(14),
        Money.FromExact(6_722.69m),
        Money.FromExact(1_277.31m),
        Money.FromExact(8_000.00m));

    [Fact]
    public async Task GetByIdAsync_ReturnsThePersistedInvoice()
    {
        var (projectId, _) = await SeedProjectAsync();
        var invoice = NewInvoice(projectId);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Invoices.Add(invoice);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var loaded = await new InvoiceRepository(readContext).GetByIdAsync(invoice.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(invoice.InvoiceNumber, loaded.InvoiceNumber);
        Assert.Equal(Money.FromExact(8_000.00m), loaded.GrossAmount);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNullForAnUnknownId()
    {
        await using var context = fixture.CreateContext();

        Assert.Null(await new InvoiceRepository(context).GetByIdAsync(999_999_999, CancellationToken.None));
    }

    /// <summary>
    /// CLAUDE.md §4: there is no partial-load contract for an aggregate root. Nothing in Slice 4
    /// reads a Payment, but the contract is the contract — and Slice 5's mark-paid will depend on it.
    /// </summary>
    [Fact]
    public async Task GetByIdAsync_EagerlyLoadsPayments()
    {
        var (projectId, adminId) = await SeedProjectAsync();
        var invoice = NewInvoice(projectId);
        invoice.Send();
        invoice.MarkPaid(PaymentMethod.BankTransfer, DateTime.UtcNow, adminId);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Invoices.Add(invoice);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var loaded = await new InvoiceRepository(readContext).GetByIdAsync(invoice.Id, CancellationToken.None);

        var payment = Assert.Single(loaded!.Payments);
        Assert.Equal(Money.FromExact(8_000.00m), payment.Amount);
    }

    /// <summary>
    /// <c>Send()</c> on an Invoice loaded through this repository persists through
    /// <c>SaveChangesAsync</c> alone — the tracked result is what makes `SendInvoiceCommandHandler`
    /// work without an <c>UpdateAsync</c> that does not exist.
    /// </summary>
    [Fact]
    public async Task AMutationOnALoadedInvoicePersistsViaSaveChangesAlone()
    {
        var (projectId, _) = await SeedProjectAsync();
        var invoice = NewInvoice(projectId);

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Invoices.Add(invoice);
            await writeContext.SaveChangesAsync();
        }

        await using (var mutateContext = fixture.CreateContext())
        {
            var loaded = await new InvoiceRepository(mutateContext).GetByIdAsync(invoice.Id, CancellationToken.None);
            loaded!.Send();
            await mutateContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await new InvoiceRepository(readContext).GetByIdAsync(invoice.Id, CancellationToken.None);

        Assert.Equal(InvoiceStatus.Sent, reloaded!.Status);
    }

    /// <summary>
    /// <c>AddAsync</c> never commits — that stays exclusively <c>IUnitOfWork</c>'s job, the same
    /// contract every other repository in this project holds.
    /// </summary>
    [Fact]
    public async Task AddAsync_DoesNotCommit()
    {
        var (projectId, _) = await SeedProjectAsync();

        await using (var context = fixture.CreateContext())
        {
            await new InvoiceRepository(context).AddAsync(NewInvoice(projectId), CancellationToken.None);
        }

        await using var readContext = fixture.CreateContext();
        Assert.Empty(readContext.Invoices.Where(i => i.ProjectId == projectId));
    }
}
