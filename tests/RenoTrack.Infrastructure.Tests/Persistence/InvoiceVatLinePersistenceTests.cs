using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Documents;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence.Repositories;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Phase 14 Slice 2 (D111), against real LocalDB (D40): the per-rate VAT lines the Invoice
/// aggregate calculates survive storage exactly, are always loaded with their Invoice, and are
/// guarded by the database as well as the Domain — and a row from before this slice, which has no
/// description and no lines, still loads and is refused as a document rather than printed around.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class InvoiceVatLinePersistenceTests(RenoTrackDbContextFixture fixture)
{
    /// <summary>
    /// Seeds a real Lead → Angebot (7 % and 19 % items) → Customer → Project chain and returns the
    /// Project id plus the Angebot's own rate mix — the value the handler hands Invoice.Create.
    /// </summary>
    private async Task<(int ProjectId, int CustomerId, IReadOnlyList<VatBreakdownLine> RateMix)> SeedMixedRateProjectAsync()
    {
        await using var context = fixture.CreateContext();

        var lead = Lead.Create("M. Klein", "0176 1234567", "m.klein@example.com", LeadSource.Phone);
        var user = new ApplicationUser { Name = "Test Inspector" };
        context.Leads.Add(lead);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var angebot = Angebot.Create(lead.Id, null, $"ANG-{Guid.NewGuid():N}"[..18], user.Id);
        var section = angebot.AddSection("Pos. 1", 1);
        angebot.AddItemToSection(section, "Material", 1m, ItemUnit.Piece(), Money.FromExact(1_000.00m), VatRate.Reduced);
        angebot.AddItemToSection(section, "Arbeit", 1m, ItemUnit.Piece(), Money.FromExact(5_000.00m), VatRate.Standard);
        var customer = Customer.Create(lead.Id, "M. Klein", "m.klein@example.com", "0176 1234567", "Musterstraße 1");
        context.Angebote.Add(angebot);
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var project = Project.Create(customer.Id, angebot.Id, angebot.GrossTotal);
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        return (project.Id, customer.Id, angebot.VatBreakdown);
    }

    private static Invoice NewMixedRateInvoice(int projectId, IReadOnlyList<VatBreakdownLine> rateMix) =>
        Invoice.Create(
            projectId,
            $"RE-{Guid.NewGuid():N}"[..17],
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(14),
            Money.FromExact(3_510.00m),
            rateMix,
            "Abschlag 1: Malerarbeiten Erdgeschoss",
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30));

    private async Task<Invoice> PersistAsync(Invoice invoice)
    {
        await using var context = fixture.CreateContext();
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();
        return invoice;
    }

    [Fact]
    public async Task TheVatLinesDescriptionAndServicePeriodRoundTrip()
    {
        var (projectId, _, rateMix) = await SeedMixedRateProjectAsync();
        var invoice = await PersistAsync(NewMixedRateInvoice(projectId, rateMix));

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Invoices.SingleAsync(i => i.Id == invoice.Id);

        Assert.Equal("Abschlag 1: Malerarbeiten Erdgeschoss", reloaded.Description);
        Assert.Equal(new DateOnly(2026, 9, 1), reloaded.ServicePeriodStart);
        Assert.Equal(new DateOnly(2026, 9, 30), reloaded.ServicePeriodEnd);
        Assert.Equal(
            invoice.VatLines.Select(l => (l.Rate, l.NetAmount, l.VatAmount)),
            reloaded.VatLines.OrderBy(l => l.Rate).Select(l => (l.Rate, l.NetAmount, l.VatAmount)));
        Assert.Equal(2, reloaded.VatLines.Count);
    }

    /// <summary>
    /// Owned lines load with their Invoice without an Include anyone could forget — through the
    /// repository every handler uses, which is the read Slice 3's document assembly will rely on.
    /// </summary>
    [Fact]
    public async Task TheRepositoryLoadsTheLinesWithTheInvoice()
    {
        var (projectId, _, rateMix) = await SeedMixedRateProjectAsync();
        var invoice = await PersistAsync(NewMixedRateInvoice(projectId, rateMix));

        await using var readContext = fixture.CreateContext();
        var reloaded = await new InvoiceRepository(readContext).GetByIdAsync(invoice.Id, CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal([VatRate.Reduced, VatRate.Standard], reloaded.VatLines.Select(l => l.Rate).Order().ToArray());
        Assert.Equal(reloaded.NetAmount, Money.Sum(reloaded.VatLines.Select(l => l.NetAmount)));
        Assert.Equal(reloaded.VatAmount, Money.Sum(reloaded.VatLines.Select(l => l.VatAmount)));
    }

    /// <summary>
    /// D111 Part 6, against the real column: <c>datetime2</c> keeps no kind, so the issue instant
    /// comes back <see cref="DateTimeKind.Unspecified"/> — with the same value. Read through the
    /// invoice calendar it gives the same Berlin date as the instant that was written: 23:30 UTC on
    /// New Year's Eve is 1 January either way.
    /// </summary>
    [Fact]
    public async Task AStoredIssueInstantReadsBackAsTheSameBerlinDate()
    {
        var (projectId, _, rateMix) = await SeedMixedRateProjectAsync();
        var issuedAt = new DateTime(2026, 12, 31, 23, 30, 0, DateTimeKind.Utc);
        var invoice = await PersistAsync(Invoice.Create(
            projectId, $"RE-{Guid.NewGuid():N}"[..17], issuedAt, DateTime.UtcNow,
            Money.FromExact(100.00m), rateMix, "Abschlag 1", null, null));

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Invoices.SingleAsync(i => i.Id == invoice.Id);
        var calendar = InvoiceCalendar.ForEuropeBerlin();

        Assert.Equal(DateTimeKind.Unspecified, reloaded.IssueDate.Kind);
        Assert.Equal(issuedAt.Ticks, reloaded.IssueDate.Ticks);
        Assert.Equal(new DateOnly(2027, 1, 1), calendar.DateOf(reloaded.IssueDate));
        Assert.Equal(calendar.DateOf(issuedAt), calendar.DateOf(reloaded.IssueDate));
    }

    /// <summary>
    /// A description of exactly <see cref="Invoice.MaxDescriptionLength"/> characters fits the column,
    /// so the Domain's limit and the schema's agree.
    /// </summary>
    [Fact]
    public async Task ADescriptionOfTheMaximumLengthFitsTheColumn()
    {
        var (projectId, _, rateMix) = await SeedMixedRateProjectAsync();
        var text = new string('ä', Invoice.MaxDescriptionLength);
        var invoice = await PersistAsync(Invoice.Create(
            projectId, $"RE-{Guid.NewGuid():N}"[..17], DateTime.UtcNow, DateTime.UtcNow, Money.FromExact(100.00m), rateMix, text, null, null));

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Invoices.SingleAsync(i => i.Id == invoice.Id);

        Assert.Equal(text, reloaded.Description);
    }

    /// <summary>
    /// The database half of "one line per rate" (D111). Written with raw SQL on purpose — the Domain
    /// cannot produce a duplicate, and this proves the constraint exists in the real schema rather
    /// than only in the model. SQL Server error 2601 is a unique-index violation.
    /// </summary>
    [Fact]
    public async Task TheDatabaseRefusesASecondLineAtTheSameRate()
    {
        var (projectId, _, rateMix) = await SeedMixedRateProjectAsync();
        var invoice = await PersistAsync(NewMixedRateInvoice(projectId, rateMix));
        var standardRate = (int)VatRate.Standard;

        await using var context = fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO InvoiceVatLines (InvoiceId, Rate, NetAmount, VatAmount) VALUES ({invoice.Id}, {standardRate}, 1.00, 0.19)"));

        Assert.Equal(2601, ex.Number);
    }

    /// <summary>
    /// A different rate on the same invoice is not a duplicate — the index is on the pair, not the
    /// rate alone, which is what lets two invoices both carry 19 %.
    /// </summary>
    [Fact]
    public async Task TwoInvoicesMayEachCarryTheSameRate()
    {
        var (projectId, _, rateMix) = await SeedMixedRateProjectAsync();

        await PersistAsync(NewMixedRateInvoice(projectId, rateMix));
        var second = await PersistAsync(NewMixedRateInvoice(projectId, rateMix));

        Assert.NotEqual(0, second.Id);
    }

    /// <summary>
    /// What migration #14 leaves behind for an invoice created before it: an empty description and
    /// no VAT lines. Simulated exactly with raw SQL. The row still loads — the constructor carries no
    /// guard that would refuse it (CLAUDE.md §2) — and the document assembler refuses it, naming both
    /// gaps, rather than inventing a description or recomputing a split from today's Angebot.
    /// </summary>
    [Fact]
    public async Task AHistoricalRowLoadsAndIsRefusedAsADocument()
    {
        var (projectId, customerId, rateMix) = await SeedMixedRateProjectAsync();
        var invoice = await PersistAsync(NewMixedRateInvoice(projectId, rateMix));

        await using (var context = fixture.CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM InvoiceVatLines WHERE InvoiceId = {invoice.Id}");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Invoices SET Description = '', ServicePeriodStart = NULL, ServicePeriodEnd = NULL WHERE Id = {invoice.Id}");
        }

        await using var readContext = fixture.CreateContext();
        var historical = await readContext.Invoices.SingleAsync(i => i.Id == invoice.Id);
        var customer = await readContext.Customers.SingleAsync(c => c.Id == customerId);

        Assert.Equal(string.Empty, historical.Description);
        Assert.Empty(historical.VatLines);

        var ex = Assert.Throws<InvalidOperationException>(
            () => InvoiceDocumentFactory.Create(historical, customer, InvoiceCalendar.ForEuropeBerlin()));
        Assert.Contains("the invoice description", ex.Message);
        Assert.Contains("the invoice's VAT lines", ex.Message);
    }
}
