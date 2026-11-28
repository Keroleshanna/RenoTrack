using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Api.ErrorHandling;
using RenoTrack.Application.Common;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Api.Tests.Angebote;

/// <summary>
/// Admin editing of an Inspector-owned Angebot and the freshness contract, over real HTTP and
/// real LocalDB (<b>D115</b>).
/// </summary>
/// <remarks>
/// Per D58 this pins what the API layer adds: that the role gates now admit an Admin to every
/// builder endpoint and to submit, that "return for correction" admits only an Admin, that
/// <c>expectedVersion</c> is required and binds from the body or the query string, and that a stale
/// one leaves the API as a 409 whose detail names nothing. The business rules beneath — the edit lock,
/// the audit-only-for-Admins rule — are covered exhaustively by the Domain and Application tests and are
/// sampled here only where crossing HTTP could break them.
/// </remarks>
[Collection("Api")]
public sealed class AdminAngebotEditingEndpointsTests(RenoTrackApiFactory factory)
{
    private static readonly object ItemValues = new
    {
        description = "Fliesen verlegen",
        specification = (string?)null,
        unitCode = "m2",
        quantity = 4m,
        unitPrice = 50.00m,
        vatRate = "Standard",
    };

    // ---- Every builder endpoint admits an Admin -----------------------------

    [Fact]
    public async Task Admin_can_add_update_and_remove_lines_on_an_inspectors_draft()
    {
        var (inspector, angebotId) = await DraftAngebotAsync();
        var (sectionId, itemId) = await AddSectionWithItemAsync(inspector, angebotId);
        using var admin = await AdminClientAsync();

        var added = await admin.PostVersionedAsync(angebotId, "items", new
        {
            sectionId,
            catalogItemId = (int?)null,
            description = "Silikonfugen",
            specification = (string?)null,
            unitCode = "lfm",
            quantity = 3m,
            unitPrice = 8.00m,
            vatRate = "Standard",
        });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        var updated = await admin.PutVersionedAsync(angebotId, $"items/{itemId}", ItemValues);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var addedItemId = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("item").GetProperty("id").GetInt32();
        var removedItem = await admin.DeleteVersionedAsync(angebotId, $"items/{addedItemId}");
        Assert.Equal(HttpStatusCode.OK, removedItem.StatusCode);

        var angebot = await ReadAngebotAsync(admin, angebotId);
        var line = angebot.GetProperty("sections")[0].GetProperty("items")[0];
        Assert.Equal("Fliesen verlegen", line.GetProperty("description").GetString());
        Assert.Equal(200.00m, angebot.GetProperty("netTotal").GetDecimal());

        // Authorship never moves.
        Assert.Equal(await factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail), angebot.GetProperty("createdByInspectorId").GetInt32());

        var removedSection = await admin.DeleteVersionedAsync(angebotId, $"sections/{sectionId}");
        Assert.Equal(HttpStatusCode.OK, removedSection.StatusCode);
    }

    [Fact]
    public async Task Each_admin_builder_mutation_is_audited_and_the_inspectors_own_are_not()
    {
        var (inspector, angebotId) = await DraftAngebotAsync();
        var (_, itemId) = await AddSectionWithItemAsync(inspector, angebotId); // two Inspector mutations
        using var admin = await AdminClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.PutVersionedAsync(angebotId, $"items/{itemId}", ItemValues)).StatusCode);

        var entries = await AuditEntriesAsync(angebotId, AuditAction.AngebotEditedByAdmin);
        var entry = Assert.Single(entries);
        Assert.Equal(await factory.GetUserIdAsync(RenoTrackApiFactory.AdminEmail), entry.PerformedByUserId);
        Assert.StartsWith("operation=ItemUpdated; sectionId=", entry.Details);
        Assert.DoesNotContain("Fliesen", entry.Details);
        Assert.DoesNotContain("50", entry.Details);
    }

    [Fact]
    public async Task Admin_still_cannot_create_or_duplicate()
    {
        var (_, angebotId) = await DraftAngebotAsync();
        using var admin = await AdminClientAsync();

        var duplicate = await admin.PostAsJsonAsync($"/api/v1/angebote/{angebotId}/duplicate", new { targetLeadId = 1 });

        // The role gate: an empty body, not an ownership ProblemDetails.
        Assert.Equal(HttpStatusCode.Forbidden, duplicate.StatusCode);
        Assert.Empty(await duplicate.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task The_edit_lock_still_refuses_an_admin_while_in_review()
    {
        var (inspector, angebotId) = await DraftAngebotAsync();
        await AddSectionWithItemAsync(inspector, angebotId);
        Assert.Equal(HttpStatusCode.OK, (await inspector.PostVersionedAsync(angebotId, "submit-for-review")).StatusCode);
        using var admin = await AdminClientAsync();

        var response = await admin.PostVersionedAsync(angebotId, "sections", new { title = "Zu spät", sortOrder = 2 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---- Return for correction (A3) -------------------------------------------

    [Fact]
    public async Task Admin_returns_an_angebot_for_correction_corrects_resubmits_and_approves_it()
    {
        var (inspector, angebotId) = await DraftAngebotAsync();
        var (_, itemId) = await AddSectionWithItemAsync(inspector, angebotId);
        Assert.Equal(HttpStatusCode.OK, (await inspector.PostVersionedAsync(angebotId, "submit-for-review")).StatusCode);
        using var admin = await AdminClientAsync();

        var returned = await admin.PostVersionedAsync(angebotId, "return-for-correction");
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        Assert.Equal("Draft", (await returned.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.OK, (await admin.PutVersionedAsync(angebotId, $"items/{itemId}", ItemValues)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostVersionedAsync(angebotId, "submit-for-review")).StatusCode);

        // The same Admin may approve their own correction — no four-eyes rule.
        var approved = await admin.PostVersionedAsync(angebotId, "approve");
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal("ApprovedInternally", (await approved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var entry = Assert.Single(await AuditEntriesAsync(angebotId, AuditAction.AngebotReturnedForCorrection));
        Assert.Equal(await factory.GetUserIdAsync(RenoTrackApiFactory.AdminEmail), entry.PerformedByUserId);
        Assert.Null(entry.Details);
    }

    [Fact]
    public async Task An_inspector_cannot_return_an_angebot_for_correction()
    {
        var (inspector, angebotId) = await DraftAngebotAsync();
        await AddSectionWithItemAsync(inspector, angebotId);
        Assert.Equal(HttpStatusCode.OK, (await inspector.PostVersionedAsync(angebotId, "submit-for-review")).StatusCode);

        var response = await inspector.PostVersionedAsync(angebotId, "return-for-correction");

        // The Admin-only role gate: an empty body.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("InReview", (await ReadAngebotAsync(inspector, angebotId)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Returning_an_angebot_that_is_not_in_review_is_a_conflict()
    {
        var (_, angebotId) = await DraftAngebotAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostVersionedAsync(angebotId, "return-for-correction");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_return_an_angebot_for_correction()
    {
        var (_, angebotId) = await DraftAngebotAsync();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostVersionedAsync(angebotId, "return-for-correction", expectedVersion: 0);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- expectedVersion: required, and a stale one is a 409 --------------------

    [Fact]
    public async Task The_angebot_read_carries_a_version_that_moves_with_every_write()
    {
        var (inspector, angebotId) = await DraftAngebotAsync();
        var before = await inspector.CurrentVersionAsync(angebotId);

        await AddSectionWithItemAsync(inspector, angebotId);

        Assert.Equal(before + 2, await inspector.CurrentVersionAsync(angebotId));
    }

    /// <summary>
    /// The case the cross-request contract exists for: an Admin's screen loaded before the Inspector's
    /// edit. Without <c>expectedVersion</c> the Admin's <c>PUT</c> would silently revert the newer value.
    /// </summary>
    [Fact]
    public async Task A_stale_version_cannot_overwrite_newer_content()
    {
        var (inspector, angebotId) = await DraftAngebotAsync();
        var (_, itemId) = await AddSectionWithItemAsync(inspector, angebotId);
        using var admin = await AdminClientAsync();
        var adminsView = await admin.CurrentVersionAsync(angebotId);

        Assert.Equal(HttpStatusCode.OK, (await inspector.PutVersionedAsync(angebotId, $"items/{itemId}", new
        {
            description = "Neuere Fassung",
            specification = (string?)null,
            unitCode = "m2",
            quantity = 9m,
            unitPrice = 50.00m,
            vatRate = "Standard",
        })).StatusCode);

        var stale = await admin.PutVersionedAsync(angebotId, $"items/{itemId}", adminsView, ItemValues);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var problem = await stale.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(GenericConflictResponseAttribute.Detail, problem.GetProperty("detail").GetString());

        var line = (await ReadAngebotAsync(admin, angebotId)).GetProperty("sections")[0].GetProperty("items")[0];
        Assert.Equal("Neuere Fassung", line.GetProperty("description").GetString());
        Assert.Empty(await AuditEntriesAsync(angebotId, AuditAction.AngebotEditedByAdmin));
    }

    public static TheoryData<string> VersionedWrites() =>
        ["sections", "items", "update-item", "delete-item", "delete-section", "submit-for-review", "return-for-correction", "approve", "request-changes"];

    /// <summary>
    /// Every write in the contract refuses a stale version with the same generic 409, whose detail
    /// names no id, table or version (D59, D115).
    /// </summary>
    [Theory]
    [MemberData(nameof(VersionedWrites))]
    public async Task Every_versioned_write_refuses_a_stale_version_with_a_generic_409(string write)
    {
        var (inspector, angebotId) = await DraftAngebotAsync();
        var (sectionId, itemId) = await AddSectionWithItemAsync(inspector, angebotId);
        using var admin = await AdminClientAsync();

        if (write is "return-for-correction" or "approve" or "request-changes")
        {
            Assert.Equal(HttpStatusCode.OK, (await inspector.PostVersionedAsync(angebotId, "submit-for-review")).StatusCode);
        }

        var stale = await admin.CurrentVersionAsync(angebotId) - 1;
        var response = write switch
        {
            "sections" => await admin.PostVersionedAsync(angebotId, "sections", stale, new { title = "X", sortOrder = 2 }),
            "items" => await admin.PostVersionedAsync(angebotId, "items", stale, new
            {
                sectionId, catalogItemId = (int?)null, description = "X", specification = (string?)null,
                unitCode = "Stk", quantity = 1m, unitPrice = 1.00m, vatRate = "Standard",
            }),
            "update-item" => await admin.PutVersionedAsync(angebotId, $"items/{itemId}", stale, ItemValues),
            "delete-item" => await admin.DeleteVersionedAsync(angebotId, $"items/{itemId}", stale),
            "delete-section" => await admin.DeleteVersionedAsync(angebotId, $"sections/{sectionId}", stale),
            "request-changes" => await admin.PostVersionedAsync(angebotId, "request-changes", stale, new { comment = "Bitte prüfen." }),
            _ => await admin.PostVersionedAsync(angebotId, write, stale),
        };

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var detail = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString()!;
        Assert.Equal(GenericConflictResponseAttribute.Detail, detail);
        Assert.DoesNotContain(angebotId.ToString(), detail);
    }

    [Fact]
    public async Task A_body_without_expectedVersion_is_a_bad_request()
    {
        var (inspector, angebotId) = await DraftAngebotAsync();

        var response = await inspector.PostAsJsonAsync($"/api/v1/angebote/{angebotId}/sections", new { title = "Ohne Version", sortOrder = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await inspector.CurrentVersionAsync(angebotId));
    }

    [Fact]
    public async Task A_bodiless_write_without_expectedVersion_is_a_bad_request()
    {
        var (inspector, angebotId) = await DraftAngebotAsync();
        await AddSectionWithItemAsync(inspector, angebotId);

        var response = await inspector.PostAsJsonAsync($"/api/v1/angebote/{angebotId}/submit-for-review", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Draft", (await ReadAngebotAsync(inspector, angebotId)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_delete_without_the_expectedVersion_query_parameter_is_a_bad_request()
    {
        var (inspector, angebotId) = await DraftAngebotAsync();
        var (_, itemId) = await AddSectionWithItemAsync(inspector, angebotId);

        var response = await inspector.DeleteAsync($"/api/v1/angebote/{angebotId}/items/{itemId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single((await ReadAngebotAsync(inspector, angebotId)).GetProperty("sections")[0].GetProperty("items").EnumerateArray());
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

    private async Task<(HttpClient Client, int AngebotId)> DraftAngebotAsync()
    {
        var inspectorId = await factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail);

        int leadId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
            var lead = Lead.Create("Admin editing lead", "0176 5550115", "admin-editing@example.com", LeadSource.Phone);
            context.Leads.Add(lead);
            await context.SaveChangesAsync();

            lead.AssignInspector(inspectorId);
            lead.MarkInspectionScheduled();
            lead.MarkInspectionDone();
            await context.SaveChangesAsync();
            leadId = lead.Id;
        }

        var client = await InspectorClientAsync();
        var response = await client.PostAsJsonAsync($"/api/v1/leads/{leadId}/angebote", new { inspectionId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (client, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32());
    }

    private static async Task<(int SectionId, int ItemId)> AddSectionWithItemAsync(HttpClient client, int angebotId)
    {
        var section = await client.PostVersionedAsync(angebotId, "sections", new { title = "Pos. 1", sortOrder = 1 });
        Assert.Equal(HttpStatusCode.Created, section.StatusCode);
        var sectionId = (await section.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var item = await client.PostVersionedAsync(angebotId, "items", new
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
        Assert.Equal(HttpStatusCode.Created, item.StatusCode);

        return (sectionId, (await item.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("item").GetProperty("id").GetInt32());
    }

    private static async Task<JsonElement> ReadAngebotAsync(HttpClient client, int angebotId)
    {
        var response = await client.GetAsync($"/api/v1/angebote/{angebotId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<List<AuditEntry>> AuditEntriesAsync(int angebotId, AuditAction action)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        return await context.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == nameof(Angebot) && a.EntityId == angebotId && a.Action == action)
            .Select(a => new AuditEntry(a.PerformedByUserId, a.Details))
            .ToListAsync();
    }

    private sealed record AuditEntry(int? PerformedByUserId, string? Details);
}
