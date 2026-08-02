using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Api.Tests.Leads;

/// <summary>
/// The public contact-form endpoint (SRS FR-1.3). Beyond the happy path, these tests pin the two
/// things that make this endpoint different from every other one: it is reachable with no
/// credentials at all, and the fields a caller must <em>not</em> control are set by the server.
/// </summary>
[Collection("Api")]
public sealed class CreateLeadEndpointTests(RenoTrackApiFactory factory)
{
    [Fact]
    public async Task Creates_a_lead_and_returns_201_with_the_created_resource()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/leads", new
        {
            name = "Anna Schmidt",
            phone = "+49 151 23456789",
            email = "anna.schmidt@example.de",
            address = "Hauptstraße 12, 40213 Düsseldorf",
            notes = "Möchte das Badezimmer neu fliesen lassen, ca. 10 m².",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("id").GetInt32() > 0);
        Assert.Equal("Anna Schmidt", body.GetProperty("name").GetString());
        Assert.Equal("anna.schmidt@example.de", body.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Sets_source_and_ownership_fields_on_the_server_not_from_the_request()
    {
        using var client = factory.CreateClient();

        // Source and createdByUserId are deliberately absent from the request contract; sending
        // them anyway must change nothing, since the controller ignores unmapped members.
        var response = await client.PostAsJsonAsync("/api/v1/leads", new
        {
            name = "Injected Source",
            phone = "+49 151 00000000",
            email = "injected@example.de",
            source = "Phone",
            createdByUserId = 999,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Website, not the Phone the caller asked for — which matters because the handler only
        // notifies the Admin for website-sourced Leads (FR-9.2), so a caller controlling this field
        // could suppress that notification.
        Assert.Equal(nameof(LeadSource.Website), body.GetProperty("source").GetString());
        Assert.Equal(nameof(LeadStatus.New), body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("assignedInspectorId").ValueKind);
    }

    [Fact]
    public async Task Requires_no_authentication()
    {
        using var client = factory.CreateClient();

        // LeadsController is [Authorize] at class level (D57), so this proves [AllowAnonymous] is
        // genuinely applied to the action rather than merely intended.
        Assert.Null(client.DefaultRequestHeaders.Authorization);

        var response = await client.PostAsJsonAsync("/api/v1/leads", new
        {
            name = "Anonymous Caller",
            phone = "+49 151 11111111",
            email = "anonymous@example.de",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Persists_the_lead()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/leads", new
        {
            name = "Persisted Lead",
            phone = "+49 151 22222222",
            email = "persisted@example.de",
        });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetInt32();

        // Returning a DTO proves the handler ran; only reading the row back proves it committed.
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var lead = await dbContext.Leads.AsNoTracking().SingleOrDefaultAsync(l => l.Id == id);

        Assert.NotNull(lead);
        Assert.Equal("Persisted Lead", lead.Name);
        Assert.Equal(LeadSource.Website, lead.Source);
    }

    [Fact]
    public async Task Rejects_an_invalid_email_with_a_field_keyed_400()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/leads", new
        {
            name = "Bad Email",
            phone = "+49 151 33333333",
            email = "not-an-email-address",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Pins the alignment between the request DTO's property names and the validator's error
        // keys: the client sent "email", so the error must come back under "Email", not under a
        // command-internal name it never saw.
        Assert.Equal("Validation Failed", problem.GetProperty("title").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("Email", out _));
    }

    [Fact]
    public async Task Rejects_a_missing_required_field()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/leads", new
        {
            name = "",
            phone = "",
            email = "missing.fields@example.de",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var errors = problem.GetProperty("errors");

        Assert.True(errors.TryGetProperty("Name", out _));
        Assert.True(errors.TryGetProperty("Phone", out _));
    }

    [Fact]
    public async Task Two_identical_submissions_create_two_leads()
    {
        using var client = factory.CreateClient();

        var payload = new
        {
            name = "Duplicate Submitter",
            phone = "+49 151 44444444",
            email = "duplicate@example.de",
        };

        var first = await client.PostAsJsonAsync("/api/v1/leads", payload);
        var second = await client.PostAsJsonAsync("/api/v1/leads", payload);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // Deliberately not idempotent: silently de-duplicating would discard a genuine second
        // enquiry, which is worse than a duplicate row an Admin can close.
        Assert.NotEqual(firstId, secondId);
    }
}
