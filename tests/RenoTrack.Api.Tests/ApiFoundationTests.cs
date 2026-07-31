using System.Net;
using System.Text.Json;

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
