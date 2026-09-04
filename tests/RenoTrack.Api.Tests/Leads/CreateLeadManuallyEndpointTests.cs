using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Api.Tests.Leads;

/// <summary>
/// The Admin's manual Lead entry (SRS FR-2.1, <c>PermissionMatrix.md</c> §1, D86).
/// </summary>
/// <remarks>
/// These tests concentrate on what makes this endpoint different from the anonymous contact form
/// beside it: it is Admin-only, it attributes the Lead to the caller from the token rather than the
/// body, and the one field it newly accepts — <c>source</c> — cannot express <c>Website</c>, so the
/// FR-9.2 notification path stays unreachable from here.
/// </remarks>
[Collection("Api")]
public sealed class CreateLeadManuallyEndpointTests(RenoTrackApiFactory factory)
{
    [Fact]
    public async Task Admin_creates_a_phone_sourced_lead_and_gets_201_with_a_working_location()
    {
        using var client = await AuthenticatedClientAsync(
            RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

        var response = await client.PostAsJsonAsync("/api/v1/leads/manual", new
        {
            name = "Telefon Anruf",
            phone = "+49 151 55555501",
            email = "telefon.anruf@example.de",
            source = "Phone",
            address = "Bahnhofstraße 3, 50667 Köln",
            notes = "Ruft wegen Dachsanierung an.",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(nameof(LeadSource.Phone), body.GetProperty("source").GetString());
        Assert.Equal(nameof(LeadStatus.New), body.GetProperty("status").GetString());

        // A Location header pointing at a route that 404s is worse than none, so prove it resolves.
        var location = response.Headers.Location!.ToString();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(location)).StatusCode);
    }

    [Fact]
    public async Task Admin_creates_an_email_sourced_lead()
    {
        using var client = await AuthenticatedClientAsync(
            RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

        var response = await client.PostAsJsonAsync("/api/v1/leads/manual", new
        {
            name = "E-Mail Anfrage",
            phone = "+49 151 55555502",
            email = "email.anfrage@example.de",
            source = "Email",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(nameof(LeadSource.Email), body.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Website_is_not_an_accepted_source_here()
    {
        using var client = await AuthenticatedClientAsync(
            RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

        var response = await client.PostAsJsonAsync("/api/v1/leads/manual", new
        {
            name = "Falsche Quelle",
            phone = "+49 151 55555503",
            email = "falsche.quelle@example.de",
            source = "Website",
        });

        // ManualLeadSource has no Website member, so this fails at model binding rather than at a
        // validator — which is the point of using a narrower enum instead of a rule (D86). Were it
        // ever accepted, it would fire the FR-9.2 "new website Lead" notification for an enquiry
        // that never came through the form.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Attributes_the_audit_entry_to_the_calling_admin_not_the_body()
    {
        var adminId = await factory.GetUserIdAsync(RenoTrackApiFactory.AdminEmail);
        using var client = await AuthenticatedClientAsync(
            RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

        var response = await client.PostAsJsonAsync("/api/v1/leads/manual", new
        {
            name = "Audit Zuordnung",
            phone = "+49 151 55555504",
            email = "audit.zuordnung@example.de",
            source = "Phone",
            createdByUserId = 999_999,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var leadId = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt32();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var entry = await dbContext.AuditLogs
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.EntityType == "Lead" && a.EntityId == leadId);

        Assert.NotNull(entry);

        // The body asked for 999999; D61 says the caller's identity comes from the token, so the
        // audit trail must name the real Admin.
        Assert.Equal(adminId, entry.PerformedByUserId);
    }

    [Fact]
    public async Task Persists_the_lead_with_its_manual_source()
    {
        using var client = await AuthenticatedClientAsync(
            RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

        var response = await client.PostAsJsonAsync("/api/v1/leads/manual", new
        {
            name = "Gespeicherter Lead",
            phone = "+49 151 55555505",
            email = "gespeichert@example.de",
            source = "Phone",
        });

        var leadId = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt32();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var lead = await dbContext.Leads.AsNoTracking().SingleOrDefaultAsync(l => l.Id == leadId);

        Assert.NotNull(lead);
        Assert.Equal(LeadSource.Phone, lead.Source);
        Assert.Null(lead.AssignedInspectorId);
    }

    [Fact]
    public async Task Inspector_is_forbidden()
    {
        using var client = await AuthenticatedClientAsync(
            RenoTrackApiFactory.InspectorEmail, RenoTrackApiFactory.InspectorPassword);

        var response = await client.PostAsJsonAsync("/api/v1/leads/manual", new
        {
            name = "Inspektor Versuch",
            phone = "+49 151 55555506",
            email = "inspektor.versuch@example.de",
            source = "Phone",
        });

        // PermissionMatrix §1 marks this row Admin F, Inspector —.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_is_unauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/leads/manual", new
        {
            name = "Anonymer Versuch",
            phone = "+49 151 55555507",
            email = "anonym.versuch@example.de",
            source = "Phone",
        });

        // The sibling POST /api/v1/leads is [AllowAnonymous]; this one must not inherit that.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_an_invalid_email_with_a_field_keyed_400()
    {
        using var client = await AuthenticatedClientAsync(
            RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

        var response = await client.PostAsJsonAsync("/api/v1/leads/manual", new
        {
            name = "Ungültige Mail",
            phone = "+49 151 55555508",
            email = "keine-mail-adresse",
            source = "Phone",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Both creation paths share CreateLeadCommandValidator, so both must report the same shape.
        Assert.Equal("Validation Failed", problem.GetProperty("title").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("Email", out _));
    }

    private async Task<HttpClient> AuthenticatedClientAsync(string email, string password)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
