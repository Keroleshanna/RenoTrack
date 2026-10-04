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

namespace RenoTrack.Api.Tests.Customers;

/// <summary>
/// <c>GET /api/v1/customers/{id}</c> and <c>PUT /api/v1/customers/{id}/address</c> (Phase 14 Slice
/// 2b, D112; <c>PermissionMatrix.md</c> §5 — Admin F, Inspector —). What only a real request shows:
/// the Admin-only role gate reaching an empty 403, the field-keyed 400, that the body cannot reach
/// any field but the address (D61), the exact response surface, and the audit row's actor.
/// </summary>
/// <remarks>
/// The Customer is seeded directly, as a converted website Lead leaves it — the conversion path
/// itself is <c>ProjectEndpointsTests</c>' subject, and the shape that matters here is "a Customer
/// with no address".
/// </remarks>
[Collection("Api")]
public sealed class CustomerEndpointsTests(RenoTrackApiFactory factory)
{
    private const string Corrected = "Musterstraße 1\n12345 Musterstadt";

    // ---- Correct the address ------------------------------------------------

    [Fact]
    public async Task Admin_can_set_the_address_of_a_customer_that_has_none()
    {
        var customerId = await SeedCustomerAsync(address: null);
        using var admin = await AdminClientAsync();

        var response = await admin.PutAsJsonAsync($"/api/v1/customers/{customerId}/address", new { address = Corrected });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(customerId, body.GetProperty("id").GetInt32());
        Assert.Equal(Corrected, body.GetProperty("address").GetString());

        Assert.Equal(Corrected, (await ReloadAsync(customerId)).Address);

        // The follow-up read the Dashboard performs after every write (D81) sees the same value.
        var read = await admin.GetAsync($"/api/v1/customers/{customerId}");
        Assert.Equal(Corrected, (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("address").GetString());
    }

    [Fact]
    public async Task Admin_can_replace_an_existing_address_and_the_value_is_trimmed()
    {
        var customerId = await SeedCustomerAsync(address: "Alte Str. 9");
        using var admin = await AdminClientAsync();

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/customers/{customerId}/address", new { address = "  Neue Str. 3  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Neue Str. 3", (await ReloadAsync(customerId)).Address);
    }

    /// <summary>
    /// D61: the request record carries the address and nothing else, so a body that names other
    /// fields — the ones D112 keeps uncorrectable, plus ids a caller must never choose — changes
    /// none of them.
    /// </summary>
    [Fact]
    public async Task The_body_cannot_reach_any_field_but_the_address()
    {
        var customerId = await SeedCustomerAsync(address: null);
        var before = await ReloadAsync(customerId);
        using var admin = await AdminClientAsync();

        var response = await admin.PutAsJsonAsync($"/api/v1/customers/{customerId}/address", new
        {
            address = Corrected,
            name = "Somebody Else",
            email = "attacker@example.test",
            phone = "+49 000 9999999",
            leadId = 999_999,
            id = 999_999,
            customerId = 999_999,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await ReloadAsync(customerId);
        Assert.Equal(Corrected, after.Address);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.Phone, after.Phone);
        Assert.Equal(before.LeadId, after.LeadId);
    }

    /// <summary>H2: audited against the Customer, naming the real Admin, with no address in it.</summary>
    [Fact]
    public async Task A_correction_is_audited_against_the_customer_by_the_acting_admin()
    {
        var customerId = await SeedCustomerAsync(address: null);
        var adminId = await factory.GetUserIdAsync(RenoTrackApiFactory.AdminEmail);
        using var admin = await AdminClientAsync();

        await admin.PutAsJsonAsync($"/api/v1/customers/{customerId}/address", new { address = Corrected });

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        var entry = await context.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.EntityType == nameof(Customer) && a.EntityId == customerId);

        Assert.Equal(AuditAction.CustomerAddressCorrected, entry.Action);
        Assert.Equal(adminId, entry.PerformedByUserId);
        Assert.Null(entry.Details);
    }

    /// <summary>H4: the address is required; a blank value clears nothing and is a field-keyed 400.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_address_is_a_field_keyed_400_and_nothing_changes(string address)
    {
        var customerId = await SeedCustomerAsync(address: "Bleibt Str. 1");
        using var admin = await AdminClientAsync();

        var response = await admin.PutAsJsonAsync($"/api/v1/customers/{customerId}/address", new { address });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty("Address", out _));
        Assert.Equal("Bleibt Str. 1", (await ReloadAsync(customerId)).Address);
    }

    [Fact]
    public async Task An_address_over_the_maximum_length_is_a_field_keyed_400()
    {
        var customerId = await SeedCustomerAsync(address: null);
        using var admin = await AdminClientAsync();

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/customers/{customerId}/address",
            new { address = new string('a', Customer.MaxAddressLength + 1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty("Address", out _));
        Assert.Null((await ReloadAsync(customerId)).Address);
    }

    [Fact]
    public async Task Correcting_an_unknown_customer_is_a_not_found()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.PutAsJsonAsync("/api/v1/customers/999999/address", new { address = Corrected });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// H1, Admin only. The empty body is what proves the rejection came from the role attribute —
    /// a <c>ForbiddenException</c> raised later would carry ProblemDetails — so this test fails if the
    /// controller's <c>[Authorize(Roles = Admin)]</c> is removed (the Phase 4 Slice 9 lesson).
    /// </summary>
    [Fact]
    public async Task An_inspector_cannot_correct_an_address()
    {
        var customerId = await SeedCustomerAsync(address: null);
        using var inspector = await InspectorClientAsync();

        var response = await inspector.PutAsJsonAsync($"/api/v1/customers/{customerId}/address", new { address = Corrected });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        Assert.Null((await ReloadAsync(customerId)).Address);
    }

    [Fact]
    public async Task Correcting_an_address_requires_authentication()
    {
        var customerId = await SeedCustomerAsync(address: null);
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PutAsJsonAsync($"/api/v1/customers/{customerId}/address", new { address = Corrected });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null((await ReloadAsync(customerId)).Address);
    }

    // ---- Read ---------------------------------------------------------------

    [Fact]
    public async Task Admin_can_read_a_customer_with_no_address()
    {
        var customerId = await SeedCustomerAsync(address: null);
        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync($"/api/v1/customers/{customerId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(customerId, body.GetProperty("id").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("address").ValueKind);
    }

    /// <summary>
    /// H3: the address is personal data, readable by the Admin alone — which is why it is served
    /// here and not through the Inspector-readable Project detail.
    /// </summary>
    [Fact]
    public async Task An_inspector_cannot_read_a_customer()
    {
        var customerId = await SeedCustomerAsync(address: "Musterstr. 1");
        using var inspector = await InspectorClientAsync();

        var response = await inspector.GetAsync($"/api/v1/customers/{customerId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reading_a_customer_requires_authentication()
    {
        var customerId = await SeedCustomerAsync(address: null);
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync($"/api/v1/customers/{customerId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reading_an_unknown_customer_is_a_not_found()
    {
        using var admin = await AdminClientAsync();

        var response = await admin.GetAsync("/api/v1/customers/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// The DTO is deliberately narrow: email and phone exist on the Customer and must not appear here
    /// merely because they exist. Pinned against raw JSON for both actions, so a field added to the
    /// record later cannot slip in unnoticed.
    /// </summary>
    [Fact]
    public async Task Both_actions_expose_exactly_the_documented_fields()
    {
        var customerId = await SeedCustomerAsync(address: null);
        using var admin = await AdminClientAsync();

        var read = await (await admin.GetAsync($"/api/v1/customers/{customerId}"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var corrected = await (await admin.PutAsJsonAsync($"/api/v1/customers/{customerId}/address", new { address = Corrected }))
            .Content.ReadFromJsonAsync<JsonElement>();

        string[] expected = ["id", "leadId", "name", "address"];
        Assert.Equal(expected, read.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(expected, corrected.EnumerateObject().Select(p => p.Name).ToArray());
    }

    // ---- Helpers -----------------------------------------------------------

    private async Task<int> SeedCustomerAsync(string? address)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();

        var lead = Lead.Create(
            "Customer address lead", "0176 5550021", $"customer-{Guid.NewGuid():N}@example.com", LeadSource.Website);
        context.Leads.Add(lead);
        await context.SaveChangesAsync();

        var customer = Customer.Create(lead.Id, lead.Name, lead.Email, lead.Phone, address);
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        return customer.Id;
    }

    private async Task<Customer> ReloadAsync(int customerId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
        return await context.Customers.AsNoTracking().SingleAsync(c => c.Id == customerId);
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
