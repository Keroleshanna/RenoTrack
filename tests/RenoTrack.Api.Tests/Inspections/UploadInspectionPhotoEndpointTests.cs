using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Domain.Entities;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Api.Tests.Inspections;

/// <summary>
/// <c>POST /api/v1/inspections/{id}/photos</c> (SRS FR-3.2). Inspector only — and specifically the
/// <em>assigned</em> Inspector, per <c>PermissionMatrix.md</c> §2's "S".
/// </summary>
/// <remarks>
/// The assertions that carry this slice are the ones about the filesystem, not the status codes: a
/// rejected upload must leave no file behind, which a status-code check alone would not catch if the
/// write and the guard were ever reordered.
/// </remarks>
[Collection("Api")]
public sealed class UploadInspectionPhotoEndpointTests(RenoTrackApiFactory factory)
{
    [Fact]
    public async Task Assigned_inspector_can_upload_a_photo()
    {
        var inspectionId = await SeedInspectionAsync();
        using var client = await InspectorClientAsync();

        var response = await UploadAsync(client, inspectionId, "bathroom.jpg");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var fileUrl = body.GetProperty("fileUrl").GetString()!;

        Assert.StartsWith($"inspections/{inspectionId}/", fileUrl);
        Assert.EndsWith(".jpg", fileUrl);
        Assert.Equal("On-site", body.GetProperty("caption").GetString());
    }

    [Fact]
    public async Task The_uploaded_file_exists_on_disk_and_the_row_exists_in_the_database()
    {
        var inspectionId = await SeedInspectionAsync();
        using var client = await InspectorClientAsync();

        var response = await UploadAsync(client, inspectionId, "bathroom.jpg");
        var fileUrl = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fileUrl").GetString()!;

        Assert.True(File.Exists(PhysicalPath(fileUrl)));
        Assert.Equal(PhotoContent, await File.ReadAllTextAsync(PhysicalPath(fileUrl)));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var inspection = await dbContext.Inspections
            .AsNoTracking()
            .Include(i => i.Photos)
            .SingleAsync(i => i.Id == inspectionId);

        Assert.Contains(inspection.Photos, photo => photo.FileUrl == fileUrl);
    }

    // ---- the side-effect ordering rule (CLAUDE.md §12) ----

    [Fact]
    public async Task Uploading_to_a_completed_inspection_is_rejected_and_writes_no_file()
    {
        var inspectionId = await SeedInspectionAsync(completed: true);
        using var client = await InspectorClientAsync();

        var filesBefore = ExistingFileCount();

        var response = await UploadAsync(client, inspectionId, "late.jpg");

        // BR-10 rejects it, mapped to 409 by D59.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // The assertion that actually pins §12: the status code alone would still be 409 if the
        // file were written first and the guard ran afterwards, leaving an orphan every time.
        Assert.Equal(filesBefore, ExistingFileCount());
    }

    [Fact]
    public async Task A_non_owning_inspector_is_refused_and_writes_no_file()
    {
        var otherInspectorId = await factory.GetUserIdAsync(RenoTrackApiFactory.SecondInspectorEmail);
        var inspectionId = await SeedInspectionAsync(inspectorId: otherInspectorId);
        using var client = await InspectorClientAsync();

        var filesBefore = ExistingFileCount();

        var response = await UploadAsync(client, inspectionId, "notmine.jpg");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(filesBefore, ExistingFileCount());
    }

    [Fact]
    public async Task A_pathological_extension_is_rejected_before_any_file_is_written()
    {
        var inspectionId = await SeedInspectionAsync();
        using var client = await InspectorClientAsync();

        var filesBefore = ExistingFileCount();

        var response = await UploadAsync(client, inspectionId, "photo." + new string('x', 40));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(filesBefore, ExistingFileCount());
    }

    [Fact]
    public async Task A_hostile_filename_cannot_influence_the_storage_directory()
    {
        var inspectionId = await SeedInspectionAsync();
        using var client = await InspectorClientAsync();

        var response = await UploadAsync(client, inspectionId, "../../evil.jpg");

        // Accepted, because Path.GetExtension reduces this to ".jpg" — the traversal never reaches
        // the storage key, which is built from the inspection id and a GUID.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var fileUrl = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fileUrl").GetString()!;

        Assert.StartsWith($"inspections/{inspectionId}/", fileUrl);
        Assert.DoesNotContain("..", fileUrl);
        Assert.DoesNotContain("evil", fileUrl);

        // And it really landed inside the configured root, not beside it.
        Assert.True(File.Exists(PhysicalPath(fileUrl)));
        Assert.StartsWith(factory.StorageRoot, Path.GetFullPath(PhysicalPath(fileUrl)), StringComparison.Ordinal);
    }

    // ---- authorization ----

    /// <summary>
    /// Inverts <c>Schedule</c>: <c>PermissionMatrix.md</c> §2 grants Admin nothing here, so that
    /// evidence comes from whoever was actually on site.
    /// </summary>
    /// <remarks>
    /// The empty-body assertion is load-bearing, and was added during the Phase 4 closeout review after
    /// the same gap was found and fixed in Slices 9 and 10. Two independent layers can produce this
    /// 403 — the action's <c>[Authorize(Roles = Roles.Inspector)]</c> and
    /// <c>EnsureInspectionOwnership</c> inside the handler (an Admin is never the assigned Inspector) —
    /// and the class-level attribute admits both roles, so it does not close the gap. A status-code-only
    /// check therefore passed even with the action's role requirement removed, leaving that role gate
    /// pinned by no test at all.
    ///
    /// A role-gate rejection is emitted by the authorization middleware with <b>no body</b>; a
    /// <c>ForbiddenException</c> reaching the D59 handler produces a ProblemDetails document. Asserting
    /// the body is empty is what pins <em>which</em> layer rejected, making this test detect the
    /// attribute-vs-guard drift CLAUDE.md §22 requires both layers to be kept against.
    /// </remarks>
    [Fact]
    public async Task An_admin_is_forbidden_by_the_role_gate_before_reaching_the_handler()
    {
        var inspectionId = await SeedInspectionAsync();
        using var client = await ClientAsync(RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

        var response = await UploadAsync(client, inspectionId, "admin.jpg");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unauthenticated_request_is_rejected()
    {
        var inspectionId = await SeedInspectionAsync();
        using var client = factory.CreateClient();

        var response = await UploadAsync(client, inspectionId, "anon.jpg");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Uploading_to_an_unknown_inspection_returns_404()
    {
        using var client = await InspectorClientAsync();

        var response = await UploadAsync(client, inspectionId: 999999, "ghost.jpg");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Omitting the file part entirely is an ordinary client mistake and must not become a 500.
    /// </summary>
    /// <remarks>
    /// The handler dereferences <c>request.File</c> directly, so this test exists to pin the
    /// framework behaviour that keeps that safe: <c>Nullable</c> is enabled solution-wide and
    /// <c>SuppressImplicitRequiredAttributeForNonNullableReferenceTypes</c> is not set, so
    /// <c>[ApiController]</c> treats the non-nullable <c>IFormFile</c> as implicitly required and
    /// rejects the request before the action runs. That was reasoning, not evidence, until this test
    /// — and this project has twice had a "the framework handles it" assumption falsified
    /// (<c>File.Delete</c>'s partial idempotency, and D62's mistyped id surfacing as a 500).
    /// </remarks>
    [Fact]
    public async Task Omitting_the_file_part_is_a_bad_request_not_a_server_error()
    {
        var inspectionId = await SeedInspectionAsync();
        using var client = await InspectorClientAsync();

        using var form = new MultipartFormDataContent
        {
            { new StringContent("On-site"), "caption" },
        };

        var response = await client.PostAsync($"/api/v1/inspections/{inspectionId}/photos", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // ---- helpers ----

    private const string PhotoContent = "fake-image-bytes";

    private string PhysicalPath(string fileUrl) =>
        Path.Combine(factory.StorageRoot, fileUrl.Replace('/', Path.DirectorySeparatorChar));

    private int ExistingFileCount() =>
        Directory.Exists(factory.StorageRoot)
            ? Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories).Length
            : 0;

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, int inspectionId, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(PhotoContent));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

        form.Add(file, "file", fileName);
        form.Add(new StringContent("On-site"), "caption");

        return await client.PostAsync($"/api/v1/inspections/{inspectionId}/photos", form);
    }

    private Task<HttpClient> InspectorClientAsync() =>
        ClientAsync(RenoTrackApiFactory.InspectorEmail, RenoTrackApiFactory.InspectorPassword);

    private async Task<HttpClient> ClientAsync(string email, string password)
    {
        var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());

        return client;
    }

    /// <summary>
    /// Seeds an Inspection directly, since no endpoint can produce a completed one and scheduling
    /// through the API would need an Admin plus a Lead in exactly the right state.
    /// </summary>
    private async Task<int> SeedInspectionAsync(int? inspectorId = null, bool completed = false)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var assignedTo = inspectorId ?? await factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail);

        var lead = Lead.Create($"Photo {Guid.NewGuid():N}", "+49 151 88888888", "photo@example.de", Domain.Enums.LeadSource.Phone);
        dbContext.Leads.Add(lead);
        await dbContext.SaveChangesAsync();

        var inspection = Inspection.Schedule(lead.Id, new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc), assignedTo);

        if (completed)
        {
            inspection.Complete();
        }

        dbContext.Inspections.Add(inspection);
        await dbContext.SaveChangesAsync();

        return inspection.Id;
    }
}
