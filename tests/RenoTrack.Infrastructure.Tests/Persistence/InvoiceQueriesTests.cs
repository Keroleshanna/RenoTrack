using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence.Queries;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Proves the Phase 10 Invoice reads against real LocalDB.
/// </summary>
/// <remarks>
/// <para>
/// <b>This class exists because the receivables query is the riskiest SQL in the codebase.</b> It is
/// a set of conditional <c>SUM</c>/<c>COUNT</c> aggregates composed over a filtered sub-query inside
/// a <c>GroupBy</c> projection, and every amount goes through <c>MoneyConverter</c>. Whether EF Core
/// can translate that at all is a question only a real database answers — the InMemory provider
/// would happily evaluate the whole thing in C# and prove nothing (D40).
/// </para>
/// <para>
/// Every test seeds its own Project, so the shared database's other rows cannot affect a per-project
/// assertion. The whole-book receivables figures are asserted as <em>deltas</em> around the test's
/// own rows for the same reason.
/// </para>
/// </remarks>
[Collection("Infrastructure Database")]
public sealed class InvoiceQueriesTests(RenoTrackDbContextFixture fixture)
{
    private static string NextNumber() => $"RE-Q-{Guid.NewGuid():N}"[..17];

    private async Task<int> SeedAdminAsync()
    {
        var user = new ApplicationUser { Name = "Receivables Admin" };
        await using var context = fixture.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Seeds Lead → Customer → Angebot → Project, the chain an Invoice hangs from.</summary>
    private async Task<(int ProjectId, string CustomerName)> SeedProjectAsync()
    {
        var inspectorId = await SeedAdminAsync();
        var customerName = $"Kunde {Guid.NewGuid():N}"[..14];

        await using var context = fixture.CreateContext();

        var lead = Lead.Create("Rechnungstest", "0221 000", "invoice@example.de", LeadSource.Website);
        context.Leads.Add(lead);
        await context.SaveChangesAsync();

        var customer = Customer.Create(lead.Id, customerName, "invoice@example.de", "0221 000");
        context.Customers.Add(customer);

        var angebot = Angebot.Create(lead.Id, null, NextNumber(), inspectorId);
        context.Angebote.Add(angebot);
        await context.SaveChangesAsync();

        var project = Project.Create(customer.Id, angebot.Id, Money.FromExact(100_000m));
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        return (project.Id, customerName);
    }

    private async Task<Invoice> AddInvoiceAsync(
        int projectId,
        decimal gross,
        DateTime dueDate,
        Action<Invoice>? transition = null)
    {
        await using var context = fixture.CreateContext();

        var net = Money.FromExact(Math.Round(gross / 1.19m, 2));
        var vat = Money.FromExact(gross - net.Amount);

        var invoice = Invoice.Create(projectId, NextNumber(), dueDate, net, vat, Money.FromExact(gross));
        transition?.Invoke(invoice);

        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();
        return invoice;
    }

    // ---- The list --------------------------------------------------------------------------------

    /// <summary>
    /// The join to Project → Customer has to survive translation, and the customer's name has to
    /// arrive — that name is the whole reason this DTO exists rather than reusing <c>InvoiceDto</c>.
    /// </summary>
    [Fact]
    public async Task Projects_the_customer_name_through_two_joins()
    {
        var (projectId, customerName) = await SeedProjectAsync();
        await AddInvoiceAsync(projectId, 1_190m, DateTime.UtcNow.Date.AddDays(14));

        await using var context = fixture.CreateContext();
        var result = await new InvoiceQueries(context)
            .GetPagedAsync(null, projectId, null, 1, 25, default);

        var row = Assert.Single(result.Items);
        Assert.Equal(customerName, row.CustomerName);
        Assert.Equal(1_190m, row.GrossAmount);
        Assert.Equal(projectId, row.ProjectId);
    }

    /// <summary>
    /// <c>PaidAt</c> has no column — it is the latest <c>Payment</c>'s date. If that projection ever
    /// stops translating, this is where it surfaces.
    /// </summary>
    [Fact]
    public async Task Derives_the_paid_date_from_the_payment_child()
    {
        var (projectId, _) = await SeedProjectAsync();
        var adminId = await SeedAdminAsync();
        var paidAt = DateTime.UtcNow.Date.AddDays(-3);

        await AddInvoiceAsync(projectId, 2_380m, DateTime.UtcNow.Date.AddDays(7), invoice =>
        {
            invoice.Send();
            invoice.MarkPaid(PaymentMethod.BankTransfer, paidAt, adminId);
        });

        await using var context = fixture.CreateContext();
        var result = await new InvoiceQueries(context)
            .GetPagedAsync(null, projectId, null, 1, 25, default);

        var row = Assert.Single(result.Items);
        Assert.Equal(InvoiceStatus.Paid, row.Status);
        Assert.Equal(paidAt, row.PaidAt);
    }

    /// <summary>An unpaid invoice has no payment row, so the date must be null rather than default.</summary>
    [Fact]
    public async Task Leaves_the_paid_date_null_while_nothing_has_been_paid()
    {
        var (projectId, _) = await SeedProjectAsync();
        await AddInvoiceAsync(projectId, 500m, DateTime.UtcNow.Date.AddDays(30));

        await using var context = fixture.CreateContext();
        var result = await new InvoiceQueries(context)
            .GetPagedAsync(null, projectId, null, 1, 25, default);

        Assert.Null(Assert.Single(result.Items).PaidAt);
    }

    [Fact]
    public async Task Orders_by_due_date_so_the_most_overdue_money_leads()
    {
        var (projectId, _) = await SeedProjectAsync();
        var today = DateTime.UtcNow.Date;

        await AddInvoiceAsync(projectId, 100m, today.AddDays(30));
        await AddInvoiceAsync(projectId, 200m, today.AddDays(-10));
        await AddInvoiceAsync(projectId, 300m, today.AddDays(5));

        await using var context = fixture.CreateContext();
        var result = await new InvoiceQueries(context)
            .GetPagedAsync(null, projectId, null, 1, 25, default);

        Assert.Equal([200m, 300m, 100m], result.Items.Select(i => i.GrossAmount));
    }

    [Fact]
    public async Task Filters_by_status_and_by_due_date()
    {
        var (projectId, _) = await SeedProjectAsync();
        var today = DateTime.UtcNow.Date;

        await AddInvoiceAsync(projectId, 100m, today.AddDays(-5), invoice => invoice.Send());
        await AddInvoiceAsync(projectId, 200m, today.AddDays(60));

        await using var context = fixture.CreateContext();
        var queries = new InvoiceQueries(context);

        var sent = await queries.GetPagedAsync(InvoiceStatus.Sent, projectId, null, 1, 25, default);
        Assert.Equal(100m, Assert.Single(sent.Items).GrossAmount);

        var dueSoon = await queries.GetPagedAsync(null, projectId, today, 1, 25, default);
        Assert.Equal(100m, Assert.Single(dueSoon.Items).GrossAmount);
    }

    /// <summary>TotalCount describes the filtered set, not the page — the client pages on it.</summary>
    [Fact]
    public async Task Counts_the_whole_filtered_set_rather_than_the_page()
    {
        var (projectId, _) = await SeedProjectAsync();
        var today = DateTime.UtcNow.Date;

        for (var i = 0; i < 3; i++)
        {
            await AddInvoiceAsync(projectId, 100m + i, today.AddDays(i));
        }

        await using var context = fixture.CreateContext();
        var page = await new InvoiceQueries(context)
            .GetPagedAsync(null, projectId, null, 1, 2, default);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(3, page.TotalCount);
    }

    // ---- Receivables -----------------------------------------------------------------------------

    /// <summary>
    /// The whole point of this class. Asserted as deltas, because the figures are company-wide and
    /// the database is shared across the collection.
    /// </summary>
    [Fact]
    public async Task Aggregates_paid_open_and_overdue_across_the_invoice_book()
    {
        var today = DateTime.UtcNow.Date;
        var adminId = await SeedAdminAsync();

        await using (var before = fixture.CreateContext())
        {
            var baseline = await new InvoiceQueries(before).GetReceivablesAsync(today, default);

            var (projectId, _) = await SeedProjectAsync();

            // Paid: counts toward invoiced and paid, never toward open.
            await AddInvoiceAsync(projectId, 1_000m, today.AddDays(-20), invoice =>
            {
                invoice.Send();
                invoice.MarkPaid(PaymentMethod.BankTransfer, today.AddDays(-15), adminId);
            });

            // Sent and past due: invoiced, open AND overdue.
            await AddInvoiceAsync(projectId, 400m, today.AddDays(-1), invoice => invoice.Send());

            // Sent but not yet due: invoiced and open, never overdue.
            await AddInvoiceAsync(projectId, 250m, today.AddDays(10), invoice => invoice.Send());

            await using var after = fixture.CreateContext();
            var actual = await new InvoiceQueries(after).GetReceivablesAsync(today, default);

            Assert.Equal(1_650m, actual.InvoicedGross - baseline.InvoicedGross);
            Assert.Equal(1_000m, actual.PaidGross - baseline.PaidGross);
            Assert.Equal(650m, actual.OpenGross - baseline.OpenGross);
            Assert.Equal(400m, actual.OverdueGross - baseline.OverdueGross);
            Assert.Equal(3, actual.InvoiceCount - baseline.InvoiceCount);
            Assert.Equal(2, actual.OpenCount - baseline.OpenCount);
            Assert.Equal(1, actual.OverdueCount - baseline.OverdueCount);
        }
    }

    /// <summary>
    /// BR-9 keeps a voided invoice for its number, not as money owed. Counting one as outstanding
    /// would invent a receivable the company has explicitly cancelled.
    /// </summary>
    [Fact]
    public async Task Excludes_voided_invoices_from_every_figure_but_their_own()
    {
        var today = DateTime.UtcNow.Date;

        await using var before = fixture.CreateContext();
        var baseline = await new InvoiceQueries(before).GetReceivablesAsync(today, default);

        var (projectId, _) = await SeedProjectAsync();
        await AddInvoiceAsync(projectId, 900m, today.AddDays(-30), invoice =>
        {
            invoice.Send();
            invoice.Void("Doppelt erfasst");
        });

        await using var after = fixture.CreateContext();
        var actual = await new InvoiceQueries(after).GetReceivablesAsync(today, default);

        Assert.Equal(0m, actual.InvoicedGross - baseline.InvoicedGross);
        Assert.Equal(0m, actual.OpenGross - baseline.OpenGross);
        Assert.Equal(0m, actual.OverdueGross - baseline.OverdueGross);
        Assert.Equal(0, actual.InvoiceCount - baseline.InvoiceCount);

        // Reported separately, so the totals still reconcile against the raw book.
        Assert.Equal(900m, actual.VoidedGross - baseline.VoidedGross);
    }

    /// <summary>
    /// "Overdue" is judged against the caller's date, never a server clock — so the same data must
    /// produce a different answer for a different <c>asOf</c>.
    /// </summary>
    [Fact]
    public async Task Judges_overdue_against_the_supplied_date()
    {
        var today = DateTime.UtcNow.Date;
        var dueDate = today.AddDays(5);

        var (projectId, _) = await SeedProjectAsync();
        await AddInvoiceAsync(projectId, 750m, dueDate, invoice => invoice.Send());

        await using var context = fixture.CreateContext();
        var queries = new InvoiceQueries(context);

        var beforeDue = await queries.GetReceivablesAsync(today, default);
        var afterDue = await queries.GetReceivablesAsync(dueDate.AddDays(1), default);

        Assert.Equal(750m, afterDue.OverdueGross - beforeDue.OverdueGross);
    }
}
