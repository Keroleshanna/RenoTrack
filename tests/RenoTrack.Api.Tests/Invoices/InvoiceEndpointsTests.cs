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

    // ---- Send (Slice 4) ----------------------------------------------------

    [Fact]
    public async Task Admin_can_send_a_draft_invoice()
    {
        var invoiceId = await DraftInvoiceAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync($"/api/v1/invoices/{invoiceId}/send", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Sent", body.GetProperty("status").GetString());
    }

    /// <summary>
    /// The status change and the token row must land together — a token for an Invoice that never
    /// became Sent would be a live credential for a bill nobody issued.
    /// </summary>
    [Fact]
    public async Task Sending_issues_a_token_link_for_the_invoice()
    {
        var invoiceId = await DraftInvoiceAsync();
        using var admin = await AdminClientAsync();

        await admin.PostAsync($"/api/v1/invoices/{invoiceId}/send", content: null);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var link = await context.TokenLinks.SingleAsync(
            t => t.EntityType == TokenLinkEntityType.Invoice && t.EntityId == invoiceId);
        Assert.Null(link.UsedAt);

        var invoice = await context.Invoices.SingleAsync(i => i.Id == invoiceId);
        Assert.Equal(InvoiceStatus.Sent, invoice.Status);
    }

    /// <summary>StateMachine.md §3.3: only a Draft Invoice may be sent — a second send is a 409.</summary>
    [Fact]
    public async Task Sending_an_already_sent_invoice_is_a_conflict()
    {
        var invoiceId = await DraftInvoiceAsync();
        using var admin = await AdminClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/v1/invoices/{invoiceId}/send", content: null)).StatusCode);

        var second = await admin.PostAsync($"/api/v1/invoices/{invoiceId}/send", content: null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Sending_an_unknown_invoice_is_not_found()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync("/api/v1/invoices/999999999/send", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// PermissionMatrix §5: "Send Invoice — Admin F / Inspector —". The empty-body assertion is what
    /// distinguishes a role-gate 403 from an ownership 403 (Phase 4 Slice 9's lesson).
    /// </summary>
    [Fact]
    public async Task An_inspector_cannot_send_an_invoice()
    {
        var invoiceId = await DraftInvoiceAsync();
        using var inspector = await InspectorClientAsync();

        var response = await inspector.PostAsync($"/api/v1/invoices/{invoiceId}/send", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_send_an_invoice()
    {
        var invoiceId = await DraftInvoiceAsync();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsync($"/api/v1/invoices/{invoiceId}/send", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- Public read (Slice 4) ---------------------------------------------

    [Fact]
    public async Task The_customer_can_read_the_invoice_with_their_token()
    {
        var (invoiceId, token) = await SentInvoiceAsync();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync($"/api/v1/public/invoices/{token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("M. Klein", body.GetProperty("customerName").GetString());
        Assert.Equal(
            body.GetProperty("grossAmount").GetDecimal(),
            body.GetProperty("netAmount").GetDecimal() + body.GetProperty("vatAmount").GetDecimal());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var invoice = await context.Invoices.SingleAsync(i => i.Id == invoiceId);
        Assert.Equal(invoice.InvoiceNumber, body.GetProperty("invoiceNumber").GetString());
    }

    /// <summary>
    /// The public payload is a separate hierarchy from InvoiceDto, and what it withholds is the
    /// point — no internal ids, no issue date, no void reason, no payments. `status` is the one
    /// field beyond Wireframe A4, added by explicit decision in Slice 5. Asserted against raw JSON
    /// so a typed read cannot ignore an added field.
    /// </summary>
    [Fact]
    public async Task The_public_payload_exposes_only_the_customer_facing_fields()
    {
        var (_, token) = await SentInvoiceAsync();
        using var anonymous = factory.CreateClient();

        var body = await anonymous.GetFromJsonAsync<JsonElement>($"/api/v1/public/invoices/{token}");

        Assert.Equal(
            ["customerName", "dueDate", "grossAmount", "invoiceNumber", "netAmount", "status", "vatAmount"],
            body.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());

        // Serialized by name, and as the public vocabulary — never the internal InvoiceStatus.
        Assert.Equal("Open", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_unknown_public_token_is_not_found()
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync("/api/v1/public/invoices/not-a-real-token");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// An Angebot token on the invoice route must be indistinguishable from an unknown one — the
    /// same 404, so an anonymous caller learns nothing about which tokens exist.
    /// </summary>
    [Fact]
    public async Task An_angebot_token_on_the_invoice_route_is_not_found()
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
        var response = await anonymous.GetAsync($"/api/v1/public/invoices/{angebotToken}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// The token is the credential, so it must never reach a diagnostic surface — not the
    /// ProblemDetails body, not `instance`. Phase 6's rule, now covering the invoice route, which
    /// `RouteDiagnostics` picks up automatically because the route parameter is named `token`.
    /// </summary>
    [Fact]
    public async Task A_failing_public_read_never_echoes_the_token()
    {
        const string secret = "a-secret-invoice-token-value";
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync($"/api/v1/public/invoices/{secret}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The invoice route joins the existing public controller, so it inherits D65's rate-limit
    /// policy by placement rather than by a second declaration. Pinned so a future refactor cannot
    /// move it somewhere unthrottled.
    /// </summary>
    [Fact]
    public void The_public_invoice_route_lives_on_the_rate_limited_controller()
    {
        var method = typeof(RenoTrack.Api.Controllers.PublicController)
            .GetMethods()
            .Single(m => m.Name == "GetInvoice");

        Assert.Equal(typeof(RenoTrack.Api.Controllers.PublicController), method.DeclaringType);
        Assert.NotNull(typeof(RenoTrack.Api.Controllers.PublicController)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), inherit: false)
            .SingleOrDefault());
    }

    // ---- Mark paid (Slice 5) -----------------------------------------------

    [Fact]
    public async Task Admin_can_mark_a_sent_invoice_paid()
    {
        var (invoiceId, _) = await SentInvoiceAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/invoices/{invoiceId}/mark-paid",
            new { paidAt = DateTime.UtcNow, method = "BankTransfer" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Paid", body.GetProperty("status").GetString());
    }

    /// <summary>
    /// The Payment row must really exist, carry the Invoice's own gross, and name the Admin who
    /// recorded it — the whole point of FR-8.4's manual confirmation.
    /// </summary>
    [Fact]
    public async Task Marking_paid_records_a_payment_for_the_full_gross()
    {
        var (invoiceId, _) = await SentInvoiceAsync();
        var adminId = await factory.GetUserIdAsync(RenoTrackApiFactory.AdminEmail);
        using var admin = await AdminClientAsync();

        await admin.PostAsJsonAsync(
            $"/api/v1/invoices/{invoiceId}/mark-paid",
            new { paidAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), method = "Cash" });

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var invoice = await context.Invoices.Include(i => i.Payments).SingleAsync(i => i.Id == invoiceId);

        var payment = Assert.Single(invoice.Payments);
        Assert.Equal(invoice.GrossAmount, payment.Amount);
        Assert.Equal(PaymentMethod.Cash, payment.Method);
        Assert.Equal(adminId, payment.RecordedByAdminId);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    /// <summary>
    /// A duplicate confirmation is impossible rather than discouraged — `Paid` is terminal, so the
    /// second attempt is a 409 and no second Payment row can ever exist.
    /// </summary>
    [Fact]
    public async Task Marking_an_already_paid_invoice_is_a_conflict_and_adds_no_second_payment()
    {
        var (invoiceId, _) = await SentInvoiceAsync();
        using var admin = await AdminClientAsync();
        var body = new { paidAt = DateTime.UtcNow, method = "BankTransfer" };

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/mark-paid", body)).StatusCode);

        var second = await admin.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/mark-paid", body);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var invoice = await context.Invoices.Include(i => i.Payments).SingleAsync(i => i.Id == invoiceId);
        Assert.Single(invoice.Payments);
    }

    /// <summary>StateMachine §3.3 draws MarkPaid only from Sent and Overdue — a Draft cannot be paid.</summary>
    [Fact]
    public async Task Marking_a_draft_invoice_paid_is_a_conflict()
    {
        var invoiceId = await DraftInvoiceAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/invoices/{invoiceId}/mark-paid",
            new { paidAt = DateTime.UtcNow, method = "BankTransfer" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_inspector_cannot_mark_an_invoice_paid()
    {
        var (invoiceId, _) = await SentInvoiceAsync();
        using var inspector = await InspectorClientAsync();

        var response = await inspector.PostAsJsonAsync(
            $"/api/v1/invoices/{invoiceId}/mark-paid",
            new { paidAt = DateTime.UtcNow, method = "BankTransfer" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Marking paid does not move the balance: a Sent invoice already counted toward
    /// `alreadyInvoiced`, and only Void is ever excluded (StateMachine §3.3).
    /// </summary>
    [Fact]
    public async Task Marking_paid_does_not_change_the_project_balance()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        var created = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/invoices",
            new { grossAmount = 100.00m, dueDate = DateTime.UtcNow.AddDays(14) });
        var invoiceId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        await admin.PostAsync($"/api/v1/invoices/{invoiceId}/send", content: null);

        var before = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{projectId}/invoice-balance");

        await admin.PostAsJsonAsync(
            $"/api/v1/invoices/{invoiceId}/mark-paid",
            new { paidAt = DateTime.UtcNow, method = "BankTransfer" });

        var after = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{projectId}/invoice-balance");

        Assert.Equal(
            before.GetProperty("alreadyInvoiced").GetDecimal(),
            after.GetProperty("alreadyInvoiced").GetDecimal());
        Assert.Equal(
            before.GetProperty("remaining").GetDecimal(),
            after.GetProperty("remaining").GetDecimal());
    }

    // ---- Void (Slice 5) ----------------------------------------------------

    [Fact]
    public async Task Admin_can_void_an_invoice_with_a_reason()
    {
        var invoiceId = await DraftInvoiceAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/invoices/{invoiceId}/void",
            new { reason = "Issued against the wrong Project." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Void", body.GetProperty("status").GetString());
        Assert.Equal("Issued against the wrong Project.", body.GetProperty("voidReason").GetString());
    }

    /// <summary>
    /// BR-9: the row and its number survive a void. Nothing anywhere deletes an Invoice, and the
    /// number can never be reused.
    /// </summary>
    [Fact]
    public async Task A_voided_invoice_keeps_its_row_and_its_number()
    {
        var invoiceId = await DraftInvoiceAsync();
        using var admin = await AdminClientAsync();

        string number;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
            number = (await context.Invoices.SingleAsync(i => i.Id == invoiceId)).InvoiceNumber;
        }

        await admin.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/void", new { reason = "Duplicate." });

        using var readScope = factory.Services.CreateScope();
        var readContext = readScope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var invoice = await readContext.Invoices.SingleAsync(i => i.Id == invoiceId);

        Assert.Equal(InvoiceStatus.Void, invoice.Status);
        Assert.Equal(number, invoice.InvoiceNumber);
    }

    /// <summary>
    /// StateMachine §3.3: a voided invoice is "excluded from 'remaining balance' math going
    /// forward" — the one balance-affecting transition in this slice.
    /// </summary>
    [Fact]
    public async Task Voiding_removes_the_invoice_from_the_remaining_balance()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        var created = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/invoices",
            new { grossAmount = 100.00m, dueDate = DateTime.UtcNow.AddDays(14) });
        var invoiceId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var before = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{projectId}/invoice-balance");
        Assert.Equal(100.00m, before.GetProperty("alreadyInvoiced").GetDecimal());

        await admin.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/void", new { reason = "Wrong amount." });

        var after = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{projectId}/invoice-balance");
        Assert.Equal(0m, after.GetProperty("alreadyInvoiced").GetDecimal());
    }

    [Fact]
    public async Task Voiding_without_a_reason_is_a_bad_request()
    {
        var invoiceId = await DraftInvoiceAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/void", new { reason = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Voiding_a_paid_invoice_is_a_conflict()
    {
        var (invoiceId, _) = await SentInvoiceAsync();
        using var admin = await AdminClientAsync();

        await admin.PostAsJsonAsync(
            $"/api/v1/invoices/{invoiceId}/mark-paid",
            new { paidAt = DateTime.UtcNow, method = "BankTransfer" });

        var response = await admin.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/void", new { reason = "Too late." });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_inspector_cannot_void_an_invoice()
    {
        var invoiceId = await DraftInvoiceAsync();
        using var inspector = await InspectorClientAsync();

        var response = await inspector.PostAsJsonAsync(
            $"/api/v1/invoices/{invoiceId}/void", new { reason = "Nope." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    // ---- Public status after Paid / Void (Slice 5 decision) -----------------

    /// <summary>
    /// The token link stays readable after payment and now says "Paid" instead of continuing to
    /// present a settled bill as outstanding.
    /// </summary>
    [Fact]
    public async Task A_paid_invoice_still_resolves_publicly_and_reports_paid()
    {
        var (invoiceId, token) = await SentInvoiceAsync();
        using var admin = await AdminClientAsync();

        await admin.PostAsJsonAsync(
            $"/api/v1/invoices/{invoiceId}/mark-paid",
            new { paidAt = DateTime.UtcNow, method = "BankTransfer" });

        using var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync($"/api/v1/public/invoices/{token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Paid", body.GetProperty("status").GetString());
    }

    /// <summary>
    /// The reason this field exists: without it a voided invoice would go on rendering as an
    /// ordinary payable bill. The link is **not** invalidated — 200, not 404 or 410.
    /// </summary>
    [Fact]
    public async Task A_voided_invoice_still_resolves_publicly_and_reports_void()
    {
        var (invoiceId, token) = await SentInvoiceAsync();
        using var admin = await AdminClientAsync();

        await admin.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/void", new { reason = "Cancelled by agreement." });

        using var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync($"/api/v1/public/invoices/{token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Void", body.GetProperty("status").GetString());
    }

    /// <summary>
    /// The void reason is staff-authored text about why the company cancelled a bill — the customer
    /// learns that it was cancelled, never the internal wording.
    /// </summary>
    [Fact]
    public async Task The_public_view_never_exposes_the_void_reason()
    {
        var (invoiceId, token) = await SentInvoiceAsync();
        using var admin = await AdminClientAsync();

        await admin.PostAsJsonAsync(
            $"/api/v1/invoices/{invoiceId}/void",
            new { reason = "Customer disputed the scope; renegotiating." });

        using var anonymous = factory.CreateClient();
        var body = await (await anonymous.GetAsync($"/api/v1/public/invoices/{token}")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("renegotiating", body, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Seeding -----------------------------------------------------------

    /// <summary>A Draft Invoice on a real converted Project, created through the real endpoint.</summary>
    private async Task<int> DraftInvoiceAsync()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        var created = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/invoices",
            new { grossAmount = 297.50m, dueDate = DateTime.UtcNow.AddDays(14) });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    /// <summary>
    /// A Sent Invoice and the token the customer actually received — read back from the database
    /// because the token is never returned in any response body, deliberately.
    /// </summary>
    private async Task<(int InvoiceId, string Token)> SentInvoiceAsync()
    {
        var invoiceId = await DraftInvoiceAsync();
        using var admin = await AdminClientAsync();

        var sent = await admin.PostAsync($"/api/v1/invoices/{invoiceId}/send", content: null);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var token = (await context.TokenLinks.SingleAsync(
            t => t.EntityType == TokenLinkEntityType.Invoice && t.EntityId == invoiceId)).Token;

        return (invoiceId, token);
    }

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
