using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Application.Common;
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
    /// The response's exact surface, asserted against raw JSON rather than a typed read so a field
    /// added later cannot slip in unnoticed — the technique Phase 6 introduced for the public DTO.
    /// <b>Updated in Phase 8 Slice 6</b>, which is when FR-7.4's Invoice portion arrived; through
    /// Phase 7 this same test pinned its absence.
    /// </summary>
    [Fact]
    public async Task The_project_detail_exposes_exactly_the_documented_fields()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync($"/api/v1/projects/{projectId}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var propertyNames = body.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal(
            ["id", "status", "agreedTotal", "createdAt", "completedAt", "customerId", "customerName",
             "leadId", "inspectionId", "angebotId", "angebotNumber", "alreadyInvoiced", "remaining",
             "invoices"],
            propertyNames);
    }

    /// <summary>
    /// An invoice row carries E1's four columns plus the id its "Mark Paid" button needs — no net
    /// or VAT split, no issue date, no void reason, no payments. Pinned against raw JSON so a typed
    /// read cannot ignore an added field.
    /// </summary>
    [Fact]
    public async Task An_invoice_row_exposes_exactly_the_documented_fields()
    {
        var projectId = await ConvertedProjectAsync();
        await CreateInvoiceAsync(projectId, gross: 100.00m);
        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync($"/api/v1/projects/{projectId}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var row = body.GetProperty("invoices").EnumerateArray().Single();

        Assert.Equal(
            ["id", "invoiceNumber", "grossAmount", "status", "dueDate"],
            row.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("Draft", row.GetProperty("status").GetString());
    }

    /// <summary>
    /// FR-7.4 over HTTP, with the two figures agreeing with the standalone balance endpoint — the
    /// duplication FR-7.4's "in one place" requires, held together by an assertion rather than by
    /// hope.
    /// </summary>
    [Fact]
    public async Task The_project_detail_carries_the_invoice_list_and_figures()
    {
        var projectId = await ConvertedProjectAsync();
        await CreateInvoiceAsync(projectId, gross: 100.00m);
        using var admin = await AdminClientAsync();

        var detail = await (await admin.GetAsync($"/api/v1/projects/{projectId}")).Content
            .ReadFromJsonAsync<JsonElement>();
        var balance = await (await admin.GetAsync($"/api/v1/projects/{projectId}/invoice-balance")).Content
            .ReadFromJsonAsync<JsonElement>();

        Assert.Single(detail.GetProperty("invoices").EnumerateArray());
        Assert.Equal(100.00m, detail.GetProperty("alreadyInvoiced").GetDecimal());
        Assert.Equal(
            balance.GetProperty("alreadyInvoiced").GetDecimal(),
            detail.GetProperty("alreadyInvoiced").GetDecimal());
        Assert.Equal(
            balance.GetProperty("remaining").GetDecimal(),
            detail.GetProperty("remaining").GetDecimal());
    }

    /// <summary>
    /// K-3, end to end: a voided Invoice stays on the page (BR-9 — a numbered record, not a deleted
    /// one) and simultaneously leaves the arithmetic (StateMachine.md §3.3). The two halves fail
    /// independently, so neither can be broken quietly.
    /// </summary>
    [Fact]
    public async Task A_voided_invoice_stays_in_the_list_but_leaves_the_figures()
    {
        var projectId = await ConvertedProjectAsync();
        var invoiceId = await CreateInvoiceAsync(projectId, gross: 100.00m);
        using var admin = await AdminClientAsync();

        await admin.PostAsJsonAsync($"/api/v1/invoices/{invoiceId}/void", new { reason = "Wrong project." });

        var body = await (await admin.GetAsync($"/api/v1/projects/{projectId}")).Content
            .ReadFromJsonAsync<JsonElement>();

        var row = body.GetProperty("invoices").EnumerateArray().Single();
        Assert.Equal("Void", row.GetProperty("status").GetString());
        Assert.Equal(0m, body.GetProperty("alreadyInvoiced").GetDecimal());
    }

    /// <summary>
    /// The invoice list is Project-detail data (`PermissionMatrix.md` §5, decision K-3), so an
    /// Inspector sees it — and, in the same request, gains no Invoice-management permission. Both
    /// halves are asserted together, so widening or narrowing either one fails here.
    /// </summary>
    [Fact]
    public async Task An_inspector_sees_the_invoice_list_and_still_cannot_manage_invoices()
    {
        var projectId = await ConvertedProjectAsync();
        await CreateInvoiceAsync(projectId, gross: 100.00m);
        using var inspector = await InspectorClientAsync();

        var detail = await inspector.GetAsync($"/api/v1/projects/{projectId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var body = await detail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(body.GetProperty("invoices").EnumerateArray());

        var create = await inspector.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/invoices",
            new { grossAmount = 50.00m, dueDate = DateTime.UtcNow.AddDays(14) });

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Empty(await create.Content.ReadAsStringAsync());
    }

    // ---- Completion (FR-7.3, FR-8.6) ---------------------------------------

    [Fact]
    public async Task Admin_can_complete_a_project_whose_invoices_are_all_paid()
    {
        var projectId = await ConvertedProjectAsync();
        var invoiceId = await CreateInvoiceAsync(projectId, gross: 100.00m);
        using var admin = await AdminClientAsync();

        await admin.PostAsync($"/api/v1/invoices/{invoiceId}/send", content: null);
        await admin.PostAsJsonAsync(
            $"/api/v1/invoices/{invoiceId}/mark-paid",
            new { paidAt = DateTime.UtcNow, method = "BankTransfer" });

        var response = await admin.PostAsync($"/api/v1/projects/{projectId}/complete", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Completed", body.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("completedAt").ValueKind);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var project = await context.Projects.SingleAsync(p => p.Id == projectId);
        Assert.Equal(ProjectStatus.Completed, project.Status);
    }

    /// <summary>
    /// The body is optional, which is what makes the ordinary case a single click with nothing to
    /// say. The test above already omits it entirely; this one proves an explicit
    /// <c>forceOverride: false</c> is equivalent rather than a second, differently-behaving shape.
    /// </summary>
    [Fact]
    public async Task An_explicit_non_override_body_behaves_like_an_absent_one()
    {
        var projectId = await ConvertedProjectAsync();
        var invoiceId = await CreateInvoiceAsync(projectId, gross: 100.00m);
        using var admin = await AdminClientAsync();

        await admin.PostAsync($"/api/v1/invoices/{invoiceId}/send", content: null);
        await admin.PostAsJsonAsync(
            $"/api/v1/invoices/{invoiceId}/mark-paid",
            new { paidAt = DateTime.UtcNow, method = "Cash" });

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/complete", new { forceOverride = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Completing_a_project_with_an_unpaid_invoice_is_a_conflict()
    {
        var projectId = await ConvertedProjectAsync();
        await CreateInvoiceAsync(projectId, gross: 100.00m);
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync($"/api/v1/projects/{projectId}/complete", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        Assert.Equal(ProjectStatus.Active, (await context.Projects.SingleAsync(p => p.Id == projectId)).Status);
    }

    /// <summary>
    /// I-2 over HTTP: a Project that was never invoiced is blocked, even though "all Invoices are
    /// Paid or Void" is vacuously true of it. This is the clause no document states outright, so it
    /// is the one most likely to be "simplified" away.
    /// </summary>
    [Fact]
    public async Task Completing_a_project_with_no_invoices_at_all_is_a_conflict()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync($"/api/v1/projects/{projectId}/complete", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_override_with_a_reason_completes_a_project_with_unpaid_invoices()
    {
        var projectId = await ConvertedProjectAsync();
        await CreateInvoiceAsync(projectId, gross: 100.00m);
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/complete",
            new { forceOverride = true, reason = "Customer waived the final instalment in writing." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "Completed",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
    }

    /// <summary>
    /// FR-8.6 requires the reason to be recorded, and `ERD.md` defines no Project column for it —
    /// so the AuditLog row is its only home (decision K-7). If that row stopped carrying it, the
    /// justification for overriding a financial guard would exist nowhere at all.
    /// </summary>
    [Fact]
    public async Task An_override_records_its_reason_in_the_audit_log()
    {
        const string reason = "Final instalment written off — see ticket 4471.";
        var projectId = await ConvertedProjectAsync();
        await CreateInvoiceAsync(projectId, gross: 100.00m);
        using var admin = await AdminClientAsync();

        await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/complete", new { forceOverride = true, reason });

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var entry = await context.AuditLogs.SingleAsync(
            log => log.EntityType == "Project" && log.EntityId == projectId && log.Action == AuditAction.ProjectCompleted);

        Assert.Equal(reason, entry.Details);
    }

    /// <summary>I-3: an override that overrides nothing is refused, and nothing is recorded.</summary>
    [Fact]
    public async Task An_override_with_nothing_to_override_is_a_bad_request()
    {
        var projectId = await ConvertedProjectAsync();
        var invoiceId = await CreateInvoiceAsync(projectId, gross: 100.00m);
        using var admin = await AdminClientAsync();

        await admin.PostAsync($"/api/v1/invoices/{invoiceId}/send", content: null);
        await admin.PostAsJsonAsync(
            $"/api/v1/invoices/{invoiceId}/mark-paid",
            new { paidAt = DateTime.UtcNow, method = "Cash" });

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/complete",
            new { forceOverride = true, reason = "Nothing is actually outstanding." });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        Assert.Equal(ProjectStatus.Active, (await context.Projects.SingleAsync(p => p.Id == projectId)).Status);
        Assert.False(await context.AuditLogs.AnyAsync(
            log => log.EntityType == "Project" && log.EntityId == projectId && log.Action == AuditAction.ProjectCompleted));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_override_without_a_real_reason_is_a_bad_request(string? reason)
    {
        var projectId = await ConvertedProjectAsync();
        await CreateInvoiceAsync(projectId, gross: 100.00m);
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/complete", new { forceOverride = true, reason });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>K-4's mirror rule: a reason without an override is refused, never silently dropped.</summary>
    [Fact]
    public async Task A_reason_without_an_override_is_a_bad_request()
    {
        var projectId = await ConvertedProjectAsync();
        await CreateInvoiceAsync(projectId, gross: 100.00m);
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/complete",
            new { forceOverride = false, reason = "Just noting something." });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Completed is terminal (StateMachine.md §4.2 draws no outgoing edge), so a second click is a
    /// 409 rather than a silent success.
    /// </summary>
    [Fact]
    public async Task Completing_an_already_completed_project_is_a_conflict()
    {
        var projectId = await ConvertedProjectAsync();
        await CreateInvoiceAsync(projectId, gross: 100.00m);
        using var admin = await AdminClientAsync();

        var first = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/complete", new { forceOverride = true, reason = "Written off." });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/complete", new { forceOverride = true, reason = "Again." });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    /// <summary>
    /// <b>The accepted error-precedence consequence, pinned rather than left to be rediscovered.</b>
    /// Preconditions are evaluated before the aggregate is touched, so an already-`Completed`
    /// Project whose Invoices are all settled reports **400 "nothing to override"** — the invoice
    /// rule answers first — rather than a 409 for its own state. The outcome is still a refusal and
    /// the Project stays `Completed`; only the reason differs. Changing this would require either a
    /// handler-level `Status` check (CLAUDE.md §6) or a public precondition probe on `Project`
    /// (§2), both of which were rejected.
    /// </summary>
    [Fact]
    public async Task An_override_on_a_completed_project_with_settled_invoices_reports_the_invoice_rule_first()
    {
        var projectId = await ConvertedProjectAsync();
        var invoiceId = await CreateInvoiceAsync(projectId, gross: 100.00m);
        using var admin = await AdminClientAsync();

        await admin.PostAsync($"/api/v1/invoices/{invoiceId}/send", content: null);
        await admin.PostAsJsonAsync(
            $"/api/v1/invoices/{invoiceId}/mark-paid",
            new { paidAt = DateTime.UtcNow, method = "Cash" });
        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PostAsync($"/api/v1/projects/{projectId}/complete", content: null)).StatusCode);

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/complete",
            new { forceOverride = true, reason = "Trying again." });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Refused all the same, and the Project is untouched — the outcome, not the wording, is
        // what the guard exists for.
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        Assert.Equal(ProjectStatus.Completed, (await context.Projects.SingleAsync(p => p.Id == projectId)).Status);
    }

    /// <summary>
    /// `PermissionMatrix.md` §5 marks completion Admin <c>F</c> / Inspector <c>—</c>. The body is
    /// asserted empty because an authorization-middleware rejection carries none while a
    /// <c>ForbiddenException</c> yields ProblemDetails — without that, this test could not tell a
    /// role gate from an ownership check, and there is no ownership check here to find.
    /// </summary>
    [Fact]
    public async Task An_inspector_cannot_complete_a_project()
    {
        var projectId = await ConvertedProjectAsync();
        using var inspector = await InspectorClientAsync();

        var response = await inspector.PostAsync($"/api/v1/projects/{projectId}/complete", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Completion_requires_authentication()
    {
        var projectId = await ConvertedProjectAsync();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsync($"/api/v1/projects/{projectId}/complete", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Completing_an_unknown_project_is_a_not_found()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync("/api/v1/projects/999999/complete", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// A completed Project is no longer <c>Active</c>/<c>OnHold</c>, so StateMachine.md §5's
    /// "an Invoice cannot exist without an Active/OnHold Project" now bites — the two guards this
    /// slice added and Slice 3 added meet here, and neither was written with the other in mind.
    /// </summary>
    [Fact]
    public async Task No_further_invoice_can_be_created_once_the_project_is_completed()
    {
        var projectId = await ConvertedProjectAsync();
        await CreateInvoiceAsync(projectId, gross: 100.00m);
        using var admin = await AdminClientAsync();

        await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/complete", new { forceOverride = true, reason = "Written off." });

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/invoices",
            new { grossAmount = 25.00m, dueDate = DateTime.UtcNow.AddDays(14) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---- Hold / Resume (PermissionMatrix.md §5, StateMachine.md §4.3) -------

    [Fact]
    public async Task Admin_can_put_an_active_project_on_hold_and_resume_it()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        var held = await admin.PostAsync($"/api/v1/projects/{projectId}/hold", content: null);

        Assert.Equal(HttpStatusCode.OK, held.StatusCode);
        Assert.Equal("OnHold", (await held.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("status").GetString());

        var resumed = await admin.PostAsync($"/api/v1/projects/{projectId}/resume", content: null);

        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.Equal("Active", (await resumed.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("status").GetString());
    }

    [Fact]
    public async Task Holding_a_project_twice_is_409()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        await admin.PostAsync($"/api/v1/projects/{projectId}/hold", content: null);
        var response = await admin.PostAsync($"/api/v1/projects/{projectId}/hold", content: null);

        // Project.PutOnHold() guards Active-only; the handler calls it and lets it throw.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Resuming_an_active_project_is_409()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync($"/api/v1/projects/{projectId}/resume", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Holding_a_completed_project_is_409()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/complete",
            new { forceOverride = true, reason = "Closed early." });

        var response = await admin.PostAsync($"/api/v1/projects/{projectId}/hold", content: null);

        // Completed is terminal — StateMachine.md §4.2 draws no edge out of it.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_on_hold_project_still_accepts_invoices()
    {
        var projectId = await ConvertedProjectAsync();
        using var admin = await AdminClientAsync();

        await admin.PostAsync($"/api/v1/projects/{projectId}/hold", content: null);

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/invoices",
            new { grossAmount = 100.00m, dueDate = DateTime.UtcNow.AddDays(14) });

        // StateMachine.md §5 permits an Invoice against an Active *or* OnHold Project, so pausing
        // must not disturb billing that is already in flight.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Inspector_is_forbidden_from_holding_or_resuming()
    {
        var projectId = await ConvertedProjectAsync();
        using var inspector = await InspectorClientAsync();

        var held = await inspector.PostAsync($"/api/v1/projects/{projectId}/hold", content: null);
        var resumed = await inspector.PostAsync($"/api/v1/projects/{projectId}/resume", content: null);

        // §5 grants an Inspector Project *reads* only; every action row stays Admin-only.
        Assert.Equal(HttpStatusCode.Forbidden, held.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, resumed.StatusCode);
    }

    [Fact]
    public async Task Holding_an_unknown_project_is_404()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsync("/api/v1/projects/999999/hold", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Helpers -----------------------------------------------------------

    private async Task<int> CreateInvoiceAsync(int projectId, decimal gross)
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/invoices",
            new { grossAmount = gross, dueDate = DateTime.UtcNow.AddDays(14) });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
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
