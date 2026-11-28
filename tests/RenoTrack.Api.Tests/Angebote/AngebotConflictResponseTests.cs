using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RenoTrack.Api.ErrorHandling;
using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Api.Tests.Angebote;

/// <summary>
/// The one generic 409 of the Angebot's versioned writes (<b>D115</b>): a stale version, an Angebot
/// that is no longer editable, and a lost race all leave the API as the same response, naming no id
/// and no state.
/// </summary>
/// <remarks>
/// <para>
/// <b>The case this class exists for is "no longer editable".</b> That refusal is the Domain's own
/// <see cref="InvalidOperationException"/>, whose message names the Angebot id and its status, and
/// D59 publishes a mapped exception's message as <c>detail</c>. It reached the response until the
/// boundary sanitisation was added — these tests are what keep it from coming back.
/// </para>
/// <para>
/// The assertions read the whole body as text, not just <c>detail</c>, so the id cannot leak through
/// <c>instance</c> or any other member either. And one test proves the opposite half: the Domain's
/// precise message still reaches the log, so diagnosis loses nothing.
/// </para>
/// </remarks>
[Collection("Api")]
public sealed class AngebotConflictResponseTests(RenoTrackApiFactory factory)
{
    private static readonly object LineValues = new
    {
        description = "Fliesen verlegen",
        specification = (string?)null,
        unitCode = "m2",
        quantity = 4m,
        unitPrice = 50.00m,
        vatRate = "Standard",
    };

    /// <summary>
    /// An Admin with the <i>current</i> version edits an Angebot that is in review: the version check
    /// passes, the Domain's edit lock refuses — and the response must not say so in the Domain's words.
    /// </summary>
    [Fact]
    public async Task A_not_editable_409_publishes_the_generic_detail_and_no_id()
    {
        var (inspector, admin, angebotId, itemId) = await AngebotInReviewAsync(factory);

        var response = await admin.PutVersionedAsync(angebotId, $"items/{itemId}", LineValues);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        AssertNamesNothing(raw, "/api/v1/Angebote/{id:int}/items/{itemId:int}");

        // The refusal really was the edit lock, not a stale version in disguise.
        Assert.Equal("InReview", (await ReadAsync(inspector, angebotId)).GetProperty("status").GetString());
    }

    /// <summary>The same sanitisation on a DELETE, whose expectedVersion arrives in the query string.</summary>
    [Fact]
    public async Task A_not_editable_409_on_a_delete_is_sanitised_too()
    {
        var (_, admin, angebotId, itemId) = await AngebotInReviewAsync(factory);

        var response = await admin.DeleteVersionedAsync(angebotId, $"items/{itemId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        AssertNamesNothing(raw, "/api/v1/Angebote/{id:int}/items/{itemId:int}");
    }

    /// <summary>
    /// A Domain refusal on a review transition — approving a Draft — goes through the same boundary.
    /// </summary>
    [Fact]
    public async Task A_refused_review_transition_is_sanitised_too()
    {
        var (inspector, angebotId, _) = await DraftWithLineAsync(factory);
        using var admin = await ClientAsync(factory, RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

        var response = await admin.PostVersionedAsync(angebotId, "approve");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        AssertNamesNothing(raw, "/api/v1/Angebote/{id:int}/approve");
        Assert.Equal("Draft", (await ReadAsync(inspector, angebotId)).GetProperty("status").GetString());
    }

    /// <summary>
    /// <b>Opt-in, and narrow.</b> An Angebot endpoint without the marker keeps its specific, caller-
    /// authored 409 — here send, on an Angebot that was never approved — so the sanitisation cannot
    /// quietly become "every 409 in the API forgets why".
    /// </summary>
    [Fact]
    public async Task An_unmarked_endpoint_keeps_its_own_409_detail()
    {
        var (_, angebotId, _) = await DraftWithLineAsync(factory);
        using var admin = await ClientAsync(factory, RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);

        var response = await admin.PostAsync($"/api/v1/angebote/{angebotId}/send", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var detail = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString();
        Assert.NotEqual(GenericConflictResponseAttribute.Detail, detail);
    }

    /// <summary>
    /// Only the published body changes: the Domain's precise message — id and status included — still
    /// reaches the log at Warning, with the exception, which is D59's mitigation.
    /// </summary>
    [Fact]
    public async Task The_domain_message_still_reaches_the_log()
    {
        var logs = new CapturingLoggerProvider();
        using var isolated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(logs)));

        var (_, admin, angebotId, itemId) = await AngebotInReviewAsync(isolated);

        var response = await admin.PutVersionedAsync(angebotId, $"items/{itemId}", LineValues);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(logs.ExceptionMessages, message =>
            message.Contains($"Angebot {angebotId}", StringComparison.Ordinal)
            && message.Contains("InReview", StringComparison.Ordinal));
    }

    // ---- Helpers -----------------------------------------------------------

    /// <summary>
    /// Structural rather than substring checks: small database ids such as <c>1</c> occur by chance in
    /// any text (a trace id, a status code), so "the body does not contain the id" would be both
    /// brittle and weak. Instead the body must consist of exactly the six members a sanitised
    /// ProblemDetails has, the detail must be the generic text, the instance a route template, and
    /// the detail may carry no digit at all.
    /// </summary>
    private static void AssertNamesNothing(string raw, string expectedInstance)
    {
        var problem = JsonDocument.Parse(raw).RootElement;

        Assert.Equal(
            ["detail", "instance", "status", "title", "traceId", "type"],
            problem.EnumerateObject().Select(member => member.Name).Order().ToArray());
        Assert.Equal(GenericConflictResponseAttribute.Detail, problem.GetProperty("detail").GetString());
        Assert.Equal(expectedInstance, problem.GetProperty("instance").GetString());
        Assert.Equal("Conflict", problem.GetProperty("title").GetString());

        // The detail carries no number at all, so no id can hide in it. The instance is pinned
        // above to the exact route template, whose only digit is the "1" of "api/v1".
        Assert.DoesNotContain(problem.GetProperty("detail").GetString()!, char.IsDigit);

        // And no internal state name reaches the body.
        foreach (var status in Enum.GetNames<AngebotStatus>())
        {
            Assert.DoesNotContain(status, raw);
        }
    }

    private static async Task<HttpClient> ClientAsync(WebApplicationFactory<Program> host, string email, string password)
    {
        var client = host.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }

    private async Task<(HttpClient Inspector, int AngebotId, int ItemId)> DraftWithLineAsync(WebApplicationFactory<Program> host)
    {
        var inspectorId = await factory.GetUserIdAsync(RenoTrackApiFactory.InspectorEmail);

        int leadId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RenoTrackDbContext>();
            var lead = Lead.Create("Conflict lead", "0176 5550409", "conflict@example.com", LeadSource.Phone);
            context.Leads.Add(lead);
            await context.SaveChangesAsync();
            lead.AssignInspector(inspectorId);
            lead.MarkInspectionScheduled();
            lead.MarkInspectionDone();
            await context.SaveChangesAsync();
            leadId = lead.Id;
        }

        var inspector = await ClientAsync(host, RenoTrackApiFactory.InspectorEmail, RenoTrackApiFactory.InspectorPassword);
        var created = await inspector.PostAsJsonAsync($"/api/v1/leads/{leadId}/angebote", new { inspectionId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var angebotId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var section = await inspector.PostVersionedAsync(angebotId, "sections", new { title = "Pos. 1", sortOrder = 1 });
        var sectionId = (await section.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var item = await inspector.PostVersionedAsync(angebotId, "items", new
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

        return (inspector, angebotId, (await item.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("item").GetProperty("id").GetInt32());
    }

    private async Task<(HttpClient Inspector, HttpClient Admin, int AngebotId, int ItemId)> AngebotInReviewAsync(
        WebApplicationFactory<Program> host)
    {
        var (inspector, angebotId, itemId) = await DraftWithLineAsync(host);
        Assert.Equal(HttpStatusCode.OK, (await inspector.PostVersionedAsync(angebotId, "submit-for-review")).StatusCode);

        var admin = await ClientAsync(host, RenoTrackApiFactory.AdminEmail, RenoTrackApiFactory.AdminPassword);
        return (inspector, admin, angebotId, itemId);
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, int angebotId) =>
        await (await client.GetAsync($"/api/v1/angebote/{angebotId}")).Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Collects the messages of logged exceptions, which is where the Domain's wording lives.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages = [];

        public List<string> ExceptionMessages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (exception is null)
                {
                    return;
                }

                lock (messages)
                {
                    messages.Add(exception.Message);
                }
            }
        }
    }
}
