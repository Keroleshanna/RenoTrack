using System.Net;

namespace RenoTrack.Website.Tests.PublicApi;

/// <summary>
/// A hand-written <see cref="HttpMessageHandler"/> standing in for the API.
/// </summary>
/// <remarks>
/// Hand-written, never a mocking framework — the same stance <c>CLAUDE.md</c> §14 already takes for
/// the Application layer's fakes. It records what the client actually sent, which is what several
/// of these tests assert on, and a mocking library would add a second vocabulary to learn for no
/// gain at this size.
/// </remarks>
internal sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Answers every request with the same status and body.</summary>
    public static StubHttpMessageHandler Responding(HttpStatusCode statusCode, string? json = null) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = json is null
                ? new StringContent(string.Empty)
                : new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        }));

    /// <summary>Throws, standing in for a refused connection, a DNS failure or a TLS failure.</summary>
    public static StubHttpMessageHandler Throwing(Exception exception) =>
        new((_, _) => Task.FromException<HttpResponseMessage>(exception));

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return respond(request, cancellationToken);
    }
}
