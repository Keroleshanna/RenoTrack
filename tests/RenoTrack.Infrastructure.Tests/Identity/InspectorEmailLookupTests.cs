using RenoTrack.Infrastructure.Identity;
using RenoTrack.Infrastructure.Tests.Persistence;

namespace RenoTrack.Infrastructure.Tests.Identity;

/// <summary>
/// Real SQL Server LocalDB (D40), because the behaviour under test is what a real
/// <c>AspNetUsers</c> row yields — including that <c>Email</c> is nullable there, which is the whole
/// reason D2 had to decide what a missing address means.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class InspectorEmailLookupTests(RenoTrackDbContextFixture fixture)
{
    private async Task<int> SeedUserAsync(string? email, bool isActive = true)
    {
        await using var context = fixture.CreateContext();

        var user = new ApplicationUser
        {
            Name = "Test Inspector",
            Email = email,
            UserName = email ?? $"no-email-{Guid.NewGuid():N}",
            IsActive = isActive,
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        return user.Id;
    }

    [Fact]
    public async Task Finds_the_address_of_an_existing_user()
    {
        var inspectorId = await SeedUserAsync("inspector@example.invalid");

        await using var context = fixture.CreateContext();
        var lookup = new InspectorEmailLookup(context);

        Assert.Equal("inspector@example.invalid", await lookup.FindEmailAsync(inspectorId, CancellationToken.None));
    }

    [Fact]
    public async Task Returns_null_for_an_unknown_user()
    {
        await using var context = fixture.CreateContext();
        var lookup = new InspectorEmailLookup(context);

        Assert.Null(await lookup.FindEmailAsync(999_999, CancellationToken.None));
    }

    /// <summary>
    /// Proves the null case is reachable rather than defensive: Identity's own <c>Email</c> column is
    /// nullable, since <c>ApplicationUserConfiguration</c> configures only the columns this project
    /// adds. The caller treats this as a delivery failure (D2).
    /// </summary>
    [Fact]
    public async Task Returns_null_when_the_row_carries_no_address()
    {
        var inspectorId = await SeedUserAsync(email: null);

        await using var context = fixture.CreateContext();
        var lookup = new InspectorEmailLookup(context);

        Assert.Null(await lookup.FindEmailAsync(inspectorId, CancellationToken.None));
    }

    /// <summary>
    /// D3: <c>IsActive</c> is not a delivery condition. Deactivation governs whether someone may act
    /// in the dashboard — a question <c>IUserQueries.IsActiveInspectorAsync</c> already answers —
    /// not whether they are told what happened to an Angebot they own.
    /// </summary>
    [Fact]
    public async Task Returns_the_address_of_a_deactivated_user()
    {
        var inspectorId = await SeedUserAsync("inactive@example.invalid", isActive: false);

        await using var context = fixture.CreateContext();
        var lookup = new InspectorEmailLookup(context);

        Assert.Equal("inactive@example.invalid", await lookup.FindEmailAsync(inspectorId, CancellationToken.None));
    }
}
