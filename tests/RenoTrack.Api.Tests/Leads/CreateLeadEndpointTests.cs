using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Domain.Entities;
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
    /// <summary>
    /// <b>201 and nothing else (Slice 7, Q17).</b> The anonymous caller is a contact form: it needs
    /// to know the enquiry was accepted and has no business knowing the Lead's sequential id — which
    /// discloses how many enquiries the company has ever received — its status, or its inspector.
    /// There is no Location header either: it would name a route this caller cannot open while
    /// disclosing that same id.
    /// </summary>
    [Fact]
    public async Task Creates_a_lead_and_returns_201_with_no_body_and_no_location()
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
        Assert.Null(response.Headers.Location);
        Assert.Equal(0, response.Content.Headers.ContentLength ?? 0);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Empty(body);

        // The row exists all the same — the caller simply is not told about it.
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var lead = await dbContext.Leads.AsNoTracking().SingleOrDefaultAsync(l => l.Email == "anna.schmidt@example.de");

        Assert.NotNull(lead);
        Assert.Equal("Anna Schmidt", lead.Name);
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

        // Read from the row rather than a response body, which Slice 7 removed. The claim is
        // unchanged: Website, not the Phone the caller asked for — which matters because the handler
        // only notifies the Admin for website-sourced Leads (FR-9.2), so a caller controlling this
        // field could suppress that notification.
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var lead = await dbContext.Leads.AsNoTracking().SingleAsync(l => l.Email == "injected@example.de");

        Assert.Equal(LeadSource.Website, lead.Source);
        Assert.Equal(LeadStatus.New, lead.Status);
        Assert.Null(lead.AssignedInspectorId);
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

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // A 201 proves the handler ran; only reading the row back proves it committed. Looked up by
        // email, because the response no longer carries the id (Slice 7).
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var lead = await dbContext.Leads.AsNoTracking().SingleOrDefaultAsync(l => l.Email == "persisted@example.de");

        Assert.NotNull(lead);
        Assert.Equal("Persisted Lead", lead.Name);
        Assert.Equal(LeadSource.Website, lead.Source);
    }

    /// <summary>
    /// <b>The defect Slice 7 closes.</b> A value longer than its column used to pass every guard and
    /// fail at the database: an ordinary bad request answered as a 500 with a stack trace. Each
    /// field is checked at its limit and one character past it, so the boundary itself is pinned.
    /// </summary>
    [Theory]
    [InlineData("name", Lead.MaxNameLength, "Name")]
    [InlineData("phone", Lead.MaxPhoneLength, "Phone")]
    [InlineData("address", Lead.MaxAddressLength, "Address")]
    [InlineData("notes", Lead.MaxNotesLength, "Notes")]
    public async Task A_field_longer_than_its_column_is_a_field_keyed_400_not_a_500(string field, int maximumLength, string errorKey)
    {
        using var client = factory.CreateClient();

        var atTheLimit = await client.PostAsJsonAsync("/api/v1/leads", Submission(field, new string('x', maximumLength)));
        Assert.Equal(HttpStatusCode.Created, atTheLimit.StatusCode);

        var oneTooLong = await client.PostAsJsonAsync("/api/v1/leads", Submission(field, new string('x', maximumLength + 1)));

        Assert.Equal(HttpStatusCode.BadRequest, oneTooLong.StatusCode);
        var problem = await oneTooLong.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Validation Failed", problem.GetProperty("title").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty(errorKey, out _), $"expected an error keyed '{errorKey}'");
    }

    /// <summary>
    /// The email limit needs its own case: a 321-character address is refused for its length, and
    /// the value still has to look like an address or the format rule answers first.
    /// </summary>
    [Fact]
    public async Task An_email_longer_than_its_column_is_a_field_keyed_400()
    {
        using var client = factory.CreateClient();
        var local = new string('a', Lead.MaxEmailLength - "@example.de".Length + 1);

        var response = await client.PostAsJsonAsync("/api/v1/leads", new
        {
            name = "Long Email",
            phone = "+49 151 44444444",
            email = $"{local}@example.de",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("Email", out _));
    }

    /// <summary>A submission whose one named field carries <paramref name="value"/>.</summary>
    private static object Submission(string field, string value) => new
    {
        name = field == "name" ? value : $"Length Case {Guid.NewGuid():N}",
        phone = field == "phone" ? value : "+49 151 55555555",
        email = $"length-{Guid.NewGuid():N}@example.de",
        address = field == "address" ? value : null,
        notes = field == "notes" ? value : null,
    };

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

        // Counted in the database rather than compared as two returned ids, because the anonymous
        // response no longer carries one (Slice 7). The claim is unchanged: deliberately not
        // idempotent, because silently de-duplicating would discard a genuine second enquiry, which
        // is worse than a duplicate row an Admin can close.
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var leads = await dbContext.Leads.AsNoTracking().Where(l => l.Email == "duplicate@example.de").ToListAsync();

        Assert.Equal(2, leads.Count);
        Assert.NotEqual(leads[0].Id, leads[1].Id);
    }
}
