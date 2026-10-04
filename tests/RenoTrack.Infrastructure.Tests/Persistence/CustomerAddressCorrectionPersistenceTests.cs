using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Repositories;

namespace RenoTrack.Infrastructure.Tests.Persistence;

/// <summary>
/// Phase 14 Slice 2b (D112) against real LocalDB: an address correction made through the
/// repository's tracked load and <see cref="UnitOfWork"/> alone reaches the database — there is no
/// <c>UpdateAsync</c> anywhere in this project — historical rows still load and can be corrected,
/// and the column holds exactly <see cref="Customer.MaxAddressLength"/>.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class CustomerAddressCorrectionPersistenceTests(RenoTrackDbContextFixture fixture)
{
    /// <summary>A converted website Lead: the shape that leaves a Customer with no address.</summary>
    private async Task<int> SeedCustomerAsync(string? address = null)
    {
        var lead = Lead.Create("M. Klein", "0176 1234567", "m.klein@example.com", LeadSource.Website);

        await using var context = fixture.CreateContext();
        context.Leads.Add(lead);
        await context.SaveChangesAsync();

        var customer = Customer.Create(lead.Id, "M. Klein", "m.klein@example.com", "0176 1234567", address);
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        return customer.Id;
    }

    private async Task CorrectThroughTheRepositoryAsync(int customerId, string address)
    {
        await using var context = fixture.CreateContext();
        var repository = new CustomerRepository(context);
        var unitOfWork = new UnitOfWork(context);

        var customer = await repository.GetByIdAsync(customerId, CancellationToken.None);
        customer!.CorrectAddress(address);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<Customer> ReloadAsync(int customerId)
    {
        await using var context = fixture.CreateContext();
        return await context.Customers.SingleAsync(c => c.Id == customerId);
    }

    [Fact]
    public async Task ACorrectionOfACustomerWithNoAddressPersistsThroughTheUnitOfWorkAlone()
    {
        var customerId = await SeedCustomerAsync();

        await CorrectThroughTheRepositoryAsync(customerId, "Musterstr. 1\n12345 Berlin");

        var reloaded = await ReloadAsync(customerId);
        Assert.Equal("Musterstr. 1\n12345 Berlin", reloaded.Address);
        Assert.Equal("M. Klein", reloaded.Name);
        Assert.Equal("m.klein@example.com", reloaded.Email);
        Assert.Equal("0176 1234567", reloaded.Phone);
    }

    [Fact]
    public async Task AnExistingAddressIsReplaced()
    {
        var customerId = await SeedCustomerAsync("Alte Str. 9");

        await CorrectThroughTheRepositoryAsync(customerId, "Neue Str. 3");

        Assert.Equal("Neue Str. 3", (await ReloadAsync(customerId)).Address);
    }

    /// <summary>
    /// An all-whitespace Lead address converted before this slice is stored as the empty string.
    /// Such a row must still load — the constructor stays guard-free (CLAUDE.md §2) — and must be
    /// correctable like any other.
    /// </summary>
    [Fact]
    public async Task AHistoricalEmptyAddressRowLoadsAndCanBeCorrected()
    {
        var customerId = await SeedCustomerAsync("   ");
        Assert.Equal(string.Empty, (await ReloadAsync(customerId)).Address);

        await CorrectThroughTheRepositoryAsync(customerId, "Musterstr. 1");

        Assert.Equal("Musterstr. 1", (await ReloadAsync(customerId)).Address);
    }

    [Fact]
    public async Task TheColumnHoldsExactlyTheMaximumLength()
    {
        var customerId = await SeedCustomerAsync();
        var longest = new string('a', Customer.MaxAddressLength);

        await CorrectThroughTheRepositoryAsync(customerId, longest);

        Assert.Equal(longest, (await ReloadAsync(customerId)).Address);
    }

    /// <summary>
    /// The column's own limit is <see cref="Customer.MaxAddressLength"/>, proved by going around the
    /// Domain with raw SQL: one character more is refused by the database itself (error 2628,
    /// string truncation), so the column and the constant cannot have drifted apart.
    /// </summary>
    [Fact]
    public async Task TheColumnRefusesOneCharacterMoreThanTheMaximum()
    {
        var customerId = await SeedCustomerAsync();
        var tooLong = new string('a', Customer.MaxAddressLength + 1);

        await using var context = fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Customers SET Address = {tooLong} WHERE Id = {customerId}"));

        Assert.Equal(2628, ex.Number);
    }
}
