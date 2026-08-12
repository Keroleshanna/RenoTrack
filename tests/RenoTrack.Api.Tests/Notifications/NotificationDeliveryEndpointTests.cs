using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RenoTrack.Infrastructure.Persistence;
using RenoTrack.Infrastructure.Persistence.Entities;

namespace RenoTrack.Api.Tests.Notifications;

/// <summary>
/// <c>GET /api/v1/notification-deliveries</c> — the Admin's operational view of email delivery
/// (`PermissionMatrix.md` §9, Phase 9 Slice 4).
/// </summary>
/// <remarks>
/// Per D58 this asserts what the API layer adds over the query beneath it: routing, the role gate,
/// query-string binding and its 400s, and the JSON contract. The projection's own behaviour is
/// proven against real SQL in <c>NotificationDeliveryQueriesTests</c> and is not re-verified here.
/// </remarks>
[Collection("Api")]
public sealed class NotificationDeliveryEndpointTests(RenoTrackApiFactory factory)
{
    private const string Endpoint = "/api/v1/notification-deliveries";

    // ---------- authorization ----------

    [Fact]
    public async Task Admin_can_list_notification_deliveries()
    {
        var id = await SeedAsync(new NotificationDelivery(NotificationType.NewWebsiteLead, "Lead", NextEntityId()));
        using var client = await AdminClientAsync();

        var response = await client.GetAsync($"{Endpoint}?pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(id, Ids(body));
    }

    /// <summary>
    /// `PermissionMatrix.md` §9 gives Inspector "—" on both notification actions. Receiving a
    /// notification and administering the delivery system are different concerns — an Inspector is
    /// the recipient of one of the six, and still has no access here.
    /// </summary>
    [Fact]
    public async Task Inspector_is_forbidden()
    {
        using var client = await ClientAsync(RenoTrackApiFactory.InspectorEmail, RenoTrackApiFactory.InspectorPassword);

        var response = await client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Empty body: the authorization middleware refused this, so the action never ran. Asserted
        // so that weakening the role attribute would fail here rather than silently change what this
        // test proves.
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// An authenticated account holding neither role must be refused too — the endpoint names Admin
    /// positively rather than admitting anyone who merely is not an Inspector.
    /// </summary>
    [Fact]
    public async Task An_account_with_no_role_is_forbidden()
    {
        using var client = await ClientAsync(RenoTrackApiFactory.NoRoleEmail, RenoTrackApiFactory.NoRolePassword);

        var response = await client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_is_unauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- filtering, paging, ordering ----------

    [Fact]
    public async Task Omitting_the_status_filter_returns_sent_rows_too()
    {
        var sent = new NotificationDelivery(NotificationType.AngebotReady, "Angebot", NextEntityId());
        sent.MarkSent(DateTime.UtcNow);
        var sentId = await SeedAsync(sent);

        using var client = await AdminClientAsync();
        var body = await ReadAsync(client, $"{Endpoint}?pageSize=100");

        Assert.Contains(sentId, Ids(body));
    }

    [Fact]
    public async Task Filtering_by_status_returns_only_that_status()
    {
        var pendingId = await SeedAsync(new NotificationDelivery(NotificationType.NewWebsiteLead, "Lead", NextEntityId()));

        var failed = new NotificationDelivery(NotificationType.AngebotDecision, "Angebot", NextEntityId());
        failed.MarkFailed(DateTime.UtcNow, nameof(TimeoutException), "The mail server could not be reached or rejected the message.");
        var failedId = await SeedAsync(failed);

        using var client = await AdminClientAsync();
        var body = await ReadAsync(client, $"{Endpoint}?status={nameof(NotificationDeliveryStatus.Failed)}&pageSize=100");

        Assert.Contains(failedId, Ids(body));
        Assert.DoesNotContain(pendingId, Ids(body));
        Assert.All(
            body.GetProperty("items").EnumerateArray(),
            item => Assert.Equal(nameof(NotificationDeliveryStatus.Failed), item.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Returns_paging_metadata_and_respects_page_size()
    {
        await SeedAsync(new NotificationDelivery(NotificationType.NewWebsiteLead, "Lead", NextEntityId()));
        await SeedAsync(new NotificationDelivery(NotificationType.NewWebsiteLead, "Lead", NextEntityId()));

        using var client = await AdminClientAsync();
        var body = await ReadAsync(client, $"{Endpoint}?page=1&pageSize=1");

        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(1, body.GetProperty("pageSize").GetInt32());
        Assert.Single(body.GetProperty("items").EnumerateArray());

        // TotalCount describes the whole set, not the page.
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 2);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    public async Task Rejects_out_of_bounds_paging(string queryString)
    {
        using var client = await AdminClientAsync();

        var response = await client.GetAsync($"{Endpoint}{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetailsAsync(response);
    }

    /// <summary>
    /// Both shapes of a bad status must be refused: a non-member name and an undefined ordinal.
    /// <para>
    /// The second case is the interesting one. The design review expected an <c>IsInEnum()</c>
    /// equivalent to be necessary, because MVC has historically bound <c>?status=99</c> to an
    /// undefined enum value without complaint — answering a nonsense filter with an empty page rather
    /// than a 400. On this runtime the binder refuses it unaided, which was established by removing a
    /// speculative <c>[EnumDataType]</c> attribute and watching both cases still fail closed; the
    /// attribute was then deleted rather than kept as decoration. <b>This test is what makes that
    /// safe</b> — it pins the behaviour, not the mechanism, so a future runtime that loosened the
    /// binder would fail here rather than silently start accepting garbage filters.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("?status=NotAStatus")]
    [InlineData("?status=99")]
    public async Task Rejects_an_invalid_status(string queryString)
    {
        using var client = await AdminClientAsync();

        var response = await client.GetAsync($"{Endpoint}{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetailsAsync(response);
    }

    // ---------- JSON contract ----------

    /// <summary>
    /// D61's rule applied to this response: enums serialize as names, never ordinals. An ordinal
    /// contract silently changes meaning if anyone reorders an enum, and the database already stores
    /// both of these as strings.
    /// </summary>
    [Fact]
    public async Task Enums_serialize_as_names_not_ordinals()
    {
        var delivery = new NotificationDelivery(NotificationType.InvoiceReady, "Invoice", NextEntityId());
        delivery.RecordRecipient("kundin@example.invalid");
        delivery.MarkSent(DateTime.UtcNow);
        var id = await SeedAsync(delivery);

        using var client = await AdminClientAsync();
        var item = await FindItemAsync(client, id);

        Assert.Equal(nameof(NotificationType.InvoiceReady), item.GetProperty("notificationType").GetString());
        Assert.Equal(nameof(NotificationDeliveryStatus.Sent), item.GetProperty("status").GetString());
    }

    /// <summary>
    /// A null recipient is a real answer — "delivery failed before a recipient could be resolved" —
    /// so it must reach the client as JSON <c>null</c>, never as <c>""</c> and never as a sentinel
    /// string that would be indistinguishable from a genuine address.
    /// </summary>
    [Fact]
    public async Task An_unresolved_recipient_serializes_as_json_null()
    {
        var delivery = new NotificationDelivery(NotificationType.AngebotChangesRequested, "Angebot", NextEntityId());
        delivery.MarkFailed(DateTime.UtcNow, nameof(InvalidOperationException), "The notification could not be prepared.");
        var id = await SeedAsync(delivery);

        using var client = await AdminClientAsync();
        var item = await FindItemAsync(client, id);

        Assert.Equal(JsonValueKind.Null, item.GetProperty("recipient").ValueKind);
    }

    [Fact]
    public async Task A_failed_delivery_exposes_its_failure_type_and_sanitized_message()
    {
        const string sanitized = "The mail server could not be reached or rejected the message.";

        var delivery = new NotificationDelivery(NotificationType.AngebotSubmittedForReview, "Angebot", NextEntityId());
        delivery.RecordRecipient($"buero@example.invalid{NotificationDelivery.RecipientSeparator}inhaber@example.invalid");
        delivery.MarkFailed(DateTime.UtcNow, nameof(TimeoutException), sanitized);
        var id = await SeedAsync(delivery);

        using var client = await AdminClientAsync();
        var item = await FindItemAsync(client, id);

        Assert.Equal(nameof(TimeoutException), item.GetProperty("failureType").GetString());
        Assert.Equal(sanitized, item.GetProperty("failureMessage").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("sentAt").ValueKind);
        Assert.Equal("buero@example.invalid, inhaber@example.invalid", item.GetProperty("recipient").GetString());
    }

    /// <summary>
    /// The polymorphic business reference is reported exactly as stored — not resolved to a title,
    /// a number or a link. There is no foreign key to join through, and this endpoint is an
    /// operational view of the delivery record rather than a report about the business object.
    /// </summary>
    [Fact]
    public async Task Entity_type_and_id_are_flat_fields_with_no_resolved_business_data()
    {
        var entityId = NextEntityId();
        var id = await SeedAsync(new NotificationDelivery(NotificationType.NewWebsiteLead, "Lead", entityId));

        using var client = await AdminClientAsync();
        var item = await FindItemAsync(client, id);

        Assert.Equal("Lead", item.GetProperty("entityType").GetString());
        Assert.Equal(entityId, item.GetProperty("entityId").GetInt32());

        // Exactly the twelve persisted columns — nothing reloaded, nothing synthesized.
        Assert.Equal(12, item.EnumerateObject().Count());
        Assert.Equal(1, item.GetProperty("attemptCount").GetInt32());
    }

    // ---------- POST {id}/retry ----------

    /// <summary>
    /// <b>Every</b> refusal on this endpoint is 409 (S5-9), and this host reaches the refusal by the
    /// disabled-email route: <c>Email:Enabled</c> is false here, exactly as it is in
    /// <c>appsettings.json</c> and on every non-production host.
    /// </summary>
    /// <remarks>
    /// A successful retry is therefore unreachable from <c>Api.Tests</c> and is not faked to make it
    /// reachable — the delivery outcomes are proven for real, over a real socket, in
    /// <c>NotificationRetryServiceTests</c>. What this class owns is what the API layer adds: routing,
    /// the role gate, the 404, and the ProblemDetails shape (D58).
    /// </remarks>
    [Fact]
    public async Task Retry_is_refused_with_409_while_email_delivery_is_disabled()
    {
        var id = await SeedAsync(new NotificationDelivery(NotificationType.NewWebsiteLead, "Lead", NextEntityId()));
        using var client = await AdminClientAsync();

        var response = await client.PostAsync($"{Endpoint}/{id}/retry", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Email delivery is disabled", problem.GetProperty("detail").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Retrying_an_unknown_delivery_returns_404()
    {
        using var client = await AdminClientAsync();

        var response = await client.PostAsync($"{Endpoint}/999999/retry", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// A <c>Sent</c> delivery is never retryable. The refusal is reached before the disabled-email
    /// guard matters, so this pins the terminal-state rule rather than the configuration one.
    /// </summary>
    [Fact]
    public async Task Retrying_a_sent_delivery_returns_409()
    {
        var sent = new NotificationDelivery(NotificationType.AngebotReady, "Angebot", NextEntityId());
        sent.MarkSent(DateTime.UtcNow);
        var id = await SeedAsync(sent);

        using var client = await AdminClientAsync();

        var response = await client.PostAsync($"{Endpoint}/{id}/retry", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Inspector_is_forbidden_from_retrying()
    {
        var id = await SeedAsync(new NotificationDelivery(NotificationType.NewWebsiteLead, "Lead", NextEntityId()));
        using var client = await ClientAsync(RenoTrackApiFactory.InspectorEmail, RenoTrackApiFactory.InspectorPassword);

        var response = await client.PostAsync($"{Endpoint}/{id}/retry", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Anonymous_cannot_retry()
    {
        var id = await SeedAsync(new NotificationDelivery(NotificationType.NewWebsiteLead, "Lead", NextEntityId()));
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"{Endpoint}/{id}/retry", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Slice 4's read must keep working unchanged now that the controller has a second dependency —
    /// the whole reason the retry service is registered unconditionally (S5-9).
    /// </summary>
    [Fact]
    public async Task The_list_endpoint_still_works_with_email_delivery_disabled()
    {
        var id = await SeedAsync(new NotificationDelivery(NotificationType.NewWebsiteLead, "Lead", NextEntityId()));
        using var client = await AdminClientAsync();

        var body = await ReadAsync(client, $"{Endpoint}?pageSize=100");

        Assert.Contains(id, Ids(body));
    }

    // ---------- helpers ----------

    private static int NextEntityId() => Random.Shared.Next(100_000, 999_999);

    private static IEnumerable<int> Ids(JsonElement body) =>
        body.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetInt32());

    private static async Task AssertProblemDetailsAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.TryGetProperty("errors", out _));
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> FindItemAsync(HttpClient client, int id)
    {
        var body = await ReadAsync(client, $"{Endpoint}?pageSize=100");

        return Assert.Single(
            body.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetInt32() == id);
    }

    private async Task<int> SeedAsync(NotificationDelivery delivery)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        dbContext.NotificationDeliveries.Add(delivery);
        await dbContext.SaveChangesAsync();

        return delivery.Id;
    }

    private Task<HttpClient> AdminClientAsync() =>
        ClientAsync(RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

    private async Task<HttpClient> ClientAsync(string email, string password)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
