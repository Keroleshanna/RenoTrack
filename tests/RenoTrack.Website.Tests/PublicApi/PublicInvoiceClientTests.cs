using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Tests.PublicApi;

/// <summary>
/// The Website's boundary to the API's invoice surface (Phase 14 Slice 3b, D114). These own the
/// mapping from every HTTP answer, malformed body and transport failure to what a customer is told,
/// and the checks a document must pass before a byte of it is passed on.
/// </summary>
/// <remarks>
/// No server and no database: a stub transport answers, so every case runs on any operating system,
/// including CI's Linux job.
/// </remarks>
public sealed class PublicInvoiceClientTests
{
    private const string Token = "Cli5-Nm3xQ2wYc0KpL7sTvE1aZoI4hJd6UgXbn5MtCk";

    private static PublicInvoiceClient ClientFor(StubHttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") },
            NullLogger<PublicInvoiceClient>.Instance);

    private static StubHttpMessageHandler Pdf(
        byte[] body,
        string? mediaType = "application/pdf",
        string? fileName = "Rechnung-RE-2026-00017.pdf",
        long? declaredLength = null) =>
        new((_, _) =>
        {
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = mediaType is null ? null : new MediaTypeHeaderValue(mediaType);
            if (fileName is not null)
            {
                content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = fileName };
            }

            if (declaredLength is not null)
            {
                content.Headers.ContentLength = declaredLength;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });

    // ---- The read: outcomes --------------------------------------------------------

    [Fact]
    public async Task A_200_yields_the_invoice_with_every_field()
    {
        var handler = StubHttpMessageHandler.Responding(HttpStatusCode.OK, CustomerInvoiceBuilder.Json());

        var result = await ClientFor(handler).GetInvoiceAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerInvoiceOutcome.Available, result.Outcome);
        Assert.Equal(CustomerInvoiceBuilder.Open(), result.Invoice);
    }

    [Theory]
    [InlineData("Open", CustomerInvoiceStatus.Open)]
    [InlineData("Paid", CustomerInvoiceStatus.Paid)]
    [InlineData("Void", CustomerInvoiceStatus.Void)]
    public async Task Each_public_status_is_read_by_name(string wire, CustomerInvoiceStatus expected)
    {
        var handler = StubHttpMessageHandler.Responding(HttpStatusCode.OK, CustomerInvoiceBuilder.Json(status: wire));

        var result = await ClientFor(handler).GetInvoiceAsync(Token, CancellationToken.None);

        Assert.Equal(expected, result.Invoice!.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Document_availability_crosses_the_boundary(bool available)
    {
        var handler = StubHttpMessageHandler.Responding(
            HttpStatusCode.OK, CustomerInvoiceBuilder.Json(documentAvailable: available));

        var result = await ClientFor(handler).GetInvoiceAsync(Token, CancellationToken.None);

        Assert.Equal(available, result.Invoice!.DocumentAvailable);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, CustomerInvoiceOutcome.NotFound)]
    [InlineData(HttpStatusCode.Gone, CustomerInvoiceOutcome.Expired)]
    [InlineData(HttpStatusCode.BadRequest, CustomerInvoiceOutcome.Unavailable)]
    [InlineData(HttpStatusCode.Conflict, CustomerInvoiceOutcome.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, CustomerInvoiceOutcome.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, CustomerInvoiceOutcome.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, CustomerInvoiceOutcome.Unavailable)]
    public async Task Each_status_maps_to_one_outcome(HttpStatusCode status, CustomerInvoiceOutcome expected)
    {
        var handler = StubHttpMessageHandler.Responding(status, "{}");

        var result = await ClientFor(handler).GetInvoiceAsync(Token, CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.Invoice);
    }

    /// <summary>
    /// A 200 whose body lacks a field the contract requires is a contract mismatch: an outage, never
    /// a zero-euro invoice, a year-1 due date or a silently missing PDF link.
    /// </summary>
    [Theory]
    [InlineData("invoiceNumber")]
    [InlineData("customerName")]
    [InlineData("status")]
    [InlineData("netAmount")]
    [InlineData("vatAmount")]
    [InlineData("grossAmount")]
    [InlineData("dueDate")]
    [InlineData("documentAvailable")]
    public async Task A_200_missing_a_required_field_is_an_outage(string field)
    {
        var json = string.Join('\n', CustomerInvoiceBuilder.Json()
            .Split('\n')
            .Where(line => !line.Contains($"\"{field}\"", StringComparison.Ordinal)));

        // Drop a trailing comma the removal may have left, so the body stays valid JSON and the
        // case tests the missing field rather than a parse error.
        json = System.Text.RegularExpressions.Regex.Replace(json, ",\\s*}", "}");

        var handler = StubHttpMessageHandler.Responding(HttpStatusCode.OK, json);

        var result = await ClientFor(handler).GetInvoiceAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerInvoiceOutcome.Unavailable, result.Outcome);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("[]")]
    public async Task An_unreadable_200_is_an_outage(string body)
    {
        var handler = StubHttpMessageHandler.Responding(HttpStatusCode.OK, body);

        var result = await ClientFor(handler).GetInvoiceAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerInvoiceOutcome.Unavailable, result.Outcome);
    }

    /// <summary>
    /// An unknown status is never read as Open: calling a cancelled invoice payable would be a wrong
    /// statement about money. An integer is refused too, since it can name no defined status.
    /// </summary>
    [Theory]
    [InlineData("\"Overdue\"")]
    [InlineData("\"Draft\"")]
    [InlineData("7")]
    public async Task An_unknown_status_is_an_outage(string status)
    {
        var json = CustomerInvoiceBuilder.Json().Replace("\"Open\"", status, StringComparison.Ordinal);
        var handler = StubHttpMessageHandler.Responding(HttpStatusCode.OK, json);

        var result = await ClientFor(handler).GetInvoiceAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerInvoiceOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task A_transport_failure_is_an_outage()
    {
        var handler = StubHttpMessageHandler.Throwing(new HttpRequestException("Connection refused."));

        var result = await ClientFor(handler).GetInvoiceAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerInvoiceOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task A_timeout_is_an_outage()
    {
        var handler = StubHttpMessageHandler.Throwing(new TaskCanceledException("The request timed out."));

        var result = await ClientFor(handler).GetInvoiceAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerInvoiceOutcome.Unavailable, result.Outcome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_token_is_not_found_without_a_request(string token)
    {
        var handler = StubHttpMessageHandler.Responding(HttpStatusCode.OK, CustomerInvoiceBuilder.Json());

        var result = await ClientFor(handler).GetInvoiceAsync(token, CancellationToken.None);

        Assert.Equal(CustomerInvoiceOutcome.NotFound, result.Outcome);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task The_read_requests_the_public_invoice_route_with_the_token_escaped()
    {
        var handler = StubHttpMessageHandler.Responding(HttpStatusCode.NotFound, "{}");

        await ClientFor(handler).GetInvoiceAsync("a/b?c", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/v1/public/invoices/a%2Fb%3Fc", request.RequestUri!.AbsolutePath);
        Assert.Empty(request.RequestUri.Query);
    }

    [Fact]
    public async Task A_cancelled_request_is_not_reported_as_an_outage()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var handler = new StubHttpMessageHandler((_, token) => Task.FromCanceled<HttpResponseMessage>(token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ClientFor(handler).GetInvoiceAsync(Token, cancellation.Token));
    }

    // ---- The document: outcomes ------------------------------------------------------

    [Fact]
    public async Task A_valid_pdf_is_returned_byte_for_byte_with_its_name()
    {
        var handler = Pdf(CustomerInvoiceBuilder.PdfBytes);

        var result = await ClientFor(handler).GetInvoiceDocumentAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerInvoiceDocumentOutcome.Available, result.Outcome);
        Assert.Equal(CustomerInvoiceBuilder.PdfBytes, result.Document!.Content);
        Assert.Equal("Rechnung-RE-2026-00017.pdf", result.Document.FileName);
    }

    [Fact]
    public async Task The_download_requests_the_public_document_route_with_the_token_escaped()
    {
        var handler = StubHttpMessageHandler.Responding(HttpStatusCode.NotFound, "{}");

        await ClientFor(handler).GetInvoiceDocumentAsync("a/b?c", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/v1/public/invoices/a%2Fb%3Fc/document", request.RequestUri!.AbsolutePath);
        Assert.Empty(request.RequestUri.Query);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, CustomerInvoiceDocumentOutcome.NotFound)]
    [InlineData(HttpStatusCode.Gone, CustomerInvoiceDocumentOutcome.Expired)]
    [InlineData(HttpStatusCode.Conflict, CustomerInvoiceDocumentOutcome.NoDocument)]
    [InlineData(HttpStatusCode.BadRequest, CustomerInvoiceDocumentOutcome.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, CustomerInvoiceDocumentOutcome.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, CustomerInvoiceDocumentOutcome.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, CustomerInvoiceDocumentOutcome.Unavailable)]
    public async Task Each_document_status_maps_to_one_outcome(HttpStatusCode status, CustomerInvoiceDocumentOutcome expected)
    {
        var handler = StubHttpMessageHandler.Responding(status, "{}");

        var result = await ClientFor(handler).GetInvoiceDocumentAsync(Token, CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.Document);
    }

    // ---- The document: what is never passed on ------------------------------------------

    /// <summary>An HTML error page or a JSON body served as 200 is never handed to a customer as their invoice.</summary>
    [Theory]
    [InlineData("text/html")]
    [InlineData("application/json")]
    [InlineData("application/octet-stream")]
    [InlineData(null)]
    public async Task A_200_that_is_not_declared_a_pdf_is_an_outage(string? mediaType)
    {
        var handler = Pdf(CustomerInvoiceBuilder.PdfBytes, mediaType);

        var result = await ClientFor(handler).GetInvoiceDocumentAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerInvoiceDocumentOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Document);
    }

    [Theory]
    [InlineData("<!DOCTYPE html><html>error</html>")]
    [InlineData("")]
    [InlineData("%PD")]
    [InlineData(" %PDF-1.7")]
    public async Task A_200_without_the_pdf_signature_is_an_outage(string body)
    {
        var handler = Pdf(System.Text.Encoding.ASCII.GetBytes(body));

        var result = await ClientFor(handler).GetInvoiceDocumentAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerInvoiceDocumentOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task A_document_at_exactly_the_limit_is_accepted()
    {
        var body = new byte[CustomerInvoiceDocument.MaxBytes];
        "%PDF-"u8.CopyTo(body);
        var handler = Pdf(body);

        var result = await ClientFor(handler).GetInvoiceDocumentAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerInvoiceDocumentOutcome.Available, result.Outcome);
        Assert.Equal(CustomerInvoiceDocument.MaxBytes, result.Document!.Content.Length);
    }

    /// <summary>The bound is enforced on the bytes themselves, whatever length is declared.</summary>
    [Fact]
    public async Task A_document_over_the_limit_is_an_outage_even_with_no_declared_length()
    {
        var body = new byte[CustomerInvoiceDocument.MaxBytes + 1];
        "%PDF-"u8.CopyTo(body);
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var content = new StreamContent(new UnseekableStream(body));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });

        var result = await ClientFor(handler).GetInvoiceDocumentAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerInvoiceDocumentOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Document);
    }

    [Fact]
    public async Task A_declared_length_over_the_limit_is_refused()
    {
        var handler = Pdf(CustomerInvoiceBuilder.PdfBytes, declaredLength: CustomerInvoiceDocument.MaxBytes + 1L);

        var result = await ClientFor(handler).GetInvoiceDocumentAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerInvoiceDocumentOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public void The_limit_is_ten_mebibytes()
    {
        Assert.Equal(10 * 1024 * 1024, CustomerInvoiceDocument.MaxBytes);
    }

    /// <summary>The API's declared name is re-derived, never trusted. See CustomerDocumentFileNameTests.</summary>
    [Theory]
    [InlineData("Rechnung-RE-2026-00017.pdf", "Rechnung-RE-2026-00017.pdf")]
    [InlineData("../../etc/passwd", "etc-passwd.pdf")]
    [InlineData(null, "Rechnung.pdf")]
    public async Task The_declared_file_name_is_sanitised(string? declared, string expected)
    {
        var handler = Pdf(CustomerInvoiceBuilder.PdfBytes, fileName: declared);

        var result = await ClientFor(handler).GetInvoiceDocumentAsync(Token, CancellationToken.None);

        Assert.Equal(expected, result.Document!.FileName);
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task A_document_transport_failure_is_an_outage(Exception failure)
    {
        var handler = StubHttpMessageHandler.Throwing(failure);

        var result = await ClientFor(handler).GetInvoiceDocumentAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerInvoiceDocumentOutcome.Unavailable, result.Outcome);
    }

    public static TheoryData<Exception> TransportFailures =>
    [
        new HttpRequestException("Connection refused."),
        new TaskCanceledException("The request timed out."),
    ];

    /// <summary>A connection that drops part-way through the body is an outage, not a truncated PDF.</summary>
    [Fact]
    public async Task A_body_that_fails_part_way_is_an_outage()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var content = new StreamContent(new FailingStream("%PDF-1.7"u8.ToArray()));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });

        var result = await ClientFor(handler).GetInvoiceDocumentAsync(Token, CancellationToken.None);

        Assert.Equal(CustomerInvoiceDocumentOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Document);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task An_empty_token_is_not_found_without_a_download(string token)
    {
        var handler = Pdf(CustomerInvoiceBuilder.PdfBytes);

        var result = await ClientFor(handler).GetInvoiceDocumentAsync(token, CancellationToken.None);

        Assert.Equal(CustomerInvoiceDocumentOutcome.NotFound, result.Outcome);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void An_available_result_cannot_be_built_without_a_document()
    {
        Assert.Throws<ArgumentException>(
            () => CustomerInvoiceDocumentResult.Of(CustomerInvoiceDocumentOutcome.Available));
    }

    /// <summary>A stream that cannot report its length, so the size bound must come from the bytes read.</summary>
    private sealed class UnseekableStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();
    }

    /// <summary>Serves a few bytes, then fails as a dropped connection does.</summary>
    private sealed class FailingStream(byte[] prefix) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position < prefix.Length)
            {
                var n = Math.Min(count, prefix.Length - _position);
                Array.Copy(prefix, _position, buffer, offset, n);
                _position += n;
                return n;
            }

            throw new IOException("The connection was reset.");
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
