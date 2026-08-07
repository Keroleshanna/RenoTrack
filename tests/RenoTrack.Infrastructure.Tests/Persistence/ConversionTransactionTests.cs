using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Domain.ValueObjects;
using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Repositories;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// The transaction semantics `ConvertAngebotToProjectCommandHandler` depends on, proved against
/// real LocalDB — and **only** provable here. The Application-layer handler tests use a fake
/// <c>IUnitOfWork</c> that has no database and rolls nothing back; they prove the handler opens,
/// commits and disposes correctly, which is orchestration. Whether a rollback actually removes the
/// Customer row is a database fact, and this is where it is established.
///
/// <para>
/// Background: `ARCHITECTURE_DECISIONS.md` D48's amendment. `Project.CustomerId` needs a
/// database-generated identity that only <c>SaveChangesAsync</c> produces, and because
/// <c>Project</c> deliberately has no <c>Customer</c> navigation property, EF cannot defer that
/// foreign key through relationship fix-up. Hence two saves, one transaction.
/// </para>
/// </summary>
[Collection("Infrastructure Database")]
public sealed class ConversionTransactionTests(RenoTrackDbContextFixture fixture)
{
    private async Task<int> SeedLeadAsync(RenoTrackDbContext context)
    {
        var lead = Lead.Create("M. Klein", "0176 1234567", $"{Guid.NewGuid():N}@example.com", LeadSource.Phone);
        context.Leads.Add(lead);
        await context.SaveChangesAsync();
        return lead.Id;
    }

    private async Task<int> SeedAngebotAsync(RenoTrackDbContext context, int leadId)
    {
        var user = new ApplicationUser { Name = "Test Inspector" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var angebot = Angebot.Create(leadId, null, $"ANG-{Guid.NewGuid():N}"[..18], user.Id);
        context.Angebote.Add(angebot);
        await context.SaveChangesAsync();
        return angebot.Id;
    }

    /// <summary>
    /// The committed create-Customer path: two saves inside one transaction, both rows present
    /// afterwards, and the Project carrying the Customer's real database-assigned id.
    /// </summary>
    [Fact]
    public async Task CommittedConversionPersistsCustomerAndProjectTogether()
    {
        int leadId, angebotId, customerId;

        await using (var context = fixture.CreateContext())
        {
            leadId = await SeedLeadAsync(context);
            angebotId = await SeedAngebotAsync(context, leadId);

            var unitOfWork = new UnitOfWork(context);
            var customerRepository = new CustomerRepository(context);
            var projectRepository = new ProjectRepository(context);

            await using var transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None);

            var customer = Customer.Create(leadId, "M. Klein", "m.klein@example.com", "0176 1234567");
            await customerRepository.AddAsync(customer, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);

            // The identity that only the first save can produce — the whole reason a transaction
            // is needed rather than a single SaveChanges.
            Assert.True(customer.Id > 0);
            customerId = customer.Id;

            var project = Project.Create(customer.Id, angebotId, Money.FromExact(25_673.36m));
            await projectRepository.AddAsync(project, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);

            await transaction.CommitAsync(CancellationToken.None);
        }

        await using var verify = fixture.CreateContext();
        Assert.True(await verify.Customers.AnyAsync(c => c.Id == customerId));
        var persisted = await verify.Projects.SingleAsync(p => p.AngebotId == angebotId);
        Assert.Equal(customerId, persisted.CustomerId);
        Assert.Equal(Money.FromExact(25_673.36m), persisted.AgreedTotal);
    }

    /// <summary>
    /// The failure this design exists to prevent. The Customer is saved successfully and gets a
    /// real id; the Project's save then fails on a real foreign key; the transaction is disposed
    /// without commit. A fresh context must find neither row.
    ///
    /// <para>
    /// This is a rollback, deliberately not a compensating delete — nothing issues a DELETE, and
    /// nothing needs to, which is what makes it immune to the crash-between-steps hole that
    /// compensation leaves open (§22).
    /// </para>
    /// <para>
    /// <b>The transaction is disposed explicitly while its <c>DbContext</c> is still alive, and the
    /// row is then re-read through that same context.</b> That ordering is load-bearing, not
    /// incidental: an earlier version of this test let the context's own disposal tear the
    /// connection down, which rolls back any open transaction as a side effect — so the test
    /// passed even when <c>IUnitOfWorkTransaction.DisposeAsync</c> was gutted to a no-op, and
    /// proved the business outcome without proving the mechanism. Found by adversarial
    /// verification, not by inspection. Reading through the same context also cannot deadlock,
    /// where a fresh context would block on the still-held locks instead of failing.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AFailedSecondWriteRollsBackTheCustomerInsert()
    {
        await using var context = fixture.CreateContext();
        var leadId = await SeedLeadAsync(context);

        var unitOfWork = new UnitOfWork(context);
        var customerRepository = new CustomerRepository(context);
        var projectRepository = new ProjectRepository(context);

        var transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None);

        var customer = Customer.Create(leadId, "M. Klein", "m.klein@example.com", "0176 1234567");
        await customerRepository.AddAsync(customer, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        var customerIdInsideTransaction = customer.Id;
        Assert.True(customerIdInsideTransaction > 0, "the Customer must have been genuinely persisted before the failure");

        // A Project pointing at no real Angebot — the FK rejects it, which is a genuine database
        // failure rather than a simulated one.
        await projectRepository.AddAsync(
            Project.Create(customer.Id, angebotId: 999_999_999, Money.FromExact(100m)), CancellationToken.None);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => unitOfWork.SaveChangesAsync(CancellationToken.None));

        // No CommitAsync — disposal alone is what rolls back. The context stays alive, so this
        // isolates the transaction's own disposal from the connection teardown.
        await transaction.DisposeAsync();

        Assert.False(
            await context.Customers.AsNoTracking().AnyAsync(c => c.LeadId == leadId),
            "no orphaned Customer may survive the rolled-back transaction");
        Assert.False(
            await context.Projects.AsNoTracking().AnyAsync(p => p.CustomerId == customerIdInsideTransaction));

        // And it is genuinely gone from the database, not merely invisible to this connection.
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Customers.AnyAsync(c => c.LeadId == leadId));
    }

    /// <summary>
    /// Disposing without committing rolls back even when nothing failed — the property that makes
    /// every escape path safe, including an exception thrown between the two saves and
    /// cancellation.
    /// </summary>
    [Fact]
    public async Task DisposingWithoutCommittingDiscardsEverythingSaved()
    {
        int leadId;

        await using (var context = fixture.CreateContext())
        {
            leadId = await SeedLeadAsync(context);

            var unitOfWork = new UnitOfWork(context);
            var customerRepository = new CustomerRepository(context);

            await using var transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None);
            await customerRepository.AddAsync(
                Customer.Create(leadId, "M. Klein", "m.klein@example.com", "0176 1234567"), CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Customers.AnyAsync(c => c.LeadId == leadId));
    }

    /// <summary>
    /// The reuse path: one save, no explicit transaction, already atomic through EF Core's own
    /// implicit per-save transaction. Opening one for symmetry would take a lock for nothing.
    /// </summary>
    [Fact]
    public async Task ReusingAnExistingCustomerNeedsNoExplicitTransaction()
    {
        await using var context = fixture.CreateContext();
        var leadId = await SeedLeadAsync(context);
        var angebotId = await SeedAngebotAsync(context, leadId);

        var unitOfWork = new UnitOfWork(context);
        var customerRepository = new CustomerRepository(context);
        var projectRepository = new ProjectRepository(context);

        var seeded = Customer.Create(leadId, "M. Klein", "m.klein@example.com", "0176 1234567");
        await customerRepository.AddAsync(seeded, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var found = await customerRepository.FindByLeadIdAsync(leadId, CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal(seeded.Id, found.Id);

        await projectRepository.AddAsync(
            Project.Create(found.Id, angebotId, Money.FromExact(100m)), CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        await using var verify = fixture.CreateContext();
        Assert.Equal(1, await verify.Customers.CountAsync(c => c.LeadId == leadId));
        Assert.True(await verify.Projects.AnyAsync(p => p.AngebotId == angebotId));
    }

    /// <summary>
    /// <c>ExistsForAngebotAsync</c> is normal control flow — it makes an ordinary repeat conversion
    /// a 409 rather than an unmapped 500 (D62). It is **not** a substitute for the unique index,
    /// which still refuses a second Project for the same Angebot when two attempts race past the
    /// pre-check. Both halves are asserted here so neither can be removed as redundant.
    /// </summary>
    [Fact]
    public async Task TheUniqueIndexRemainsTheBackstopBehindTheApplicationPreCheck()
    {
        await using var context = fixture.CreateContext();
        var leadId = await SeedLeadAsync(context);
        var angebotId = await SeedAngebotAsync(context, leadId);

        var unitOfWork = new UnitOfWork(context);
        var customerRepository = new CustomerRepository(context);
        var projectRepository = new ProjectRepository(context);

        var customer = Customer.Create(leadId, "M. Klein", "m.klein@example.com", "0176 1234567");
        await customerRepository.AddAsync(customer, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Assert.False(await projectRepository.ExistsForAngebotAsync(angebotId, CancellationToken.None));

        await projectRepository.AddAsync(Project.Create(customer.Id, angebotId, Money.FromExact(100m)), CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Assert.True(await projectRepository.ExistsForAngebotAsync(angebotId, CancellationToken.None));

        // Past the pre-check — what a racing second conversion would do — the database still refuses.
        await using var racing = fixture.CreateContext();
        await new ProjectRepository(racing).AddAsync(
            Project.Create(customer.Id, angebotId, Money.FromExact(100m)), CancellationToken.None);

        await Assert.ThrowsAsync<DbUpdateException>(() => new UnitOfWork(racing).SaveChangesAsync(CancellationToken.None));
    }

    /// <summary>
    /// The Customer-side backstop, for the same reason: <c>Customers.LeadId</c> is unique, so two
    /// conversions racing on a Lead that has never been converted cannot both create a Customer.
    /// </summary>
    [Fact]
    public async Task TheCustomerLeadIdUniqueIndexRemainsABackstop()
    {
        await using var context = fixture.CreateContext();
        var leadId = await SeedLeadAsync(context);

        var customerRepository = new CustomerRepository(context);
        await customerRepository.AddAsync(
            Customer.Create(leadId, "M. Klein", "m.klein@example.com", "0176 1234567"), CancellationToken.None);
        await new UnitOfWork(context).SaveChangesAsync(CancellationToken.None);

        await using var racing = fixture.CreateContext();
        await new CustomerRepository(racing).AddAsync(
            Customer.Create(leadId, "M. Klein", "m.klein@example.com", "0176 1234567"), CancellationToken.None);

        await Assert.ThrowsAsync<DbUpdateException>(() => new UnitOfWork(racing).SaveChangesAsync(CancellationToken.None));
    }
}
