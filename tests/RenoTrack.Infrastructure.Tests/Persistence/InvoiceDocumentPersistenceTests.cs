using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Queries;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Real LocalDB behind Phase 14 Slice 3a (<b>D113</b>): the archived document's key round-trips and
/// is absent for a Draft, <c>Invoices.Status</c> is a concurrency token for every transition — not
/// only Send — and the Project detail read reports a document by a flag, never by its key.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class InvoiceDocumentPersistenceTests(RenoTrackDbContextFixture fixture)
{
    private const string Key = "invoices/0/6f9619ff-8b86-d011-b42d-00c04fc964ff.pdf";

    private async Task<int> SeedProjectAsync()
    {
        await using var context = fixture.CreateContext();

        var lead = Lead.Create("M. Klein", "0176 1234567", "m.klein@example.invalid", LeadSource.Phone);
        context.Leads.Add(lead);
        var user = new ApplicationUser { Name = "Test Admin" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var customer = Customer.Create(lead.Id, "M. Klein", "m.klein@example.invalid", "0176 1234567");
        context.Customers.Add(customer);
        var angebot = Angebot.Create(lead.Id, null, $"ANG-{Guid.NewGuid():N}"[..18], user.Id);
        context.Angebote.Add(angebot);
        await context.SaveChangesAsync();

        var project = Project.Create(customer.Id, angebot.Id, Money.FromExact(25_673.36m));
        context.Projects.Add(project);
        await context.SaveChangesAsync();
        return project.Id;
    }

    private async Task<Invoice> SeedInvoiceAsync(int projectId, bool sent)
    {
        var invoice = TestInvoices.AtStandardRate(projectId, $"RE-{Guid.NewGuid():N}"[..17], DateTime.UtcNow.AddDays(14), 1_190m);

        await using var context = fixture.CreateContext();
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        if (sent)
        {
            invoice.Send(Key);
            await context.SaveChangesAsync();
        }

        return invoice;
    }

    [Fact]
    public async Task TheArchivedDocumentKeyRoundTrips()
    {
        var invoice = await SeedInvoiceAsync(await SeedProjectAsync(), sent: true);

        await using var readContext = fixture.CreateContext();
        var stored = await readContext.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id);

        Assert.Equal(Key, stored.DocumentFileUrl);
    }

    [Fact]
    public async Task ADraftIsStoredWithoutADocumentKey()
    {
        var invoice = await SeedInvoiceAsync(await SeedProjectAsync(), sent: false);

        await using var readContext = fixture.CreateContext();
        var stored = await readContext.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id);

        Assert.Null(stored.DocumentFileUrl);
    }

    /// <summary>The column is as wide as the guard in Invoice.Send — one constant for both.</summary>
    [Fact]
    public async Task AKeyOfTheMaximumLengthIsStoredWhole()
    {
        var invoice = TestInvoices.AtStandardRate(await SeedProjectAsync(), $"RE-{Guid.NewGuid():N}"[..17], DateTime.UtcNow, 1_190m);
        var key = new string('k', Invoice.MaxDocumentFileUrlLength);

        await using (var context = fixture.CreateContext())
        {
            context.Invoices.Add(invoice);
            await context.SaveChangesAsync();
            invoice.Send(key);
            await context.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        Assert.Equal(key, (await readContext.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id)).DocumentFileUrl);
    }

    /// <summary>
    /// The token covers every transition, which is the accepted consequence D113 records: a Void and a
    /// MarkPaid racing on one Sent invoice no longer end with whichever wrote last — the second writer
    /// is refused, and UnitOfWork reports it as the 409 the API maps.
    /// </summary>
    [Fact]
    public async Task ConcurrentPayAndVoid_TheSecondWriterIsAConflict()
    {
        var invoice = await SeedInvoiceAsync(await SeedProjectAsync(), sent: true);

        await using var payContext = fixture.CreateContext();
        await using var voidContext = fixture.CreateContext();
        var toPay = await payContext.Invoices.SingleAsync(i => i.Id == invoice.Id);
        var toVoid = await voidContext.Invoices.SingleAsync(i => i.Id == invoice.Id);

        toPay.MarkPaid(PaymentMethod.BankTransfer, DateTime.UtcNow, recordedByAdminId: 1);
        toVoid.Void("Issued against the wrong Project.");

        await new UnitOfWork(payContext).SaveChangesAsync(CancellationToken.None);
        await Assert.ThrowsAsync<ConflictException>(
            () => new UnitOfWork(voidContext).SaveChangesAsync(CancellationToken.None));

        await using var readContext = fixture.CreateContext();
        var stored = await readContext.Invoices.AsNoTracking().Include(i => i.Payments).SingleAsync(i => i.Id == invoice.Id);
        Assert.Equal(InvoiceStatus.Paid, stored.Status);
        Assert.Null(stored.VoidReason);
        Assert.Single(stored.Payments);
    }

    /// <summary>
    /// Sending a Draft that was voided meanwhile: the guard passed against a stale Draft, and the
    /// token refuses the write rather than resurrecting a void invoice as Sent.
    /// </summary>
    [Fact]
    public async Task SendingAnInvoiceVoidedMeanwhile_IsAConflict()
    {
        var invoice = await SeedInvoiceAsync(await SeedProjectAsync(), sent: false);

        await using var sendContext = fixture.CreateContext();
        await using var voidContext = fixture.CreateContext();
        var toSend = await sendContext.Invoices.SingleAsync(i => i.Id == invoice.Id);
        var toVoid = await voidContext.Invoices.SingleAsync(i => i.Id == invoice.Id);

        toVoid.Void("Not needed after all.");
        await new UnitOfWork(voidContext).SaveChangesAsync(CancellationToken.None);

        toSend.Send(Key);
        await Assert.ThrowsAsync<ConflictException>(
            () => new UnitOfWork(sendContext).SaveChangesAsync(CancellationToken.None));

        await using var readContext = fixture.CreateContext();
        var stored = await readContext.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
        Assert.Equal(InvoiceStatus.Void, stored.Status);
        Assert.Null(stored.DocumentFileUrl);
    }

    /// <summary>
    /// The Project detail read — Inspector-visible — says whether a document exists and nothing more.
    /// </summary>
    [Fact]
    public async Task TheProjectDetailReadReportsADocumentByFlagOnly()
    {
        var projectId = await SeedProjectAsync();
        var draft = await SeedInvoiceAsync(projectId, sent: false);
        var issued = await SeedInvoiceAsync(projectId, sent: true);

        await using var context = fixture.CreateContext();
        var detail = await new ProjectQueries(context).GetByIdAsync(projectId, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.False(detail.Invoices.Single(i => i.Id == draft.Id).HasDocument);
        Assert.True(detail.Invoices.Single(i => i.Id == issued.Id).HasDocument);
    }
}
