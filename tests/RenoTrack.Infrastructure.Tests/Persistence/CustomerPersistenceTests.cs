using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Real SQL constraints behind <c>CustomerConfiguration</c>, against real LocalDB rather than
/// InMemory (D40) — the unique index on <c>LeadId</c>, the FK to <c>Leads</c>, and the nullable
/// <c>Address</c> column are precisely the things InMemory would not enforce.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class CustomerPersistenceTests(RenoTrackDbContextFixture fixture)
{
    /// <summary>Customer.LeadId is a real FK — every test needs an actually-persisted Lead row.</summary>
    private async Task<int> SeedLeadAsync()
    {
        var lead = Lead.Create("M. Klein", "0176 1234567", "m.klein@example.com", LeadSource.Phone);
        await using var writeContext = fixture.CreateContext();
        writeContext.Leads.Add(lead);
        await writeContext.SaveChangesAsync();
        return lead.Id;
    }

    [Fact]
    public async Task ACustomerRoundTripsWithEveryField()
    {
        var leadId = await SeedLeadAsync();
        var customer = Customer.Create(leadId, "M. Klein", "m.klein@example.com", "0176 1234567", "Musterstr. 1, 12345 Berlin");

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Customers.Add(customer);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Customers.SingleAsync(c => c.Id == customer.Id);

        Assert.Equal(leadId, reloaded.LeadId);
        Assert.Equal("M. Klein", reloaded.Name);
        Assert.Equal("m.klein@example.com", reloaded.Email);
        Assert.Equal("0176 1234567", reloaded.Phone);
        Assert.Equal("Musterstr. 1, 12345 Berlin", reloaded.Address);
    }

    /// <summary>
    /// The approved Phase 7 decision, enforced at the column level rather than only in the Domain:
    /// a website-sourced Lead has no address, and a NOT NULL column here would make converting one
    /// impossible no matter what the Domain allows.
    /// </summary>
    [Fact]
    public async Task ACustomerWithNoAddressPersists()
    {
        var leadId = await SeedLeadAsync();
        var customer = Customer.Create(leadId, "M. Klein", "m.klein@example.com", "0176 1234567");

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Customers.Add(customer);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var reloaded = await readContext.Customers.SingleAsync(c => c.Id == customer.Id);

        Assert.Null(reloaded.Address);
    }

    /// <summary>
    /// ERD.md: "One Customer per Lead". The unique index is what makes that true even if a caller
    /// skips the Application-layer check — though the handler still checks first, so an ordinary
    /// repeat conversion is a 409 and not this exception (D62).
    /// </summary>
    [Fact]
    public async Task TwoCustomersCannotShareALead()
    {
        var leadId = await SeedLeadAsync();

        await using (var writeContext = fixture.CreateContext())
        {
            writeContext.Customers.Add(Customer.Create(leadId, "M. Klein", "m.klein@example.com", "0176 1234567"));
            await writeContext.SaveChangesAsync();
        }

        await using var duplicateContext = fixture.CreateContext();
        duplicateContext.Customers.Add(Customer.Create(leadId, "M. Klein Again", "other@example.com", "0176 7654321"));

        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
    }

    /// <summary>
    /// Unlike <c>TokenLink.EntityId</c> — the one documented exception — this column carries a real
    /// FK, per CLAUDE.md §21's "add an FK wherever both tables exist".
    /// </summary>
    [Fact]
    public async Task ACustomerReferencingNoRealLeadIsRejected()
    {
        await using var context = fixture.CreateContext();
        context.Customers.Add(Customer.Create(999_999_999, "Nobody", "nobody@example.com", "0000"));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
