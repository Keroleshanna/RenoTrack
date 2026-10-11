using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RenoTrack.Application.Common;
using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Application.Invoices.Commands.SendInvoice;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Documents;
using RenoTrack.Infrastructure.Email;
using RenoTrack.Infrastructure.FileStorage;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Repositories;
using RenoTrack.Infrastructure.TokenLinks;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Two Admins send the same Draft invoice at the same moment (Phase 14 Slice 3a, <b>D113</b>). Run
/// end to end through the real <see cref="SendInvoiceCommandHandler"/>, real repositories, the real
/// <see cref="UnitOfWork"/>, real LocalDB, real disk storage and the real PDF renderer — because the
/// claim is about all of them together: exactly one send wins, the loser is a 409, and the loser's
/// freshly written PDF is removed, so a race can never leave two archived documents or two links.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the race is staged rather than hoped for.</b> Each send is held, after it has passed the
/// <c>Draft</c> guard and written its document, until both have reached that point; only then are
/// both released to commit. That makes "both contenders loaded the invoice as Draft" a property of
/// the test's structure, which is exactly the case <c>Invoices.Status</c>'s concurrency token exists
/// for (CLAUDE.md §21: the question is what happens when two callers pass the guard at once). An
/// unstaged race would mostly serialise itself and pass even without the token.
/// </para>
/// <para>
/// <b>Repeated, not run once</b> (CLAUDE.md §14, D55): every test loops over several fresh invoices,
/// and the theory runs the whole race more than once.
/// </para>
/// </remarks>
[Collection("Infrastructure Database")]
public sealed class InvoiceSendConcurrencyTests(RenoTrackDbContextFixture fixture) : IDisposable
{
    private const int RaceRepetitions = 5;

    private readonly string _storageRoot =
        Path.Combine(Path.GetTempPath(), "RenoTrackSendRace", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    private static CompanyLegalIdentityOptions FictionalIdentity() => new()
    {
        // Fictional, as every fixture in this repository is (D100).
        LegalName = "Musterbetrieb Beispiel GmbH (Testdaten)",
        StreetAddress = "Teststraße 1",
        PostalCode = "00000",
        Locality = "Testort",
        TaxNumber = "00/000/00000",
    };

    private async Task<int> SeedDraftInvoiceAsync()
    {
        await using var context = fixture.CreateContext();

        var lead = Lead.Create("M. Klein", "0176 1234567", "m.klein@example.invalid", LeadSource.Phone);
        context.Leads.Add(lead);
        var user = new ApplicationUser { Name = "Test Admin" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var customer = Customer.Create(lead.Id, "M. Klein", "m.klein@example.invalid", "0176 1234567", "Teststraße 2\n00000 Testort");
        context.Customers.Add(customer);
        var angebot = Angebot.Create(lead.Id, null, $"ANG-{Guid.NewGuid():N}"[..18], user.Id);
        context.Angebote.Add(angebot);
        await context.SaveChangesAsync();

        var project = Project.Create(customer.Id, angebot.Id, Money.FromExact(25_673.36m));
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var invoice = TestInvoices.AtStandardRate(project.Id, $"RE-{Guid.NewGuid():N}"[..17], DateTime.UtcNow.AddDays(14), 1_190m);
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        return invoice.Id;
    }

    private SendInvoiceCommandHandler HandlerOn(RenoTrackDbContext context, IFileStorage storage) =>
        new(
            new SendInvoiceCommandValidator(),
            new InvoiceRepository(context),
            new ProjectRepository(context),
            new CustomerRepository(context),
            new TokenLinkRepository(context),
            new TokenLinkService(new TokenLinkOptions { LifetimeDays = 30 }),
            new MigraDocPdfGenerator(FictionalIdentity()),
            storage,
            InvoiceCalendar.ForEuropeBerlin(),
            new UnitOfWork(context),
            new AuditService(context, NullLogger<AuditService>.Instance),
            new LoggingNoOpEmailSender(NullLogger<LoggingNoOpEmailSender>.Instance),
            NullLogger<SendInvoiceCommandHandler>.Instance);

    private string[] ArchivedFilesFor(int invoiceId)
    {
        var directory = Path.Combine(_storageRoot, "invoices", invoiceId.ToString());
        return Directory.Exists(directory) ? Directory.GetFiles(directory) : [];
    }

    /// <summary>Runs two sends of one invoice, both held after their write until both have written.</summary>
    private async Task<(Exception? First, Exception? Second)> RaceTwoSendsAsync(int invoiceId)
    {
        var disk = new LocalDiskFileStorage(new FileStorageOptions { RootPath = _storageRoot });
        var bothWritten = new CountdownGate(participants: 2);

        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();

        var first = AttemptAsync(HandlerOn(firstContext, new GatedFileStorage(disk, bothWritten)), invoiceId);
        var second = AttemptAsync(HandlerOn(secondContext, new GatedFileStorage(disk, bothWritten)), invoiceId);

        var outcomes = await Task.WhenAll(first, second);
        return (outcomes[0], outcomes[1]);
    }

    private static async Task<Exception?> AttemptAsync(SendInvoiceCommandHandler handler, int invoiceId)
    {
        try
        {
            await handler.HandleAsync(new SendInvoiceCommand(invoiceId, SentByAdminId: 1), CancellationToken.None);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task TwoConcurrentSends_ExactlyOneWins_TheLoserIsAConflict_AndOneDocumentRemains(int run)
    {
        Assert.InRange(run, 1, 3);

        for (var repetition = 0; repetition < RaceRepetitions; repetition++)
        {
            var invoiceId = await SeedDraftInvoiceAsync();

            var (first, second) = await RaceTwoSendsAsync(invoiceId);

            // Exactly one winner and one loser, and the loser is the 409 the API maps.
            var failures = new[] { first, second }.Where(outcome => outcome is not null).ToList();
            Assert.Single(failures);
            Assert.IsType<ConflictException>(failures[0]);

            await using var readContext = fixture.CreateContext();
            var stored = await readContext.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
            Assert.Equal(InvoiceStatus.Sent, stored.Status);
            Assert.NotNull(stored.DocumentFileUrl);

            // One archived document — the winner's, the one the row points at.
            var archived = Assert.Single(ArchivedFilesFor(invoiceId));
            Assert.Equal(
                Path.GetFullPath(Path.Combine(_storageRoot, stored.DocumentFileUrl!)),
                Path.GetFullPath(archived));

            // One credential: the loser's token link rolled back with its batch.
            Assert.Equal(1, await readContext.TokenLinks.CountAsync(
                t => t.EntityType == TokenLinkEntityType.Invoice && t.EntityId == invoiceId));

            // And the surviving file is a real PDF, not a truncated one.
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await File.ReadAllBytesAsync(archived))[..4]));
        }
    }

    /// <summary>
    /// The sequential case still behaves: a second send of an already-sent invoice is refused by the
    /// Domain before anything is rendered or written, so the first send's document is the only one.
    /// </summary>
    [Fact]
    public async Task ASecondSendAfterTheFirstCommitted_IsRefusedAndWritesNothing()
    {
        var invoiceId = await SeedDraftInvoiceAsync();
        var disk = new LocalDiskFileStorage(new FileStorageOptions { RootPath = _storageRoot });

        await using (var firstContext = fixture.CreateContext())
        {
            await HandlerOn(firstContext, disk).HandleAsync(new SendInvoiceCommand(invoiceId, 1), CancellationToken.None);
        }

        await using var secondContext = fixture.CreateContext();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => HandlerOn(secondContext, disk).HandleAsync(new SendInvoiceCommand(invoiceId, 1), CancellationToken.None));

        Assert.Single(ArchivedFilesFor(invoiceId));
    }

    /// <summary>A barrier for async callers: each arrival waits until every participant has arrived.</summary>
    private sealed class CountdownGate(int participants)
    {
        private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _remaining = participants;

        public Task ArriveAndWaitAsync()
        {
            if (Interlocked.Decrement(ref _remaining) == 0)
            {
                _allArrived.SetResult();
            }

            return _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    /// <summary>
    /// Real storage, plus a hold after each write. Delays only; never changes what is stored.
    /// </summary>
    private sealed class GatedFileStorage(IFileStorage inner, CountdownGate gate) : IFileStorage
    {
        public async Task SaveAsync(Stream content, string fileUrl, CancellationToken cancellationToken)
        {
            await inner.SaveAsync(content, fileUrl, cancellationToken);
            await gate.ArriveAndWaitAsync();
        }

        public Task DeleteAsync(string fileUrl, CancellationToken cancellationToken) =>
            inner.DeleteAsync(fileUrl, cancellationToken);

        public Task<Stream> OpenReadAsync(string fileUrl, CancellationToken cancellationToken) =>
            inner.OpenReadAsync(fileUrl, cancellationToken);
    }
}
