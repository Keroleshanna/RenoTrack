using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RenoTrack.Website.PublicApi;

/// <summary>
/// The typed <c>HttpClient</c> behind <see cref="IPublicAngebotClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This class is the only place an HTTP status becomes a customer outcome</b>, which is what
/// keeps that mapping reviewable in one file rather than spread across page models.
/// </para>
/// <para>
/// <b>The token is never logged.</b> Not in a success path, not in a failure path, not in an
/// exception message this class constructs. It is a credential (Architecture.md §7.2), and a log
/// sink is exactly the kind of place that outlives the request and is read by more people than the
/// request ever reached — the reasoning <c>RouteDiagnostics</c> records on the API side and
/// <c>LoggingNoOpEmailSender</c> applies to the same value. Failures are logged with the *outcome*
/// and nothing that identifies which link produced it.
/// </para>
/// </remarks>
public sealed class PublicAngebotClient(
    HttpClient httpClient,
    ILogger<PublicAngebotClient> logger) : IPublicAngebotClient
{
    /// <summary>
    /// Matches the API's own serialization: enums as names, camelCase property names
    /// (<c>Program.cs</c>'s <c>AddJsonOptions</c>, D61). Declared here rather than relying on the
    /// framework default so a future change on either side is a visible edit rather than a silent
    /// mismatch.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<CustomerAngebotResult> GetAngebotAsync(string token, CancellationToken cancellationToken)
    {
        // Refused before a request is made. An empty token cannot identify a link, and sending it
        // would append nothing to the path — turning a customer's typo into a request for a
        // different resource entirely.
        if (string.IsNullOrWhiteSpace(token))
        {
            return CustomerAngebotResult.NotFound();
        }

        try
        {
            // Uri.EscapeDataString, even though TokenLinkService emits URL-safe base64 and needs no
            // escaping: what arrives here is whatever was in the customer's address bar, not
            // necessarily a token this system issued. Escaping keeps a hand-edited value a path
            // segment rather than letting '/' or '?' rewrite the request.
            using var response = await httpClient.GetAsync(
                $"api/v1/public/angebote/{Uri.EscapeDataString(token)}",
                cancellationToken);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var angebot = await response.Content.ReadFromJsonAsync<CustomerAngebot>(
                        SerializerOptions, cancellationToken);

                    if (angebot is null || string.IsNullOrWhiteSpace(angebot.AngebotNumber))
                    {
                        // A 200 the Website cannot make sense of is an integration fault, not a
                        // missing quote — so it is reported as an outage, and loudly, because it
                        // means the two sides disagree about the contract.
                        logger.LogError(
                            "The public Angebot endpoint returned 200 with a body this Website could not read.");
                        return CustomerAngebotResult.Unavailable();
                    }

                    return CustomerAngebotResult.Available(angebot);

                // Unknown token, or a token belonging to something other than an Angebot. The API
                // conflates the two deliberately and so does this.
                case HttpStatusCode.NotFound:
                    return CustomerAngebotResult.NotFound();

                case HttpStatusCode.Gone:
                    return CustomerAngebotResult.Expired();

                // The API's shape validator rejects only an empty token, which is already handled
                // above — so a 400 here means the Website sent something the API did not expect,
                // which is a fault on this side and must not be shown to the customer as "your link
                // is wrong".
                case HttpStatusCode.BadRequest:
                    logger.LogError("The public Angebot endpoint rejected this Website's request as malformed.");
                    return CustomerAngebotResult.Unavailable();

                // 429 included: the customer did nothing wrong and the honest answer is "not right
                // now". Behind a correctly configured trust boundary (D97) the limiter partitions on
                // the real client, so this is rare and self-resolving.
                default:
                    logger.LogWarning(
                        "The public Angebot endpoint answered {StatusCode}.",
                        (int)response.StatusCode);
                    return CustomerAngebotResult.Unavailable();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The customer navigated away or the connection dropped. Not an outage, and nothing is
            // rendered — rethrow so the framework abandons the request rather than logging noise.
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            // A refused connection, a DNS failure, a TLS failure, the client timeout, or a body that
            // is not the JSON this Website expects. The exception is logged for operators; the
            // customer is told the quote is temporarily unreachable and nothing else.
            logger.LogError(exception, "The public Angebot endpoint could not be reached.");
            return CustomerAngebotResult.Unavailable();
        }
    }
}
