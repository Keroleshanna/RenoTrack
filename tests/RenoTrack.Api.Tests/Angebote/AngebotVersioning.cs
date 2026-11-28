using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RenoTrack.Api.Tests.Angebote;

/// <summary>
/// Sends an Angebot write the way the Dashboard does since D115: with the <c>expectedVersion</c> it
/// last read. Each call reads the current version first, as the given client, then sends it — in the
/// body for <c>POST</c>/<c>PUT</c>, as a query parameter for <c>DELETE</c>.
/// </summary>
/// <remarks>
/// <para>
/// One helper rather than a version threaded by hand through every seeding method in the suite. A
/// test that is <i>about</i> freshness passes an explicit version through the <c>expectedVersion</c>
/// overloads instead, so a stale value is always a deliberate choice of the test, never an accident of
/// this helper.
/// </para>
/// <para>
/// The read happens as the caller. A caller who may not read the Angebot (a non-owning Inspector)
/// makes the read fail loudly here — such a test must pass an explicit version, because what it
/// asserts is the 403, which comes before the version check.
/// </para>
/// </remarks>
internal static class AngebotVersioning
{
    public static async Task<int> CurrentVersionAsync(this HttpClient client, int angebotId)
    {
        var response = await client.GetAsync($"/api/v1/angebote/{angebotId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt32();
    }

    public static async Task<HttpResponseMessage> PostVersionedAsync(
        this HttpClient client, int angebotId, string relativePath, object? body = null) =>
        await client.PostVersionedAsync(angebotId, relativePath, await client.CurrentVersionAsync(angebotId), body);

    public static Task<HttpResponseMessage> PostVersionedAsync(
        this HttpClient client, int angebotId, string relativePath, int expectedVersion, object? body = null) =>
        client.PostAsJsonAsync($"/api/v1/angebote/{angebotId}/{relativePath}", WithVersion(body, expectedVersion));

    public static async Task<HttpResponseMessage> PutVersionedAsync(
        this HttpClient client, int angebotId, string relativePath, object body) =>
        await client.PutVersionedAsync(angebotId, relativePath, await client.CurrentVersionAsync(angebotId), body);

    public static Task<HttpResponseMessage> PutVersionedAsync(
        this HttpClient client, int angebotId, string relativePath, int expectedVersion, object body) =>
        client.PutAsJsonAsync($"/api/v1/angebote/{angebotId}/{relativePath}", WithVersion(body, expectedVersion));

    public static async Task<HttpResponseMessage> DeleteVersionedAsync(
        this HttpClient client, int angebotId, string relativePath) =>
        await client.DeleteVersionedAsync(angebotId, relativePath, await client.CurrentVersionAsync(angebotId));

    public static Task<HttpResponseMessage> DeleteVersionedAsync(
        this HttpClient client, int angebotId, string relativePath, int expectedVersion) =>
        client.DeleteAsync($"/api/v1/angebote/{angebotId}/{relativePath}?expectedVersion={expectedVersion}");

    /// <summary>The request body plus <c>expectedVersion</c>, serialised with the API's camelCase names.</summary>
    private static JsonObject WithVersion(object? body, int expectedVersion)
    {
        var json = body is null
            ? new JsonObject()
            : JsonSerializer.SerializeToNode(body, JsonSerializerOptions.Web)!.AsObject();
        json["expectedVersion"] = expectedVersion;
        return json;
    }
}
