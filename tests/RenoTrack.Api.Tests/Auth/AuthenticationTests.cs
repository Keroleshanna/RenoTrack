using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace RenoTrack.Api.Tests.Auth;

/// <summary>
/// Covers the authentication behaviours most likely to regress: every login failure mode, refresh
/// rotation, stolen-token reuse detection, and access-token validation. All against the real
/// pipeline, real Identity, and real password hashing (D58).
/// </summary>
[Collection("Api")]
public sealed class AuthenticationTests
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthenticationTests(RenoTrackApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddControllers().AddApplicationPart(typeof(TestProtectedController).Assembly)));
    }

    // ---------- login ----------

    [Fact]
    public async Task Login_with_valid_credentials_returns_tokens_and_user_details()
    {
        using var client = _factory.CreateClient();

        var response = await LoginAsync(client, RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("refreshToken").GetString()));
        Assert.Equal(RenoTrackApiFactory.AdminEmail, body.GetProperty("email").GetString());
        Assert.Equal("Admin", body.GetProperty("role").GetString());
        Assert.True(body.GetProperty("accessTokenExpiresAt").GetDateTime() > DateTime.UtcNow);
    }

    [Fact]
    public async Task Login_issues_an_access_token_carrying_the_expected_claims()
    {
        using var client = _factory.CreateClient();

        var accessToken = await GetAccessTokenAsync(client, RenoTrackApiFactory.InspectorEmail, RenoTrackApiFactory.InspectorPassword);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);

        Assert.Equal(RenoTrackApiFactory.InspectorEmail, jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal("Inspector", jwt.Claims.First(c => c.Type == ClaimTypes.Role).Value);
        Assert.False(string.IsNullOrWhiteSpace(jwt.Subject));
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Jti);

        // Ownership is decided from the loaded aggregate by IOwnershipValidator (CLAUDE.md §16),
        // never from a claim that could go stale mid-token — so no such claim may appear here.
        Assert.DoesNotContain(jwt.Claims, c => c.Type.Contains("Inspector", StringComparison.OrdinalIgnoreCase)
            && c.Type != ClaimTypes.Role);
    }

    [Fact]
    public async Task Login_with_wrong_password_is_rejected()
    {
        using var client = _factory.CreateClient();

        var response = await LoginAsync(client, RenoTrackApiFactory.AdminEmail, "NotTheRightPassword1!");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_unknown_email_is_rejected_indistinguishably_from_a_wrong_password()
    {
        using var client = _factory.CreateClient();

        var unknown = await LoginAsync(client, "nobody@renotrack.test", "AnyPassword1!");
        var wrongPassword = await LoginAsync(client, RenoTrackApiFactory.AdminEmail, "NotTheRightPassword1!");

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);

        // Identical responses, deliberately: any difference here would turn this endpoint into an
        // account-enumeration oracle.
        Assert.Equal(
            await unknown.Content.ReadAsStringAsync(),
            await wrongPassword.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_by_an_inactive_user_is_rejected_even_with_the_correct_password()
    {
        using var client = _factory.CreateClient();

        var response = await LoginAsync(client, RenoTrackApiFactory.InactiveEmail, RenoTrackApiFactory.InactivePassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_out_even_once_the_password_is_correct()
    {
        using var client = _factory.CreateClient();

        // SRS FR-10.3. Identity's default MaxFailedAccessAttempts is 5; CheckPasswordAsync does not
        // increment the counter by itself, so this proves AuthController calls AccessFailedAsync.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await LoginAsync(client, RenoTrackApiFactory.LockoutEmail, "WrongPassword1!");
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var withCorrectPassword = await LoginAsync(
            client, RenoTrackApiFactory.LockoutEmail, RenoTrackApiFactory.LockoutPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, withCorrectPassword.StatusCode);
    }

    // ---------- refresh ----------

    [Fact]
    public async Task Refresh_rotates_the_token_pair()
    {
        using var client = _factory.CreateClient();

        var login = await ReadBodyAsync(
            await LoginAsync(client, RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword));
        var originalRefreshToken = login.GetProperty("refreshToken").GetString()!;

        var refreshed = await RefreshAsync(client, originalRefreshToken);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);

        var body = await ReadBodyAsync(refreshed);
        var newRefreshToken = body.GetProperty("refreshToken").GetString()!;

        Assert.NotEqual(originalRefreshToken, newRefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));

        // The rotated-away token must no longer work.
        var reusedOriginal = await RefreshAsync(client, originalRefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reusedOriginal.StatusCode);
    }

    [Fact]
    public async Task Reusing_a_revoked_refresh_token_revokes_the_whole_chain()
    {
        using var client = _factory.CreateClient();

        var login = await ReadBodyAsync(
            await LoginAsync(client, RenoTrackApiFactory.InspectorEmail, RenoTrackApiFactory.InspectorPassword));
        var first = login.GetProperty("refreshToken").GetString()!;

        var rotated = await ReadBodyAsync(await RefreshAsync(client, first));
        var second = rotated.GetProperty("refreshToken").GetString()!;

        // Replay the already-rotated token, as a thief holding a stolen copy would.
        var replay = await RefreshAsync(client, first);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // The legitimate current token is now dead too: breaking the entire chain is the only way
        // to end an attacker's access when we cannot tell which holder is genuine.
        var afterChainRevocation = await RefreshAsync(client, second);
        Assert.Equal(HttpStatusCode.Unauthorized, afterChainRevocation.StatusCode);
    }

    [Fact]
    public async Task Refresh_with_an_unknown_token_is_rejected()
    {
        using var client = _factory.CreateClient();

        var response = await RefreshAsync(client, "not-a-real-refresh-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- access-token validation ----------

    [Fact]
    public async Task A_valid_access_token_reaches_a_protected_endpoint()
    {
        using var client = _factory.CreateClient();

        var accessToken = await GetAccessTokenAsync(client, RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync("/api/test-protected");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_protected_endpoint_rejects_an_unauthenticated_request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/test-protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_signed_with_the_wrong_key_is_rejected()
    {
        using var client = _factory.CreateClient();

        var forged = CreateToken(
            signingKey: "a-completely-different-key-that-is-long-enough-32",
            expires: DateTime.UtcNow.AddMinutes(15));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);

        var response = await client.GetAsync("/api/test-protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_expired_access_token_is_rejected()
    {
        using var client = _factory.CreateClient();

        // Signed with the real key, so only the expiry can be what rejects it. ClockSkew is set to
        // zero in AddJwtAuthentication, which is what makes a one-minute-old expiry sufficient —
        // the framework default of five minutes would let this token still pass.
        var expired = CreateToken(
            signingKey: TestSigningKey,
            expires: DateTime.UtcNow.AddMinutes(-1));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expired);

        var response = await client.GetAsync("/api/test-protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- helpers ----------

    // Taken from the factory rather than duplicated, so the "expired token" test can never drift
    // into passing for the wrong reason (a signature mismatch instead of the expiry it means to
    // prove). Deliberately not read from appsettings.Development.json, which is gitignored.
    private const string TestSigningKey = RenoTrackApiFactory.TestSigningKey;
    private const string TestIssuer = RenoTrackApiFactory.TestIssuer;
    private const string TestAudience = RenoTrackApiFactory.TestAudience;

    private static string CreateToken(string signingKey, DateTime expires)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, "1"), new Claim(ClaimTypes.Role, "Admin")],
            notBefore: expires.AddMinutes(-30),
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });

    private static async Task<JsonElement> ReadBodyAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> GetAccessTokenAsync(HttpClient client, string email, string password)
    {
        var body = await ReadBodyAsync(await LoginAsync(client, email, password));
        return body.GetProperty("accessToken").GetString()!;
    }
}
