using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Entities;

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

    /// <summary>
    /// The un-wrapped fixture, kept for its seeding helpers (<c>GetUserIdAsync</c>) — the wrapped
    /// factory above is a <c>WebApplicationFactory&lt;Program&gt;</c> and does not expose them.
    /// </summary>
    private readonly RenoTrackApiFactory _fixture;

    public AuthenticationTests(RenoTrackApiFactory factory)
    {
        _fixture = factory;
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

    /// <summary>
    /// A login failure is RFC 7807 <c>ProblemDetails</c> like every other error in this API
    /// (Architecture.md §5.3, CLAUDE.md §22) — it used to be a bare JSON string, which made the most
    /// frequently hit error the one a client could not parse uniformly.
    /// </summary>
    [Fact]
    public async Task Login_with_wrong_password_is_rejected_as_problem_details()
    {
        using var client = _factory.CreateClient();

        var response = await LoginAsync(client, RenoTrackApiFactory.AdminEmail, "NotTheRightPassword1!");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(401, problem.GetProperty("status").GetInt32());
        Assert.Equal("Unauthorized", problem.GetProperty("title").GetString());
        Assert.Equal("Invalid email or password.", problem.GetProperty("detail").GetString());

        // traceId comes from Program.cs's CustomizeProblemDetails, so it must be present here too —
        // that is the whole point of matching the API-wide contract rather than hand-rolling a body.
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Login_with_unknown_email_is_rejected_indistinguishably_from_a_wrong_password()
    {
        using var client = _factory.CreateClient();

        var unknown = await LoginAsync(client, "nobody@renotrack.test", "AnyPassword1!");
        var wrongPassword = await LoginAsync(client, RenoTrackApiFactory.AdminEmail, "NotTheRightPassword1!");

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);

        // Read each body once — an HttpContent stream cannot be re-read.
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        var wrongPasswordBody = await wrongPassword.Content.ReadAsStringAsync();

        // Every field is compared *except* traceId, which is per-request by design and would defeat
        // the comparison for a reason that has nothing to do with enumeration. Comparing the stable
        // fields deliberately — rather than the raw body — is what keeps this test meaningful now
        // that the response is ProblemDetails.
        Assert.Equal(StableProblemFields(unknownBody), StableProblemFields(wrongPasswordBody));

        // Both still carry a traceId; it is only its *value* that legitimately differs.
        foreach (var body in new[] { unknownBody, wrongPasswordBody })
        {
            using var problem = JsonDocument.Parse(body);
            Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        }
    }

    /// <summary>
    /// Every ProblemDetails member except <c>traceId</c>, rendered in a stable order so two responses
    /// can be compared for the security-relevant difference only.
    /// </summary>
    private static string StableProblemFields(string problemJson)
    {
        using var problem = JsonDocument.Parse(problemJson);

        return string.Join(
            "\n",
            problem.RootElement.EnumerateObject()
                .Where(property => property.Name != "traceId")
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => $"{property.Name}={property.Value}"));
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
    public async Task Refresh_with_an_unknown_token_is_rejected_as_problem_details()
    {
        using var client = _factory.CreateClient();

        var response = await RefreshAsync(client, "not-a-real-refresh-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(401, problem.GetProperty("status").GetInt32());
        Assert.Equal("Unauthorized", problem.GetProperty("title").GetString());
        Assert.Equal("Invalid refresh token.", problem.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    /// <summary>
    /// An unknown token and a reused (revoked) one must be indistinguishable, for the same
    /// non-disclosure reason as login: telling them apart would confirm that a token once existed.
    /// </summary>
    [Fact]
    public async Task A_reused_refresh_token_is_rejected_indistinguishably_from_an_unknown_one()
    {
        using var client = _factory.CreateClient();

        var login = await LoginAsync(client, RenoTrackApiFactory.SecondInspectorEmail, RenoTrackApiFactory.SecondInspectorPassword);
        var original = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString()!;

        // Rotate once so the original becomes revoked, then present it again.
        await RefreshAsync(client, original);

        var reused = await RefreshAsync(client, original);
        var unknown = await RefreshAsync(client, "not-a-real-refresh-token");

        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);

        Assert.Equal(
            StableProblemFields(await reused.Content.ReadAsStringAsync()),
            StableProblemFields(await unknown.Content.ReadAsStringAsync()));
    }

    /// <summary>
    /// Concurrent rotation of one refresh token: exactly one caller may win, and no second live chain
    /// may ever exist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without a database-enforced guarantee, two requests can both read the token as un-revoked, both
    /// revoke it, and both insert a successor — producing two live chains from one token and bypassing
    /// reuse detection entirely. <c>RevokedAt</c> is a concurrency token precisely so the losing
    /// UPDATE matches zero rows; EF wraps <c>SaveChanges</c> in one transaction, so the loser's
    /// replacement INSERT rolls back with it.
    /// </para>
    /// <para>
    /// Each request gets its own DI scope and therefore its own <c>DbContext</c>, which is what makes
    /// this a genuine test of the database's arbitration rather than of in-process state — the same
    /// property that lets it hold across multiple application instances.
    /// </para>
    /// <para>
    /// The surviving-token assertion is <b>at most one</b>, not exactly one, and that is deliberate: a
    /// loser whose read happens after the winner has committed sees a revoked token and correctly
    /// treats it as reuse, revoking the whole chain (D60). Both outcomes are safe; what must never
    /// happen is two live chains.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Concurrent_rotation_of_one_refresh_token_lets_exactly_one_caller_win()
    {
        using var client = _factory.CreateClient();

        var login = await LoginAsync(client, RenoTrackApiFactory.DualRoleEmail, RenoTrackApiFactory.DualRolePassword);
        var original = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString()!;

        const int attempts = 8;

        var responses = await Task.WhenAll(
            Enumerable.Range(0, attempts).Select(_ => RefreshAsync(client, original)));

        var succeeded = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var rejected = responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized);

        // Reported on failure: "one succeeded, six rejected" leaves the eighth response invisible,
        // and an unexpected 500 here would be a real defect rather than a flaky count.
        var distribution = string.Join(
            ", ",
            responses.GroupBy(r => r.StatusCode)
                .OrderBy(group => group.Key)
                .Select(group => $"{(int)group.Key}×{group.Count()}"));

        Assert.True(succeeded == 1, $"Expected exactly one rotation to succeed. Distribution: {distribution}.");
        Assert.True(rejected == attempts - 1, $"Expected every other attempt to be 401. Distribution: {distribution}.");

        var userId = await _fixture.GetUserIdAsync(RenoTrackApiFactory.DualRoleEmail);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var stored = await dbContext.RefreshTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .ToListAsync();

        // The presented token is revoked no matter which path ended the race.
        var originalRow = Assert.Single(stored, t => t.TokenHash == RefreshToken.Hash(original));
        Assert.NotNull(originalRow.RevokedAt);

        // The security invariant: never a second live branch.
        Assert.True(
            stored.Count(t => t.RevokedAt is null) <= 1,
            $"Expected at most one live refresh token, found {stored.Count(t => t.RevokedAt is null)}.");
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
