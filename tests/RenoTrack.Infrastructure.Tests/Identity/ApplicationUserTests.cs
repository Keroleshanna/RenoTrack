using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Infrastructure.Identity;

namespace RenoTrack.Infrastructure.Tests.Identity;

/// <summary>
/// Confirms password hashing is entirely delegated to ASP.NET Core Identity's own registered
/// IPasswordHasher (via UserManager.CreateAsync) — no custom hashing code exists anywhere in
/// this project. Not testing the hasher's own internals (that's the framework's job), just that
/// it's genuinely wired: a created user's PasswordHash is populated and isn't the plaintext.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class ApplicationUserTests
{
    [Fact]
    public async Task CreateAsync_HashesThePasswordViaTheFrameworksDefaultHasher_NotStoredAsPlaintext()
    {
        await using var provider = IdentityTestServices.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        const string plaintextPassword = "Correct-Horse-Battery-Staple-1!";
        var user = new ApplicationUser { Name = "Test Admin", UserName = $"admin-{Guid.NewGuid():N}" };

        var result = await userManager.CreateAsync(user, plaintextPassword);

        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        Assert.NotNull(user.PasswordHash);
        Assert.NotEqual(plaintextPassword, user.PasswordHash);
        Assert.True(user.IsActive);
    }
}
