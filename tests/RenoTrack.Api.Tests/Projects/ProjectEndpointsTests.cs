using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Api.Tests.Projects;

/// <summary>
/// <c>POST /api/v1/angebote/{id}/convert-to-project</c> (SRS FR-7.1, BR-2) and
/// <c>GET /api/v1/projects/{id}</c> (FR-7.4). What this class adds over the Application-layer
/// tests is what only a real request can show: the role gates, the 201/Location contract, that
/// BR-2 and the already-converted rule surface as 409, and that the Customer and Project genuinely
/// reach the database together.
/// </summary>
[Collection("Api")]
public sealed class ProjectEndpointsTests(RenoTrackApiFactory factory)
{
    // ---- Conversion --------------------------------------------------------

    [Fact]
    public async Task Admin_can_convert_a_customer_approved_angebot()
    {
        var (angebotId, leadId) = await CustomerApprovedAngebotAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync($"/api/v1/angebote/{angebotId}/convert-to-project", content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Active", body.GetProperty("status").GetString());
        Assert.Equal(angebotId, body.GetProperty("angebotId").GetInt32());
        var projectId = body.GetProperty("id").GetInt32();

        // The Location header points at the detail read — the reason that endpoint is in this
        // slice's scope rather than deferred (Phase 4's Inspection scheduling had to ship without
        // one, and it is still an open item). Followed rather than string-matched, per
        // LeadReadEndpointsTests' precedent: what matters is that it resolves, and the
        // `[controller]` route token capitalises the segment (`/api/v1/Projects/...`) across every
        // controller in this API, which routing then matches case-insensitively.
        var location = response.Headers.Location;
        Assert.NotNull(location);
        Assert.EndsWith($"/{projectId}", location.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(location)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var project = await context.Projects.SingleAsync(p => p.Id == projectId);
        var customer = await context.Customers.SingleAsync(c => c.Id == project.CustomerId);

        // Both rows are really there — the transaction committed, not merely the Project.
        Assert.Equal(leadId, customer.LeadId);

        // ERD.md's snapshot: the agreed total equals the Angebot's gross total at conversion time.
        var angebot = await context.Angebote.SingleAsync(a => a.Id == angebotId);
        Assert.Equal(angebot.GrossTotal, project.AgreedTotal);
    }

    /// <summary>
    /// Sequence Diagram §7's Phase 6 correction, over HTTP: the Lead already reached <c>Won</c> via
    /// the customer's decision, and conversion must not touch it. A regression here would mean a
    /// second path to <c>Won</c> had appeared.
    /// </summary>
    [Fact]
    public async Task Conversion_leaves_the_lead_status_alone()
    {
        var (angebotId, leadId) = await CustomerApprovedAngebotAsync();
        using var admin = await AdminClientAsync();

        await admin.PostAsync($"/api/v1/angebote/{angebotId}/convert-to-project", content: null);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var lead = await context.Leads.SingleAsync(l => l.Id == leadId);

        Assert.Equal(LeadStatus.Won, lead.Status);
    }

    /// <summary>
    /// BR-2 over HTTP. A `Sent` Angebot is a realistic mistake — the Admin clicking convert before
    /// the customer has answered — rather than a contrived state.
    /// </summary>
    [Fact]
    public async Task Converting_an_angebot_the_customer_has_not_approved_is_a_conflict()
    {
        var (angebotId, _) = await SentAngebotAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync($"/api/v1/angebote/{angebotId}/convert-to-project", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        Assert.False(await context.Projects.AnyAsync(p => p.AngebotId == angebotId));
    }

    /// <summary>
    /// The second click. 409 rather than a 500 from the unique index — the Application pre-check is
    /// normal control flow, the index is the concurrency backstop (D62). Also asserts no second
    /// Customer appeared, which is what the reuse path exists to prevent.
    /// </summary>
    [Fact]
    public async Task Converting_the_same_angebot_twice_is_a_conflict()
    {
        var (angebotId, leadId) = await CustomerApprovedAngebotAsync();
        using var admin = await AdminClientAsync();
        var first = await admin.PostAsync($"/api/v1/angebote/{angebotId}/convert-to-project", content: null);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await admin.PostAsync($"/api/v1/angebote/{angebotId}/convert-to-project", content: null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        Assert.Equal(1, await context.Projects.CountAsync(p => p.AngebotId == angebotId));
        Assert.Equal(1, await context.Customers.CountAsync(c => c.LeadId == leadId));
    }

    [Fact]
    public async Task Converting_an_unknown_angebot_is_a_not_found()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync("/api/v1/angebote/999999/convert-to-project", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// PermissionMatrix.md §5 marks "Convert Angebot to Project" Admin "F", Inspector "—". The
    /// empty-body assertion is what distinguishes this role-gate rejection from an ownership
    /// <c>ForbiddenException</c>, which would carry ProblemDetails — without it the test would pass
    /// even if the role requirement were removed (the Phase 4 Slice 9 finding).
    /// </summary>
    [Fact]
    public async Task An_inspector_cannot_convert()
    {
        var (angebotId, _) = await CustomerApprovedAngebotAsync();
        using var inspector = await InspectorClientAsync();

        var response = await inspector.PostAsync($"/api/v1/angebote/{angebotId}/convert-to-project", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        Assert.False(await context.Projects.AnyAsync(p => p.AngebotId == angebotId));
    }

    [Fact]
    public async Task Conversion_requires_authentication()
    {
        var (angebotId, _) = await CustomerApprovedAngebotAsync();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsync($"/api/v1/angebote/{angebotId}/convert-to-project", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- Detail read -------------------------------------------------------

    [Fact]
    public async Task Admin_can_read_the_project_detail()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync($"/api/v1/projects/{projectId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(projectId, body.GetProperty("id").GetInt32());
        Assert.Equal("Active", body.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("customerName").GetString()));
        Assert.StartsWith("ANG-", body.GetProperty("angebotNumber").GetString()!, StringComparison.Ordinal);
        Assert.True(body.GetProperty("leadId").GetInt32() > 0);
    }

    /// <summary>
    /// PermissionMatrix.md §5: "View Project detail — Admin F, Inspector R". <c>R</c> is read-only
    /// but **unscoped**, so an Inspector reads a Project regardless of whether they worked its Lead
    /// — here, one converted by an Admin from an Angebot they do not own. Wireframe E1 heads its
    /// screen "Roles: Admin"; the matrix is the authority on permissions (CLAUDE.md §16), the same
    /// way Phase 5 resolved D3's identical divergence.
    /// </summary>
    [Fact]
    public async Task An_inspector_can_read_the_project_detail_and_is_not_scoped()
    {
        var projectId = await ConvertedProjectAsync();
        using var inspector = await InspectorClientAsync();

        var response = await inspector.GetAsync($"/api/v1/projects/{projectId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(projectId, body.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Reading_an_unknown_project_is_a_not_found()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync("/api/v1/projects/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_project_detail_requires_authentication()
    {
        var projectId = await ConvertedProjectAsync();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync($"/api/v1/projects/{projectId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// FR-7.4's Invoice portion is deferred to Phase 8, and the response must not pretend
    /// otherwise. Asserted against raw JSON rather than a typed read, so a field added later cannot
    /// slip in unnoticed — the same technique Phase 6 used to pin the public DTO's surface.
    /// </summary>
    [Fact]
    public async Task The_project_detail_carries_no_invoice_fields_yet()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync($"/api/v1/projects/{projectId}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var propertyNames = body.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal(
            ["id", "status", "agreedTotal", "createdAt", "completedAt", "customerId", "customerName",
             "leadId", "inspectionId", "angebotId", "angebotNumber"],
            propertyNames);
    }

    // ---- Helpers -----------------------------------------------------------

    private Task<HttpClient> InspectorClientAsync() =>
        ClientAsync(RenoTrackApiFactory.InspectorEmail, RenoTrackApiFactory.InspectorPassword);

    private Task<HttpClient> AdminClientAsync() =>
        ClientAsync(RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

    private async Task<HttpClient> ClientAsync(string email, string password)
    {
        var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());

        return client;
    }

    private async Task<int> ConvertedProjectAsync()
    {
        var (angebotId, _) = await CustomerApprovedAngebotAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync($"/api/v1/angebote/{angebotId}/convert-to-project", content: null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    /// <summary>
    /// Drives a Lead and Angebot all the way to <c>CustomerApproved</c> through the real endpoints,
    /// including the customer's own token-link decision — never by writing a status directly, so
    /// BR-2's precondition is genuinely reached the way production reaches it.
    /// </summary>
    private async Task<(int AngebotId, int LeadId)> CustomerApprovedAngebotAsync()
    {
        var (angebotId, leadId) = await SentAngebotAsync();

        string token;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
            token = (await context.TokenLinks.SingleAsync(t => t.EntityId == angebotId)).Token;
        }

        using var anonymous = factory.CreateClient();
        var decision = await anonymous.PostAsJsonAsync(
            $"/api/v1/public/angebote/{token}/decision", new { decision = "Approve" });
        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);

        return (angebotId, leadId);
    }

    private async Task<(int AngebotId, int LeadId)> SentAngebotAsync()
    {
        var leadId = await SeedLeadReadyForAngebotAsync();
        using var inspector = await InspectorClientAsync();

        var created = await inspector.PostAsJsonAsync($"/api/v1/leads/{leadId}/angebote", new { inspectionId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var angebotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var section = await inspector.PostAsJsonAsync(
            $"/api/v1/angebote/{angebotId}/sections", new { title = "Pos. 1", sortOrder = 1 });
        var sectionId = (await section.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        await inspector.PostAsJsonAsync($"/api/v1/angebote/{angebotId}/items", new
        {
            sectionId,
            catalogItemId = (int?)null,
            description = "Wände abbrechen",
            specification = (string?)null,
            unitCode = "m2",
            quantity = 10m,
            unitPrice = 25.00m,
            vatRate = "Standard",
        });

        var submitted = await inspector.PostAsync($"/api/v1/angebote/{angebotId}/submit-for-review", content: null);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);

        using var admin = await AdminClientAsync();
        var approved = await admin.PostAsync($"/api/v1/angebote/{angebotId}/approve", content: null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        var sent = await admin.PostAsync($"/api/v1/angebote/{angebotId}/send", content: null);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);

        return (angebotId, leadId);
    }

    private async Task<int> SeedLeadReadyForAngebotAsync()
    {
        var inspectorId = await factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var lead = Lead.Create(
            "Convert project lead", "0176 5550007", $"convert-{Guid.NewGuid():N}@example.com", LeadSource.Phone);
        context.Leads.Add(lead);
        await context.SaveChangesAsync();

        lead.AssignInspector(inspectorId);
        lead.MarkInspectionScheduled();
        lead.MarkInspectionDone();
        await context.SaveChangesAsync();

        return lead.Id;
    }
}
