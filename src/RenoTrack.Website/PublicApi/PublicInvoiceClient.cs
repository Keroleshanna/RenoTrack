using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RenoTrack.Website.PublicApi;

/// <summary>
/// The typed <c>HttpClient</c> behind <see cref="IPublicInvoiceClient"/> (Phase 14 Slice 3b, D114).
/// </summary>
/// <remarks>
/// <para>
/// <b>This class is the only place an invoice-related HTTP status becomes a customer outcome</b>,
/// exactly as <see cref="PublicAngebotClient"/> is for Angebote, and it follows that class's rules.
/// </para>
/// <para>
/// <b>The token is never logged.</b> Not in a success path, not in a failure path, not in an
/// exception message this class constructs. Failures are logged with the outcome and nothing that
/// identifies the link. <c>IHttpClientFactory</c>'s own request logging is removed where this client
/// is registered (<see cref="PublicApiClientRegistration"/>).
/// </para>
/// <para>
/// <b>Nothing the API returns in a body or header reaches the customer unchecked.</b> The read's
/// body is validated field by field. The document is read in full, bounded, and checked for its
/// declared type and its signature before a byte is returned. Its file name is re-derived through
/// <see cref="CustomerDocumentFileName"/>.
/// </para>
/// </remarks>
public sealed class PublicInvoiceClient(
    HttpClient httpClient,
    ILogger<PublicInvoiceClient> logger) : IPublicInvoiceClient
{
    /// <summary>Matches the API's serialization: enums as names, camelCase (D61).</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    public async Task<CustomerInvoiceResult> GetInvoiceAsync(string token, CancellationToken cancellationToken)
    {
        // An empty token cannot identify a link, and sending it would request a different resource.
        if (string.IsNullOrWhiteSpace(token))
        {
            return CustomerInvoiceResult.NotFound();
        }

        try
        {
            // Escaped because what arrives is whatever was in the address bar, not necessarily a
            // token this system issued: a '/' or '?' must stay inside one path segment.
            using var response = await httpClient.GetAsync(
                $"api/v1/public/invoices/{Uri.EscapeDataString(token)}",
                cancellationToken);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var wire = await response.Content.ReadFromJsonAsync<WireInvoice>(SerializerOptions, cancellationToken);
                    var invoice = wire?.ToCustomerInvoice();

                    if (invoice is null)
                    {
                        // A 200 the Website cannot make sense of means the two sides disagree about
                        // the contract: an outage, reported loudly, never a missing invoice.
                        logger.LogError(
                            "The public Invoice endpoint returned 200 with a body this Website could not read.");
                        return CustomerInvoiceResult.Unavailable();
                    }

                    return CustomerInvoiceResult.Available(invoice);

                case HttpStatusCode.NotFound:
                    return CustomerInvoiceResult.NotFound();

                case HttpStatusCode.Gone:
                    return CustomerInvoiceResult.Expired();

                // The API rejects only an empty token as malformed, which is refused above. A 400 here
                // is this side's fault and must not be shown as "your link is wrong".
                case HttpStatusCode.BadRequest:
                    logger.LogError("The public Invoice endpoint rejected this Website's request as malformed.");
                    return CustomerInvoiceResult.Unavailable();

                default:
                    logger.LogWarning("The public Invoice endpoint answered {StatusCode}.", (int)response.StatusCode);
                    return CustomerInvoiceResult.Unavailable();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(exception, "The public Invoice endpoint could not be reached.");
            return CustomerInvoiceResult.Unavailable();
        }
    }

    public async Task<CustomerInvoiceDocumentResult> GetInvoiceDocumentAsync(
        string token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return CustomerInvoiceDocumentResult.Of(CustomerInvoiceDocumentOutcome.NotFound);
        }

        try
        {
            // Headers first, so a declared length over the bound is refused before any body is read.
            using var response = await httpClient.GetAsync(
                $"api/v1/public/invoices/{Uri.EscapeDataString(token)}/document",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return await ReadDocumentAsync(response, cancellationToken);

                case HttpStatusCode.NotFound:
                    return CustomerInvoiceDocumentResult.Of(CustomerInvoiceDocumentOutcome.NotFound);

                case HttpStatusCode.Gone:
                    return CustomerInvoiceDocumentResult.Of(CustomerInvoiceDocumentOutcome.Expired);

                // A valid link to an Invoice that has no archived document (D113). A state to report,
                // not a fault, so it is not logged as one.
                case HttpStatusCode.Conflict:
                    return CustomerInvoiceDocumentResult.Of(CustomerInvoiceDocumentOutcome.NoDocument);

                case HttpStatusCode.BadRequest:
                    logger.LogError("The public Invoice document endpoint rejected this Website's request as malformed.");
                    return CustomerInvoiceDocumentResult.Of(CustomerInvoiceDocumentOutcome.Unavailable);

                default:
                    logger.LogWarning(
                        "The public Invoice document endpoint answered {StatusCode}.",
                        (int)response.StatusCode);
                    return CustomerInvoiceDocumentResult.Of(CustomerInvoiceDocumentOutcome.Unavailable);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            logger.LogError(exception, "The public Invoice document endpoint could not be reached.");
            return CustomerInvoiceDocumentResult.Of(CustomerInvoiceDocumentOutcome.Unavailable);
        }
    }

    /// <summary>
    /// Reads a 200 in full and passes it on only if it is what it claims to be: declared as
    /// <c>application/pdf</c>, at most <see cref="CustomerInvoiceDocument.MaxBytes"/>, and beginning
    /// with the PDF signature. Anything else is an outage. This Website never forwards an HTML error
    /// page, or an unbounded body, as a customer's invoice.
    /// </summary>
    private async Task<CustomerInvoiceDocumentResult> ReadDocumentAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(mediaType, CustomerInvoiceDocument.PdfContentType, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("The public Invoice document endpoint returned 200 with a media type that is not a PDF.");
            return CustomerInvoiceDocumentResult.Of(CustomerInvoiceDocumentOutcome.Unavailable);
        }

        if (response.Content.Headers.ContentLength > CustomerInvoiceDocument.MaxBytes)
        {
            logger.LogError("The public Invoice document endpoint declared a document over this Website's size limit.");
            return CustomerInvoiceDocumentResult.Of(CustomerInvoiceDocumentOutcome.Unavailable);
        }

        var content = await ReadBoundedAsync(response.Content, cancellationToken);
        if (content is null)
        {
            logger.LogError("The public Invoice document endpoint sent a document over this Website's size limit.");
            return CustomerInvoiceDocumentResult.Of(CustomerInvoiceDocumentOutcome.Unavailable);
        }

        if (!content.AsSpan().StartsWith(PdfSignature))
        {
            logger.LogError("The public Invoice document endpoint returned 200 with a body that is not a PDF.");
            return CustomerInvoiceDocumentResult.Of(CustomerInvoiceDocumentOutcome.Unavailable);
        }

        var disposition = response.Content.Headers.ContentDisposition;
        var fileName = CustomerDocumentFileName.From(disposition?.FileNameStar ?? disposition?.FileName);

        return CustomerInvoiceDocumentResult.Available(new CustomerInvoiceDocument(fileName, content));
    }

    /// <summary>
    /// Reads at most <see cref="CustomerInvoiceDocument.MaxBytes"/>, and returns null if the body
    /// holds more. A declared length can be absent or wrong, so the bound is enforced on the bytes
    /// themselves.
    /// </summary>
    private static async Task<byte[]?> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];

        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > CustomerInvoiceDocument.MaxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// The API's JSON, with every member nullable so a missing field can be told apart from a
    /// default. A 200 whose body lacks one is a contract mismatch, not a zero-euro invoice due on
    /// 1 January 0001 with no PDF.
    /// </summary>
    private sealed record WireInvoice(
        string? InvoiceNumber,
        string? CustomerName,
        CustomerInvoiceStatus? Status,
        decimal? NetAmount,
        decimal? VatAmount,
        decimal? GrossAmount,
        DateTime? DueDate,
        bool? DocumentAvailable)
    {
        public CustomerInvoice? ToCustomerInvoice() =>
            string.IsNullOrWhiteSpace(InvoiceNumber)
            || CustomerName is null
            || Status is null

            // JsonStringEnumConverter also accepts an integer, which can name no defined status.
            || !Enum.IsDefined(Status.Value)
            || NetAmount is null
            || VatAmount is null
            || GrossAmount is null
            || DueDate is null
            || DocumentAvailable is null
                ? null
                : new CustomerInvoice(
                    InvoiceNumber,
                    CustomerName,
                    Status.Value,
                    NetAmount.Value,
                    VatAmount.Value,
                    GrossAmount.Value,
                    DueDate.Value,
                    DocumentAvailable.Value);
    }
}
