using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace RenoTrack.Api.Tests.ErrorHandling;

/// <summary>
/// Proves every branch of ProblemDetailsExceptionHandler's mapping table over the real HTTP
/// pipeline, using <see cref="TestErrorsController"/> — which exists only in this test assembly
/// and reaches the application only through the ApplicationPart registered below.
/// </summary>
[Collection("Api")]
public sealed class ProblemDetailsExceptionHandlerTests
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProblemDetailsExceptionHandlerTests(RenoTrackApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddControllers().AddApplicationPart(typeof(TestErrorsController).Assembly)));
    }

    [Theory]
    [InlineData("not-found", HttpStatusCode.NotFound, "Not Found")]
    [InlineData("forbidden", HttpStatusCode.Forbidden, "Forbidden")]
    [InlineData("conflict", HttpStatusCode.Conflict, "Conflict")]
    [InlineData("argument", HttpStatusCode.BadRequest, "Bad Request")]
    [InlineData("invalid-operation", HttpStatusCode.Conflict, "Conflict")]
    public async Task Mapped_exceptions_produce_their_documented_status_and_title(
        string route,
        HttpStatusCode expectedStatus,
        string expectedTitle)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/test-errors/{route}");
        var problem = await ReadProblemAsync(response);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal((int)expectedStatus, problem.GetProperty("status").GetInt32());
        Assert.Equal(expectedTitle, problem.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("not-found", "Lead with id '42' was not found.")]
    [InlineData("forbidden", "Inspection 7 is not assigned to Inspector 3.")]
    [InlineData("conflict", "Lead 42 already has an active Angebot.")]
    [InlineData("invalid-operation", "Cannot submit an Angebot in status Sent for review.")]
    public async Task Mapped_exceptions_surface_their_message_as_detail(string route, string expectedDetail)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/test-errors/{route}");
        var problem = await ReadProblemAsync(response);

        Assert.Equal(expectedDetail, problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ValidationException_produces_400_with_field_keyed_errors()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/test-errors/validation");
        var problem = await ReadProblemAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Validation Failed", problem.GetProperty("title").GetString());

        var errors = problem.GetProperty("errors");

        // Two failures on the same property must group under one key, not overwrite each other.
        var nameErrors = errors.GetProperty("Name").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(2, nameErrors.Length);
        Assert.Contains("Name is required.", nameErrors);
        Assert.Contains("Name must not exceed 200 characters.", nameErrors);

        var emailErrors = errors.GetProperty("Email").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal("Email must be a valid email address.", Assert.Single(emailErrors));
    }

    [Fact]
    public async Task Unmapped_exception_produces_500_and_never_leaks_its_message()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/test-errors/unmapped");
        var body = await response.Content.ReadAsStringAsync();
        var problem = JsonDocument.Parse(body).RootElement;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("An unexpected error occurred.", problem.GetProperty("title").GetString());

        // The thrown message contains a password; nothing resembling it may reach the client, and
        // no "detail" member should be present at all.
        Assert.DoesNotContain("hunter2", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-host", body, StringComparison.OrdinalIgnoreCase);
        Assert.False(problem.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task Every_problem_response_carries_a_traceId()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/test-errors/not-found");
        var problem = await ReadProblemAsync(response);

        var traceId = problem.GetProperty("traceId").GetString();

        Assert.False(string.IsNullOrWhiteSpace(traceId));
    }

    [Fact]
    public async Task Problem_response_uses_the_problem_json_content_type()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/test-errors/conflict");

        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem;
    }
}
