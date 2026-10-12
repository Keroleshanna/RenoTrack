using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Api.Tests.Email;

/// <summary>
/// The issued invoice, end to end over the real transport (Phase 14 Slice 3a, D113, BR-15): one
/// archived PDF, and the customer's attachment, the file on disk and the Admin's download are the
/// same bytes. Driven the way production gets there — the customer approves from the emailed link,
/// the Admin converts, supplies the address a phone Lead never gave (D112), invoices and sends.
/// </summary>
public sealed partial class CustomerWorkflowE2ETests
{
    [Fact]
    public async Task The_emailed_invoice_attachment_is_byte_for_byte_the_archived_document()
    {
        var invoiceId = await SendInvoiceToCustomerAsync(address: "Teststraße 2\n00000 Testort");

        // ---- The email ---------------------------------------------------------------------
        var message = MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(Assert.Single(_smtp.Messages))));
        var attachment = Assert.IsType<MimePart>(Assert.Single(message.Attachments));
        Assert.Equal("application/pdf", attachment.ContentType.MimeType);

        using var decoded = new MemoryStream();
        attachment.Content!.DecodeTo(decoded);
        var attached = decoded.ToArray();

        // ---- The archive ---------------------------------------------------------------------
        string key;
        string token;
        string invoiceNumber;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
            var invoice = await context.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
            key = invoice.DocumentFileUrl!;
            invoiceNumber = invoice.InvoiceNumber;
            token = (await context.TokenLinks.SingleAsync(
                t => t.EntityType == TokenLinkEntityType.Invoice && t.EntityId == invoiceId)).Token;
        }

        Assert.Equal($"Rechnung-{invoiceNumber}.pdf", attachment.ContentDisposition!.FileName);
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(_factory.StorageRoot, key)), attached);

        // ---- The Admin's download ------------------------------------------------------------
        using var admin = await AdminClientAsync();
        var download = await admin.GetAsync($"/api/v1/invoices/{invoiceId}/document");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(attached, await download.Content.ReadAsByteArrayAsync());

        // ---- Nothing reached a log it should not -------------------------------------------
        AssertNoTokenInLogs(token);
        AssertNotInLogs(key);
        AssertNotInLogs(Path.GetFileNameWithoutExtension(key));
    }

    /// <summary>
    /// D112's consequence, now real: the archive freezes the customer's address as sent. A later
    /// correction reaches the next invoice, never the one already issued.
    /// </summary>
    [Fact]
    public async Task A_later_address_correction_does_not_change_the_issued_document()
    {
        var invoiceId = await SendInvoiceToCustomerAsync(address: "Alte Straße 1\n00000 Testort");
        using var admin = await AdminClientAsync();
        var issued = await (await admin.GetAsync($"/api/v1/invoices/{invoiceId}/document")).Content.ReadAsByteArrayAsync();

        var customerId = await CustomerIdOfInvoiceAsync(invoiceId);
        var corrected = await admin.PutAsJsonAsync($"/api/v1/customers/{customerId}/address", new { address = "Neue Straße 9\n00000 Testort" });
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);

        var afterCorrection = await (await admin.GetAsync($"/api/v1/invoices/{invoiceId}/document")).Content.ReadAsByteArrayAsync();

        Assert.Equal(issued, afterCorrection);
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    /// <summary>
    /// Customer approval through the emailed link, conversion, the Admin's address correction, an
    /// invoice, and its send — every step through the real endpoints. Leaves exactly the invoice
    /// email in the SMTP capture.
    /// </summary>
    private async Task<int> SendInvoiceToCustomerAsync(string address)
    {
        var (angebotId, _) = await ApprovedAngebotAsync();
        using var admin = await AdminClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/v1/angebote/{angebotId}/send", content: null)).StatusCode);
        var angebotToken = TokenFrom(Assert.Single(_smtp.Messages));

        using var anonymous = _factory.CreateClient();
        var decision = await anonymous.PostAsJsonAsync($"/api/v1/public/angebote/{angebotToken}/decision", new { decision = "Approve" });
        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);

        var converted = await admin.PostAsync($"/api/v1/angebote/{angebotId}/convert-to-project", content: null);
        Assert.Equal(HttpStatusCode.Created, converted.StatusCode);
        var projectId = (await converted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var project = await (await admin.GetAsync($"/api/v1/projects/{projectId}")).Content.ReadFromJsonAsync<JsonElement>();
        var customerId = project.GetProperty("customerId").GetInt32();
        var addressed = await admin.PutAsJsonAsync($"/api/v1/customers/{customerId}/address", new { address });
        Assert.Equal(HttpStatusCode.OK, addressed.StatusCode);

        var created = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/invoices",
            new { grossAmount = 500.00m, dueDate = DateTime.UtcNow.AddDays(14), description = "Abschlag 1: Spachtelarbeiten" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var invoiceId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        _smtp.Reset();

        var sent = await admin.PostAsync($"/api/v1/invoices/{invoiceId}/send", content: null);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);

        return invoiceId;
    }

    private async Task<int> CustomerIdOfInvoiceAsync(int invoiceId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var projectId = await context.Invoices.Where(i => i.Id == invoiceId).Select(i => i.ProjectId).SingleAsync();
        return await context.Projects.Where(p => p.Id == projectId).Select(p => p.CustomerId).SingleAsync();
    }

    /// <summary>The storage key is internal: no log line, scope or exception may carry it (D113).</summary>
    private void AssertNotInLogs(string value)
    {
        var offenders = _factory.Logs.Messages
            .Where(m => m.Contains(value, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"'{value}' appeared in {offenders.Count} log message(s):{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders));
    }
}
