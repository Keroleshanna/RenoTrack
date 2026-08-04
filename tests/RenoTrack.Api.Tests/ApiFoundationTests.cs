using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Infrastructure.Identity;

namespace RenoTrack.Api.Tests;

/// <summary>
/// Proves the API foundation itself works before any endpoint exists: the real application boots,
/// serves its OpenAPI document, and describes the JWT bearer scheme that protected endpoints will
/// use from Slice 4 onward. No business endpoint is asserted here because none exists yet — and
/// none was invented purely to give these tests something to call.
/// </summary>
[Collection("Api")]
public sealed class ApiFoundationTests(RenoTrackApiFactory factory)
{
    [Fact]
    public void Application_starts_successfully()
    {
        // Creating the client is what actually builds and starts the host, which also runs
        // Program.cs's Identity role seeding against the real (migrated) test database.
        using var client = factory.CreateClient();

        Assert.NotNull(client.BaseAddress);
    }

    /// <summary>
    /// The real <c>Program.cs</c> runs <c>DevelopmentBootstrap</c> immediately after
    /// <c>DatabaseInitializer</c> (D64), and this host is Development — so this proves the step is
    /// genuinely a no-op when it is not enabled, through the production startup path rather than
    /// against the component in isolation.
    /// </summary>
    /// <remarks>
    /// The factory disables it explicitly rather than leaving it unconfigured, because Development is
    /// exactly the environment in which the API project's user secrets are loaded — so a developer
    /// who has set bootstrap passwords on their own machine would otherwise get accounts provisioned
    /// into this database, and this test would pass in CI while failing locally.
    /// </remarks>
    [Fact]
    public async Task No_development_bootstrap_accounts_are_provisioned_when_it_is_disabled()
    {
        using var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // The bootstrap's own default addresses, which carry a dev- prefix precisely so they are
        // distinct from the accounts this factory seeds for its own tests — otherwise this assertion
        // could not tell the two apart, and would fail for the wrong reason.
        Assert.Null(await users.FindByEmailAsync("dev-admin@renotrack.test"));
        Assert.Null(await users.FindByEmailAsync("dev-inspector@renotrack.test"));
    }

    [Fact]
    public async Task OpenApi_document_is_served()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);

        Assert.True(document.RootElement.TryGetProperty("openapi", out _));
    }

    [Fact]
    public async Task Scalar_api_reference_ui_is_served()
    {
        using var client = factory.CreateClient();

        // "/scalar/v1" is Scalar's own default route (/scalar/{documentName}) for the "v1"
        // OpenAPI document — MapScalarApiReference() is called with no route argument. Asserting
        // it here pins the documentation UI as a real deliverable, so removing or breaking the
        // mapping fails CI rather than going unnoticed.
        var response = await client.GetAsync("/scalar/v1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_document_declares_the_bearer_security_scheme()
    {
        using var client = factory.CreateClient();

        var content = await client.GetStringAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(content);

        var scheme = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("bearer");

        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.Equal("JWT", scheme.GetProperty("bearerFormat").GetString());
    }
}
