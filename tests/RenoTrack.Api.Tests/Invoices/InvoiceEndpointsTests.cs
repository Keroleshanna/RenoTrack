using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Api.Tests.Invoices;

/// <summary>
/// <c>POST /api/v1/projects/{id}/invoices</c> (SRS FR-8.1/FR-8.2) and
/// <c>GET /api/v1/projects/{id}/invoice-balance</c> (BR-3). What this class adds over the
/// Application-layer tests is what only a real request can show: the two different role gates on
/// the two endpoints, the 201 contract, the 409s, and that BR-3's warning genuinely travels the
/// whole way to the wire as a negative number rather than a rejection.
///
/// <para>
/// Every Project here is driven all the way through the real endpoints — Lead, Angebot, submit,
/// approve, send, the customer's own anonymous token decision, then conversion — so the
/// preconditions are reached the way production reaches them.
/// </para>
/// </summary>
[Collection("Api")]
public sealed class InvoiceEndpointsTests(RenoTrackApiFactory factory)
{
    // ---- Creation ----------------------------------------------------------

    [Fact]
    public async Task Admin_can_create_an_invoice_against_a_project()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/invoices",
            new { grossAmount = 100.00m, dueDate = DateTime.UtcNow.AddDays(14) });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(projectId, body.GetProperty("projectId").GetInt32());
        Assert.Equal("Draft", body.GetProperty("status").GetString());
        Assert.StartsWith("RE-", body.GetProperty("invoiceNumber").GetString()!, StringComparison.Ordinal);
        Assert.Equal(100.00m, body.GetProperty("grossAmount").GetDecimal());

        // FR-8.2: net + VAT must reconcile to the gross on the wire, not merely in the Domain.
        Assert.Equal(
            body.GetProperty("grossAmount").GetDecimal(),
            body.GetProperty("netAmount").GetDecimal() + body.GetProperty("vatAmount").GetDecimal());
    }

    /// <summary>
    /// The seeded Angebot is 10 m² × €25.00 at 19% — €250.00 net, €297.50 gross. Invoicing the
    /// whole gross must reproduce the Angebot's own net and VAT exactly (FR-8.2).
    /// </summary>
    [Fact]
    public async Task The_split_matches_the_originating_angebots_rates()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/invoices",
            new { grossAmount = 297.50m, dueDate = DateTime.UtcNow.AddDays(14) });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(250.00m, body.GetProperty("netAmount").GetDecimal());
        Assert.Equal(47.50m, body.GetProperty("vatAmount").GetDecimal());
    }

    [Fact]
    public async Task The_invoice_really_reaches_the_database()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/invoices",
            new { grossAmount = 100.00m, dueDate = DateTime.UtcNow.AddDays(14) });
        var invoiceId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var invoice = await context.Invoices.SingleAsync(i => i.Id == invoiceId);

        Assert.Equal(projectId, invoice.ProjectId);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
    }

    /// <summary>
    /// No <c>GET /api/v1/invoices/{id}</c> is documented anywhere, so 201 carries no
    /// <c>Location</c> — the same position <c>POST /leads/{id}/inspections</c> occupies. Pinned so
    /// nobody "fixes" it by inventing a read endpoint this slice did not agree.
    /// </summary>
    [Fact]
    public async Task Creation_returns_201_with_no_location_header()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/invoices",
            new { grossAmount = 100.00m, dueDate = DateTime.UtcNow.AddDays(14) });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    // ---- Authorization -----------------------------------------------------

    /// <summary>
    /// `PermissionMatrix.md` §5: "Create Invoice — Admin F / Inspector —". The body is asserted
    /// empty because an authorization-middleware rejection carries none, while a
    /// <c>ForbiddenException</c> would yield ProblemDetails — without that assertion this test
    /// could not tell a role gate from an ownership check (Phase 4 Slice 9's lesson).
    /// </summary>
    [Fact]
    public async Task An_inspector_cannot_create_an_invoice()
    {
        var projectId = await ConvertedProjectAsync();
        using var inspector = await InspectorClientAsync();

        var response = await inspector.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/invoices",
            new { grossAmount = 100.00m, dueDate = DateTime.UtcNow.AddDays(14) });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_create_an_invoice()
    {
        var projectId = await ConvertedProjectAsync();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/invoices",
            new { grossAmount = 100.00m, dueDate = DateTime.UtcNow.AddDays(14) });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- Guards ------------------------------------------------------------

    [Fact]
    public async Task Creating_against_an_unknown_project_is_not_found()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            "/api/v1/projects/999999999/invoices",
            new { grossAmount = 100.00m, dueDate = DateTime.UtcNow.AddDays(14) });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_negative_amount_is_a_bad_request()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/invoices",
            new { grossAmount = -1.00m, dueDate = DateTime.UtcNow.AddDays(14) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- BR-3: the warning is a number, not a rejection ---------------------

    /// <summary>
    /// <b>BR-3 warns; it does not block.</b> Invoicing far beyond the agreed total succeeds over
    /// HTTP, and the discrepancy appears as a negative <c>remaining</c>. If this ever returns 409,
    /// someone has converted a documented warning into a prohibition.
    /// </summary>
    [Fact]
    public async Task Over_invoicing_is_allowed_and_reports_a_negative_remaining()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        var created = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/invoices",
            new { grossAmount = 1_000.00m, dueDate = DateTime.UtcNow.AddDays(14) });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var balance = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{projectId}/invoice-balance");

        // The seeded Angebot's gross is 297.50, so 1,000.00 invoiced leaves -702.50 remaining.
        Assert.Equal(297.50m, balance.GetProperty("agreedTotal").GetDecimal());
        Assert.Equal(1_000.00m, balance.GetProperty("alreadyInvoiced").GetDecimal());
        Assert.Equal(-702.50m, balance.GetProperty("remaining").GetDecimal());
    }

    /// <summary>
    /// The payload carries exactly the three figures Sequence Diagram §8 specifies, plus the
    /// project id — no <c>warning</c>, no <c>isOverInvoiced</c>. Asserted against raw JSON so a
    /// typed read cannot ignore an added field.
    /// </summary>
    [Fact]
    public async Task The_balance_payload_carries_no_warning_field()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        var balance = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{projectId}/invoice-balance");

        Assert.Equal(
            ["agreedTotal", "alreadyInvoiced", "projectId", "remaining"],
            balance.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task A_project_with_no_invoices_reports_the_full_remainder()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        var balance = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{projectId}/invoice-balance");

        Assert.Equal(0m, balance.GetProperty("alreadyInvoiced").GetDecimal());
        Assert.Equal(
            balance.GetProperty("agreedTotal").GetDecimal(),
            balance.GetProperty("remaining").GetDecimal());
    }

    // ---- Balance authorization (the decision this slice recorded) -----------

    /// <summary>
    /// `PermissionMatrix.md` §5 grants the balance read Admin <c>F</c> / Inspector <c>R</c> — Project
    /// financial-summary data, read-only and **unscoped**. This Inspector is not the one who worked
    /// the Lead, and must still be able to read it: an ownership check appearing here would be the
    /// <c>S</c>-semantics the matrix does not grant.
    /// </summary>
    [Fact]
    public async Task An_inspector_can_read_the_balance_and_is_not_scoped()
    {
        var projectId = await ConvertedProjectAsync();
        using var inspector = await InspectorClientAsync();

        var response = await inspector.GetAsync($"/api/v1/projects/{projectId}/invoice-balance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The other half of that decision, and the one that keeps it narrow: reading the summary grants
    /// an Inspector nothing over Invoices themselves. Both facts are asserted in one test so they
    /// cannot drift apart.
    /// </summary>
    [Fact]
    public async Task Reading_the_balance_grants_an_inspector_no_invoice_permissions()
    {
        var projectId = await ConvertedProjectAsync();
        using var inspector = await InspectorClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await inspector.GetAsync($"/api/v1/projects/{projectId}/invoice-balance")).StatusCode);

        var create = await inspector.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/invoices",
            new { grossAmount = 100.00m, dueDate = DateTime.UtcNow.AddDays(14) });

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_read_the_balance()
    {
        var projectId = await ConvertedProjectAsync();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync($"/api/v1/projects/{projectId}/invoice-balance");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_balance_of_an_unknown_project_is_not_found()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync("/api/v1/projects/999999999/invoice-balance");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Several invoices accumulate against one Project — FR-8.1's splitting, over HTTP.</summary>
    [Fact]
    public async Task Splitting_a_project_across_several_invoices_accumulates()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        foreach (var amount in new[] { 100.00m, 100.00m, 97.50m })
        {
            var created = await admin.PostAsJsonAsync(
                $"/api/v1/projects/{projectId}/invoices",
                new { grossAmount = amount, dueDate = DateTime.UtcNow.AddDays(14) });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var balance = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{projectId}/invoice-balance");

        Assert.Equal(297.50m, balance.GetProperty("alreadyInvoiced").GetDecimal());
        Assert.Equal(0m, balance.GetProperty("remaining").GetDecimal());
    }

    // ---- Seeding -----------------------------------------------------------

    /// <summary>
    /// Drives a Lead all the way to a converted Project through the real endpoints — including the
    /// customer's own anonymous token-link decision — so BR-2's precondition is reached the way
    /// production reaches it, never by writing a status directly.
    /// </summary>
    private async Task<int> ConvertedProjectAsync()
    {
        var inspectorId = await factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail);

        int leadId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
            var lead = Lead.Create("M. Klein", "0176 1234567", "m.klein@example.com", LeadSource.Phone);
            context.Leads.Add(lead);
            await context.SaveChangesAsync();

            var inspection = Inspection.Schedule(lead.Id, DateTime.UtcNow.AddDays(1), inspectorId);
            context.Inspections.Add(inspection);
            lead.MarkInspectionScheduled();
            lead.AssignInspector(inspectorId);
            inspection.Complete();
            lead.MarkInspectionDone();
            await context.SaveChangesAsync();

            leadId = lead.Id;
        }

        using var inspector = await InspectorClientAsync();

        var created = await inspector.PostAsJsonAsync($"/api/v1/leads/{leadId}/angebote", new { inspectionId = (int?)null });
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

        await inspector.PostAsync($"/api/v1/angebote/{angebotId}/submit-for-review", content: null);

        using var admin = await AdminClientAsync();
        await admin.PostAsync($"/api/v1/angebote/{angebotId}/approve", content: null);
        await admin.PostAsync($"/api/v1/angebote/{angebotId}/send", content: null);

        string token;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
            token = (await context.TokenLinks.SingleAsync(t => t.EntityId == angebotId)).Token;
        }

        using var anonymous = factory.CreateClient();
        await anonymous.PostAsJsonAsync($"/api/v1/public/angebote/{token}/decision", new { decision = "Approve" });

        var converted = await admin.PostAsync($"/api/v1/angebote/{angebotId}/convert-to-project", content: null);
        Assert.Equal(HttpStatusCode.Created, converted.StatusCode);

        return (await converted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

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
}
