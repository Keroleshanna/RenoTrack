using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Api.Tests.Invoices;

/// <summary>
/// Phase 14 Slice 3b (D114) through the real HTTP pipeline: the customer's token link serves the
/// archived PDF, byte for byte, for every status. The link is never consumed. 404, 410, 409 and 500
/// mean what the Admin download's do. No response carries the token, the storage key or an internal
/// id.
/// </summary>
/// <remarks>
/// The real renderer, real LocalDB and real disk, as in the 3a tests beside this file. What these
/// add over the Application tests is the wire: anonymity, status codes, headers, ProblemDetails
/// bodies, and that the bytes served are the bytes on disk.
/// </remarks>
public sealed partial class InvoiceEndpointsTests
{
    private static string PublicDocumentUrl(string token) => $"/api/v1/public/invoices/{token}/document";

    private async Task<(int InvoiceId, string Token, string DocumentFileUrl)> ArchivedInvoiceWithTokenAsync()
    {
        var (invoiceId, token) = await SentInvoiceAsync();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var key = (await context.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId)).DocumentFileUrl;

        Assert.NotNull(key);
        return (invoiceId, token, key);
    }

    // ---- The customer's download ----------------------------------------------

    [Fact]
    public async Task The_customer_downloads_exactly_the_archived_bytes_without_logging_in()
    {
        var (invoiceId, token, key) = await ArchivedInvoiceWithTokenAsync();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(PublicDocumentUrl(token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(
            $"Rechnung-{await InvoiceNumberAsync(invoiceId)}.pdf",
            response.Content.Headers.ContentDisposition.FileName);
        Assert.Equal(await File.ReadAllBytesAsync(DiskPathOf(key)), await response.Content.ReadAsByteArrayAsync());
    }

    /// <summary>The Admin and the customer receive the one issued document (BR-15).</summary>
    [Fact]
    public async Task The_customer_and_the_admin_receive_identical_bytes()
    {
        var (invoiceId, token, _) = await ArchivedInvoiceWithTokenAsync();
        using var anonymous = factory.CreateClient();
        using var admin = await AdminClientAsync();

        var customerBytes = await (await anonymous.GetAsync(PublicDocumentUrl(token))).Content.ReadAsByteArrayAsync();
        var adminBytes = await (await admin.GetAsync($"/api/v1/invoices/{invoiceId}/document")).Content.ReadAsByteArrayAsync();

        Assert.Equal(adminBytes, customerBytes);
    }

    [Fact]
    public async Task The_public_download_is_never_cached_and_never_sniffed()
    {
        var (_, token, _) = await ArchivedInvoiceWithTokenAsync();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(PublicDocumentUrl(token));

        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    /// <summary>
    /// The Product Owner's decision: a voided invoice stays downloadable, because the archive is the
    /// issued document. Paid likewise. The bytes and the file on disk are unchanged.
    /// </summary>
    [Theory]
    [InlineData("Paid")]
    [InlineData("Void")]
    public async Task The_customer_still_downloads_the_archive_after_payment_or_void(string outcome)
    {
        var (invoiceId, token, key) = await ArchivedInvoiceWithTokenAsync();
        var archivedBefore = await File.ReadAllBytesAsync(DiskPathOf(key));
        using var admin = await AdminClientAsync();

        var transition = outcome == "Paid"
            ? await admin.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/mark-paid", new { paidAt = DateTime.UtcNow, method = "BankTransfer" })
            : await admin.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/void", new { reason = "Issued against the wrong Project." });
        Assert.Equal(HttpStatusCode.OK, transition.StatusCode);

        using var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync(PublicDocumentUrl(token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(archivedBefore, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(archivedBefore, await File.ReadAllBytesAsync(DiskPathOf(key)));
    }

    /// <summary>
    /// Correcting the customer's address after sending reaches the next invoice, never this one
    /// (D112, D113). The customer's download proves nothing was re-rendered.
    /// </summary>
    [Fact]
    public async Task An_address_correction_after_sending_leaves_the_customers_download_unchanged()
    {
        var (invoiceId, token, key) = await ArchivedInvoiceWithTokenAsync();
        var archivedBefore = await File.ReadAllBytesAsync(DiskPathOf(key));
        using var admin = await AdminClientAsync();

        var customerId = await CustomerIdOfAsync(invoiceId);
        var corrected = await admin.PutAsJsonAsync(
            $"/api/v1/customers/{customerId}/address", new { address = "Neue Straße 2, 00000 Anderort" });
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);

        using var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync(PublicDocumentUrl(token));

        Assert.Equal(archivedBefore, await response.Content.ReadAsByteArrayAsync());
        Assert.Single(Directory.GetFiles(InvoiceDirectory(invoiceId)));
    }

    /// <summary>
    /// A download is a read: the link is never consumed (PermissionMatrix §7, Sequence §12), however
    /// often the customer downloads, and the page stays readable afterwards.
    /// </summary>
    [Fact]
    public async Task Downloading_never_consumes_the_link()
    {
        var (_, token, _) = await ArchivedInvoiceWithTokenAsync();
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(PublicDocumentUrl(token))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(PublicDocumentUrl(token))).StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var link = await context.TokenLinks.AsNoTracking().SingleAsync(t => t.Token == token);
        Assert.Null(link.UsedAt);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/v1/public/invoices/{token}")).StatusCode);
    }

    // ---- The read's flag -------------------------------------------------------

    [Fact]
    public async Task The_public_read_reports_an_archived_document_as_available()
    {
        var (_, token, _) = await ArchivedInvoiceWithTokenAsync();
        using var anonymous = factory.CreateClient();

        var body = await anonymous.GetFromJsonAsync<JsonElement>($"/api/v1/public/invoices/{token}");

        Assert.True(body.GetProperty("documentAvailable").GetBoolean());
    }

    [Fact]
    public async Task The_public_read_reports_no_document_for_an_invoice_without_an_archive()
    {
        var (invoiceId, token, _) = await ArchivedInvoiceWithTokenAsync();
        await ForgetArchiveAsync(invoiceId);
        using var anonymous = factory.CreateClient();

        var body = await anonymous.GetFromJsonAsync<JsonElement>($"/api/v1/public/invoices/{token}");

        Assert.False(body.GetProperty("documentAvailable").GetBoolean());
    }

    // ---- Refusals ----------------------------------------------------------------

    [Fact]
    public async Task An_unknown_token_is_not_found_and_echoes_nothing()
    {
        const string secret = "a-secret-invoice-document-token";
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(PublicDocumentUrl(secret));
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.DoesNotContain(secret, raw, StringComparison.Ordinal);
    }

    /// <summary>
    /// An Angebot token on the document route is the same 404, with the same text, as an unknown
    /// token. The read and the download answer an invalid link identically.
    /// </summary>
    [Fact]
    public async Task An_angebot_token_is_indistinguishable_from_an_unknown_one()
    {
        var projectId = await ConvertedProjectAsync();
        string angebotToken;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
            var project = await context.Projects.SingleAsync(p => p.Id == projectId);
            angebotToken = (await context.TokenLinks.SingleAsync(
                t => t.EntityType == TokenLinkEntityType.Angebot && t.EntityId == project.AngebotId)).Token;
        }

        using var anonymous = factory.CreateClient();
        var wrongType = await anonymous.GetAsync(PublicDocumentUrl(angebotToken));
        var unknown = await anonymous.GetAsync(PublicDocumentUrl("not-a-real-token"));

        Assert.Equal(HttpStatusCode.NotFound, wrongType.StatusCode);
        Assert.Equal(
            Detail(await unknown.Content.ReadAsStringAsync()),
            Detail(await wrongType.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task An_expired_link_is_gone_for_the_download_and_the_read_alike()
    {
        var (_, token, _) = await ArchivedInvoiceWithTokenAsync();
        await ExpireAsync(token);
        using var anonymous = factory.CreateClient();

        var document = await anonymous.GetAsync(PublicDocumentUrl(token));
        var read = await anonymous.GetAsync($"/api/v1/public/invoices/{token}");

        Assert.Equal(HttpStatusCode.Gone, document.StatusCode);
        Assert.Equal(HttpStatusCode.Gone, read.StatusCode);
        Assert.DoesNotContain(token, await document.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// An Invoice sent before Slice 3a has no archive and gets none (D113). Produced here by clearing
    /// the key with SQL, since the aggregate cannot be sent without one. The refusal names nothing:
    /// no invoice number, no id, no key.
    /// </summary>
    [Fact]
    public async Task An_invoice_without_an_archive_is_a_conflict_that_names_nothing()
    {
        var (invoiceId, token, key) = await ArchivedInvoiceWithTokenAsync();
        await ForgetArchiveAsync(invoiceId);
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(PublicDocumentUrl(token));
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.DoesNotContain(await InvoiceNumberAsync(invoiceId), raw, StringComparison.Ordinal);
        Assert.DoesNotContain(key, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(token, raw, StringComparison.Ordinal);
        AssertNoInternalId(raw, invoiceId);
    }

    /// <summary>
    /// A recorded key whose file has gone is a storage defect: a 500 with no detail, naming no key
    /// and no path, and the document is not regenerated to cover the loss.
    /// </summary>
    [Fact]
    public async Task A_missing_archived_file_is_a_server_error_for_the_customer_and_is_never_regenerated()
    {
        var (invoiceId, token, key) = await ArchivedInvoiceWithTokenAsync();
        File.Delete(DiskPathOf(key));
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(PublicDocumentUrl(token));
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(JsonSerializer.Deserialize<JsonElement>(raw).TryGetProperty("detail", out _));
        Assert.DoesNotContain(key, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(token, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.StorageRoot, raw, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(InvoiceDirectory(invoiceId)));
    }

    /// <summary>
    /// The read's own 404 for a token whose Invoice no longer resolves (TokenLinks has no foreign key)
    /// used to name the internal Invoice id in its ProblemDetails detail. D114 fixed it, on both routes.
    /// </summary>
    [Theory]
    [InlineData("read")]
    [InlineData("document")]
    public async Task A_token_whose_invoice_does_not_resolve_names_no_internal_id(string route)
    {
        var (_, token, _) = await ArchivedInvoiceWithTokenAsync();
        const int missingInvoiceId = 987_654_321;
        await RepointAsync(token, missingInvoiceId);
        using var anonymous = factory.CreateClient();

        var url = route == "read" ? $"/api/v1/public/invoices/{token}" : PublicDocumentUrl(token);
        var response = await anonymous.GetAsync(url);
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(missingInvoiceId.ToString(), raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoice", Detail(raw), StringComparison.Ordinal);
        Assert.DoesNotContain(token, raw, StringComparison.Ordinal);
    }

    /// <summary>
    /// No successful public response names where the document is stored, or the Invoice's internal id.
    /// </summary>
    [Fact]
    public async Task No_public_response_carries_the_storage_key_or_the_internal_id()
    {
        var (invoiceId, token, key) = await ArchivedInvoiceWithTokenAsync();
        using var anonymous = factory.CreateClient();

        var read = await (await anonymous.GetAsync($"/api/v1/public/invoices/{token}")).Content.ReadAsStringAsync();
        var document = await anonymous.GetAsync(PublicDocumentUrl(token));
        var headers = string.Join('\n', document.Headers.Concat(document.Content.Headers)
            .Select(h => $"{h.Key}: {string.Join(',', h.Value)}"));

        Assert.DoesNotContain(key, read, StringComparison.Ordinal);
        Assert.DoesNotContain("invoices/", read, StringComparison.Ordinal);
        Assert.DoesNotContain(key, headers, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetFileName(key), headers, StringComparison.Ordinal);
        AssertNoInternalId(read, invoiceId);
    }

    /// <summary>
    /// Inherits D65's policy by placement on the rate-limited public controller, sharing the read's
    /// bucket. Pinned so a refactor cannot move it somewhere unthrottled.
    /// </summary>
    [Fact]
    public void The_public_document_route_lives_on_the_rate_limited_anonymous_controller()
    {
        var controller = typeof(RenoTrack.Api.Controllers.PublicController);
        var method = controller.GetMethods().Single(m => m.Name == "GetInvoiceDocument");

        Assert.Equal(controller, method.DeclaringType);
        Assert.NotNull(controller
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), inherit: false)
            .SingleOrDefault());
        Assert.Empty(method.GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), inherit: false));
        Assert.Empty(method.GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.DisableRateLimitingAttribute), inherit: false));
    }

    // ---- Helpers -------------------------------------------------------------------

    private static string? Detail(string problemJson) =>
        JsonSerializer.Deserialize<JsonElement>(problemJson).TryGetProperty("detail", out var detail)
            ? detail.GetString()
            : null;

    /// <summary>
    /// The id must not appear as a JSON number or as a token of text. A plain substring check would
    /// also match inside the invoice number or a trace id, so the body is split on non-digits.
    /// </summary>
    private static void AssertNoInternalId(string raw, int invoiceId)
    {
        var withoutTrace = System.Text.RegularExpressions.Regex.Replace(raw, "\"traceId\":\"[^\"]*\"", string.Empty);
        var numbers = System.Text.RegularExpressions.Regex.Split(withoutTrace, "[^0-9]+");
        Assert.DoesNotContain(invoiceId.ToString(), numbers);
    }

    /// <summary>What an Invoice sent before Slice 3a looks like: Sent, with a link, and no archive.</summary>
    private async Task ForgetArchiveAsync(int invoiceId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Invoices SET DocumentFileUrl = NULL WHERE Id = {invoiceId}");
    }

    private async Task ExpireAsync(string token)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE TokenLinks SET ExpiresAt = {DateTime.UtcNow.AddDays(-1)} WHERE Token = {token}");
    }

    /// <summary>A token row pointing at no Invoice — possible because TokenLinks has no foreign key.</summary>
    private async Task RepointAsync(string token, int entityId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE TokenLinks SET EntityId = {entityId} WHERE Token = {token}");
    }

    private async Task<int> CustomerIdOfAsync(int invoiceId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var projectId = await context.Invoices.Where(i => i.Id == invoiceId).Select(i => i.ProjectId).SingleAsync();
        return await context.Projects.Where(p => p.Id == projectId).Select(p => p.CustomerId).SingleAsync();
    }
}
