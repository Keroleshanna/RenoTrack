using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Api.Tests.Public;

/// <summary>
/// <c>GET /api/v1/public/angebote/{token}</c> — the first anonymous endpoint in the system
/// (SRS FR-6.2, Sequence Diagram §6/§12).
/// </summary>
/// <remarks>
/// The field-level assertions here are the point of the class, not padding. Domain and Application
/// tests prove the mapping; only a real HTTP response proves what a customer's browser actually
/// receives — and this is the one endpoint where an accidentally-added field is disclosed to anyone
/// holding a forwarded email.
/// </remarks>
[Collection("Api")]
public sealed class PublicAngebotViewEndpointTests(RenoTrackApiFactory factory)
{
    [Fact]
    public async Task Anyone_holding_the_token_can_read_the_angebot_without_logging_in()
    {
        var (token, angebotNumber) = await SentAngebotAsync();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync($"/api/v1/public/angebote/{token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(angebotNumber, body.GetProperty("angebotNumber").GetString());
        Assert.Equal("Pending", body.GetProperty("decision").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("decisionAt").ValueKind);
    }

    /// <summary>
    /// No Authorization header is sent at all. Pinned explicitly because the whole design rests on
    /// this endpoint requiring no principal (Architecture.md §7.2) — if it ever started demanding
    /// one, every customer link in the field would break at once.
    /// </summary>
    [Fact]
    public async Task The_endpoint_requires_no_authorization_header()
    {
        var (token, _) = await SentAngebotAsync();
        using var anonymous = factory.CreateClient();

        Assert.Null(anonymous.DefaultRequestHeaders.Authorization);
        var response = await anonymous.GetAsync($"/api/v1/public/angebote/{token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The approved exclusion list, asserted against the raw JSON rather than a typed DTO — a typed
    /// read would silently ignore any extra field, which is exactly the failure this must catch.
    /// Staff identities and the catalogue trace link are the ones with real disclosure cost.
    /// </summary>
    [Fact]
    public async Task The_public_response_exposes_no_internal_field()
    {
        var (token, _) = await SentAngebotAsync();
        using var anonymous = factory.CreateClient();

        var raw = await (await anonymous.GetAsync($"/api/v1/public/angebote/{token}")).Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(raw).RootElement;

        Assert.Equal(
            ["angebotNumber", "decision", "decisionAt", "grossTotal", "netTotal", "sections", "vatBreakdown"],
            body.EnumerateObject().Select(p => p.Name).Order());

        foreach (var forbidden in new[]
                 {
                     "id", "leadId", "inspectionId", "status", "createdByInspectorId",
                     "reviewedByAdminId", "createdAt", "sentAt", "catalogItemId", "sortOrder", "vatRate",
                 })
        {
            Assert.DoesNotContain($"\"{forbidden}\"", raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Section_and_item_objects_carry_only_the_approved_fields()
    {
        var (token, _) = await SentAngebotAsync();
        using var anonymous = factory.CreateClient();

        var body = await (await anonymous.GetAsync($"/api/v1/public/angebote/{token}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var section = body.GetProperty("sections").EnumerateArray().First();
        Assert.Equal(["items", "subtotal", "title"], section.EnumerateObject().Select(p => p.Name).Order());

        var item = section.GetProperty("items").EnumerateArray().First();
        Assert.Equal(
            ["description", "lineTotal", "quantity", "specification", "unit", "unitPrice"],
            item.EnumerateObject().Select(p => p.Name).Order());
    }

    /// <summary>
    /// The rate is the printable percentage (Wireframe A3's "zzgl. 19% MwSt"), never the internal
    /// enum member name — which D61's JsonStringEnumConverter would otherwise have produced.
    /// </summary>
    [Fact]
    public async Task Vat_lines_carry_the_percentage_not_the_internal_enum_name()
    {
        var (token, _) = await SentAngebotAsync();
        using var anonymous = factory.CreateClient();

        var body = await (await anonymous.GetAsync($"/api/v1/public/angebote/{token}"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var line = body.GetProperty("vatBreakdown").EnumerateArray().First();
        Assert.Equal(["rate", "vatAmount"], line.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(19m, line.GetProperty("rate").GetDecimal());
    }

    // ---- Sequence Diagram §12 ----------------------------------------------

    /// <summary>
    /// The token must not appear in any field this codebase authors — <c>title</c> and
    /// <c>detail</c> — because those are also what gets logged at Warning (D59).
    ///
    /// <c>instance</c> is the deliberate exception: ASP.NET sets it to the request path, which is
    /// the very line the caller just sent, so echoing it back to that same caller discloses nothing
    /// they do not already hold. It is asserted here rather than ignored, so that if the field ever
    /// stops mirroring the request it is a decision rather than a surprise.
    /// </summary>
    [Fact]
    public async Task An_unknown_token_is_a_not_found_and_the_token_appears_in_no_authored_field()
    {
        const string token = "definitely-not-a-real-token";
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync($"/api/v1/public/angebote/{token}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(token, problem.GetProperty("title").GetString()!, StringComparison.Ordinal);
        Assert.DoesNotContain(token, problem.GetProperty("detail").GetString()!, StringComparison.Ordinal);
        Assert.Equal("This link is not valid.", problem.GetProperty("detail").GetString());

        Assert.Contains(token, problem.GetProperty("instance").GetString()!, StringComparison.Ordinal);
    }

    /// <summary>410, not 404 — Sequence Diagram §6 names the status and §12 requires the reason to be specific.</summary>
    [Fact]
    public async Task An_expired_token_is_gone_with_problem_details()
    {
        var (token, _) = await SentAngebotAsync();
        await ExpireTokenAsync(token);
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync($"/api/v1/public/angebote/{token}");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Gone", problem.GetProperty("title").GetString());
        Assert.Equal(410, problem.GetProperty("status").GetInt32());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    /// <summary>BR-4: single use restricts decisions, not viewing.</summary>
    [Fact]
    public async Task A_used_token_can_still_be_viewed()
    {
        var (token, angebotNumber) = await SentAngebotAsync();
        await MarkTokenUsedAsync(token);
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync($"/api/v1/public/angebote/{token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(angebotNumber, body.GetProperty("angebotNumber").GetString());
    }

    // ---- Helpers -----------------------------------------------------------

    private async Task ExpireTokenAsync(string token)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        // Set through the column rather than the aggregate: TokenLink deliberately refuses to be
        // constructed already-expired, and reflecting into ExpiresAt would be using reflection to
        // step around a guard, which CLAUDE.md §14 permits only for simulating assigned ids.
        await context.Database.ExecuteSqlAsync(
            $"UPDATE TokenLinks SET ExpiresAt = {DateTime.UtcNow.AddDays(-1)} WHERE Token = {token}");
    }

    private async Task MarkTokenUsedAsync(string token)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var link = await context.TokenLinks.SingleAsync(t => t.Token == token);
        link.MarkUsed();
        await context.SaveChangesAsync();
    }

    private async Task<(string Token, string AngebotNumber)> SentAngebotAsync()
    {
        var inspectorId = await factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail);

        int leadId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
            var lead = Lead.Create("Public view lead", "0176 5550004", $"public-{Guid.NewGuid():N}@example.com", LeadSource.Phone);
            context.Leads.Add(lead);
            await context.SaveChangesAsync();

            lead.AssignInspector(inspectorId);
            lead.MarkInspectionScheduled();
            lead.MarkInspectionDone();
            await context.SaveChangesAsync();

            leadId = lead.Id;
        }

        int angebotId;
        string angebotNumber;
        using (var inspector = await ClientAsync(RenoTrackApiFactory.InspectorEmail, RenoTrackApiFactory.InspectorPassword))
        {
            var created = await inspector.PostAsJsonAsync($"/api/v1/leads/{leadId}/angebote", new { inspectionId = (int?)null });
            var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
            angebotId = createdBody.GetProperty("id").GetInt32();
            angebotNumber = createdBody.GetProperty("angebotNumber").GetString()!;

            var section = await inspector.PostAsJsonAsync(
                $"/api/v1/angebote/{angebotId}/sections", new { title = "Pos. 1 Abriss", sortOrder = 1 });
            var sectionId = (await section.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

            await inspector.PostAsJsonAsync($"/api/v1/angebote/{angebotId}/items", new
            {
                sectionId,
                catalogItemId = (int?)null,
                description = "Wände abbrechen",
                specification = "inkl. Entsorgung",
                unitCode = "m2",
                quantity = 10m,
                unitPrice = 25.00m,
                vatRate = "Standard",
            });

            await inspector.PostAsync($"/api/v1/angebote/{angebotId}/submit-for-review", content: null);
        }

        using (var admin = await ClientAsync(RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword))
        {
            await admin.PostAsync($"/api/v1/angebote/{angebotId}/approve", content: null);
            var sent = await admin.PostAsync($"/api/v1/angebote/{angebotId}/send", content: null);
            Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        }

        using var readScope = factory.Services.CreateScope();
        var readContext = readScope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var token = (await readContext.TokenLinks.SingleAsync(t => t.EntityId == angebotId)).Token;

        return (token, angebotNumber);
    }

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
