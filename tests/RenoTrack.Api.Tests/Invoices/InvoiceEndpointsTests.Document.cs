using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Documents;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Api.Tests.Invoices;

/// <summary>
/// Phase 14 Slice 3a (D113, BR-15) through the real HTTP pipeline: sending archives the invoice's
/// PDF, <c>GET /api/v1/invoices/{id}/document</c> serves those exact bytes to an Admin and to no one
/// else, missing business data is a 409 while a missing deployment configuration is a 500, and no
/// response ever carries the storage key.
/// </summary>
/// <remarks>
/// The real renderer, real LocalDB and real disk under the factory's per-run storage root. What these
/// add over the Application tests is the wire: status codes, headers, the empty-body role-gate 403,
/// and that the bytes served are the bytes on disk.
/// </remarks>
public sealed partial class InvoiceEndpointsTests
{
    private async Task<(int InvoiceId, string DocumentFileUrl)> ArchivedInvoiceAsync()
    {
        var (invoiceId, _) = await SentInvoiceAsync();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var key = (await context.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId)).DocumentFileUrl;

        Assert.NotNull(key);
        return (invoiceId, key);
    }

    private string DiskPathOf(string documentFileUrl) => Path.Combine(factory.StorageRoot, documentFileUrl);

    private string InvoiceDirectory(int invoiceId) => Path.Combine(factory.StorageRoot, "invoices", invoiceId.ToString());

    // ---- Sending archives the document --------------------------------------

    [Fact]
    public async Task Sending_archives_exactly_one_pdf_under_the_invoices_own_directory()
    {
        var (invoiceId, key) = await ArchivedInvoiceAsync();

        var archived = Assert.Single(Directory.GetFiles(InvoiceDirectory(invoiceId)));
        Assert.Equal(Path.GetFullPath(DiskPathOf(key)), Path.GetFullPath(archived));
        Assert.Equal("%PDF-"u8.ToArray(), (await File.ReadAllBytesAsync(archived))[..5]);
    }

    /// <summary>The response says a document exists, and nothing about where it is stored.</summary>
    [Fact]
    public async Task The_send_response_reports_a_document_and_never_its_key()
    {
        var invoiceId = await DraftInvoiceAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync($"/api/v1/invoices/{invoiceId}/send", content: null);
        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonSerializer.Deserialize<JsonElement>(raw);

        Assert.True(body.GetProperty("hasDocument").GetBoolean());
        Assert.DoesNotContain("invoices/", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("documentFileUrl", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".pdf", raw, StringComparison.OrdinalIgnoreCase);
    }

    // ---- The Admin download --------------------------------------------------

    [Fact]
    public async Task Admin_downloads_exactly_the_archived_bytes_as_a_pdf_attachment()
    {
        var (invoiceId, key) = await ArchivedInvoiceAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync($"/api/v1/invoices/{invoiceId}/document");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);

        var number = await InvoiceNumberAsync(invoiceId);
        Assert.Equal($"Rechnung-{number}.pdf", response.Content.Headers.ContentDisposition.FileName);
        Assert.Equal(await File.ReadAllBytesAsync(DiskPathOf(key)), await response.Content.ReadAsByteArrayAsync());
    }

    /// <summary>
    /// A legal document carrying personal data must not linger in a shared cache, and must only ever
    /// be treated as the PDF it declares itself to be.
    /// </summary>
    [Fact]
    public async Task The_download_is_never_cached_and_never_sniffed()
    {
        var (invoiceId, _) = await ArchivedInvoiceAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync($"/api/v1/invoices/{invoiceId}/document");

        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    /// <summary>BR-15: the same bytes every time — the archive is served, never re-rendered.</summary>
    [Fact]
    public async Task Repeated_downloads_return_identical_bytes()
    {
        var (invoiceId, _) = await ArchivedInvoiceAsync();
        using var admin = await AdminClientAsync();

        var first = await (await admin.GetAsync($"/api/v1/invoices/{invoiceId}/document")).Content.ReadAsByteArrayAsync();
        var second = await (await admin.GetAsync($"/api/v1/invoices/{invoiceId}/document")).Content.ReadAsByteArrayAsync();

        Assert.Equal(first, second);
    }

    /// <summary>
    /// The archive is the issued invoice whatever happens afterwards: a payment or a void changes the
    /// status and leaves the document — and the file on disk — exactly as issued (BR-9, BR-15).
    /// </summary>
    [Theory]
    [InlineData("Paid")]
    [InlineData("Void")]
    public async Task The_archived_document_stays_downloadable_after_payment_or_void(string outcome)
    {
        var (invoiceId, key) = await ArchivedInvoiceAsync();
        var archivedBefore = await File.ReadAllBytesAsync(DiskPathOf(key));
        using var admin = await AdminClientAsync();

        var transition = outcome == "Paid"
            ? await admin.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/mark-paid", new { paidAt = DateTime.UtcNow, method = "BankTransfer" })
            : await admin.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/void", new { reason = "Issued against the wrong Project." });
        Assert.Equal(HttpStatusCode.OK, transition.StatusCode);

        var response = await admin.GetAsync($"/api/v1/invoices/{invoiceId}/document");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(archivedBefore, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(archivedBefore, await File.ReadAllBytesAsync(DiskPathOf(key)));
    }

    /// <summary>A Draft was never issued: nothing to serve, and nothing is made up for it.</summary>
    [Fact]
    public async Task A_draft_has_no_document_and_is_a_conflict()
    {
        var invoiceId = await DraftInvoiceAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync($"/api/v1/invoices/{invoiceId}/document");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.False(Directory.Exists(InvoiceDirectory(invoiceId)));
    }

    [Fact]
    public async Task An_unknown_invoice_document_is_not_found()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync("/api/v1/invoices/999999999/document");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// PermissionMatrix §5: Admin F / Inspector —, because the document prints the customer's address
    /// (D112). The empty body is what proves this is the role gate, not an ownership 403.
    /// </summary>
    [Fact]
    public async Task An_inspector_cannot_download_an_invoice_document()
    {
        var (invoiceId, _) = await ArchivedInvoiceAsync();
        using var inspector = await InspectorClientAsync();

        var response = await inspector.GetAsync($"/api/v1/invoices/{invoiceId}/document");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_download_an_invoice_document()
    {
        var (invoiceId, _) = await ArchivedInvoiceAsync();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync($"/api/v1/invoices/{invoiceId}/document");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A recorded key whose file has gone is a storage defect: a 500 with no detail — the body names
    /// no key and no path — and the document is not regenerated to cover the loss.
    /// </summary>
    [Fact]
    public async Task A_missing_archived_file_is_a_server_error_and_is_never_regenerated()
    {
        var (invoiceId, key) = await ArchivedInvoiceAsync();
        File.Delete(DiskPathOf(key));
        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync($"/api/v1/invoices/{invoiceId}/document");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(JsonSerializer.Deserialize<JsonElement>(raw).TryGetProperty("detail", out _));

        // The key, not the substring "invoices/": the ProblemDetails `instance` is the request's own
        // route, /api/v1/invoices/{id}/document, which legitimately contains that word.
        Assert.DoesNotContain(key, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetFileName(key), raw, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.StorageRoot, raw, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(InvoiceDirectory(invoiceId)));
    }

    // ---- 409 for business data, 500 for deployment configuration -------------

    /// <summary>
    /// The customer's address is business data an Admin can supply (D112), so its absence is a 409 —
    /// and the refusal comes before anything is written: the invoice stays a Draft, no file, no link.
    /// </summary>
    [Fact]
    public async Task Sending_for_a_customer_without_an_address_is_a_conflict_and_leaves_nothing()
    {
        var invoiceId = await DraftInvoiceAsync();
        await ClearCustomerAddressAsync(invoiceId);
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync($"/api/v1/invoices/{invoiceId}/send", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertNothingWasIssuedAsync(invoiceId);
    }

    /// <summary>
    /// A missing company identity is the deployment's defect, which no Admin can fix from the screen:
    /// a 500 whose body carries no detail, never a 409 (D113) — and still nothing is written.
    /// </summary>
    [Fact]
    public async Task Sending_without_a_configured_company_identity_is_a_server_error_and_leaves_nothing()
    {
        var invoiceId = await DraftInvoiceAsync();
        await using var withoutIdentity = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting($"{CompanyLegalIdentityOptions.SectionName}:{nameof(CompanyLegalIdentityOptions.LegalName)}", "");
            builder.UseSetting($"{CompanyLegalIdentityOptions.SectionName}:{nameof(CompanyLegalIdentityOptions.TaxNumber)}", "");
        });
        using var admin = withoutIdentity.CreateClient();
        await LogInAsync(admin, RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

        var response = await admin.PostAsync($"/api/v1/invoices/{invoiceId}/send", content: null);
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(JsonSerializer.Deserialize<JsonElement>(raw).TryGetProperty("detail", out _));
        Assert.DoesNotContain("CompanyLegalIdentity", raw, StringComparison.Ordinal);
        await AssertNothingWasIssuedAsync(invoiceId);
    }

    // ---- Helpers ---------------------------------------------------------------

    private async Task AssertNothingWasIssuedAsync(int invoiceId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var invoice = await context.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Null(invoice.DocumentFileUrl);
        Assert.False(await context.TokenLinks.AnyAsync(
            t => t.EntityType == TokenLinkEntityType.Invoice && t.EntityId == invoiceId));
        Assert.False(Directory.Exists(InvoiceDirectory(invoiceId)) && Directory.GetFiles(InvoiceDirectory(invoiceId)).Length > 0);
    }

    /// <summary>
    /// What a Customer converted from a website Lead looks like: no address (D112). Produced by data,
    /// because the Customer aggregate deliberately cannot clear its address.
    /// </summary>
    private async Task ClearCustomerAddressAsync(int invoiceId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var projectId = await context.Invoices.Where(i => i.Id == invoiceId).Select(i => i.ProjectId).SingleAsync();
        var customerId = await context.Projects.Where(p => p.Id == projectId).Select(p => p.CustomerId).SingleAsync();

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Customers SET Address = NULL WHERE Id = {customerId}");
    }

    private async Task<string> InvoiceNumberAsync(int invoiceId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        return await context.Invoices.Where(i => i.Id == invoiceId).Select(i => i.InvoiceNumber).SingleAsync();
    }

    private static async Task LogInAsync(HttpClient client, string email, string password)
    {
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
    }
}
