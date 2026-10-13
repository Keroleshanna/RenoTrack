namespace RenoTrack.Website.PublicApi;

/// <summary>
/// What happened when the Website asked the API for the Invoice behind a customer's token link
/// (Phase 14 Slice 3b, D114).
/// </summary>
/// <remarks>
/// The same four outcomes as <see cref="CustomerAngebotOutcome"/>, for the same reasons. An unknown
/// token and a token for something else are one outcome. Expiry is honest. An outage is never
/// reported as a bad link. There is no "already used" outcome, because an Invoice link is never
/// consumed (PermissionMatrix.md §7).
/// </remarks>
public enum CustomerInvoiceOutcome
{
    /// <summary>The API returned the Invoice. 200.</summary>
    Available,

    /// <summary>No such link, or a link to something other than an Invoice. 404.</summary>
    NotFound,

    /// <summary>The link's validity window has closed. 410.</summary>
    Expired,

    /// <summary>
    /// The API could not answer: a 5xx, a 429, a network failure, a timeout, or a body the Website
    /// could not read.
    /// </summary>
    Unavailable,
}

/// <summary>
/// The three things the customer can be told about their invoice. This mirrors the API's
/// <c>PublicInvoiceStatus</c>, never the internal <c>InvoiceStatus</c>.
/// </summary>
/// <remarks>
/// An unrecognised value fails deserialization and the page reports an outage, rather than
/// defaulting to <see cref="Open"/>. Calling a cancelled invoice payable would be a wrong statement
/// about money the customer owes, which is worse than "not available right now".
/// </remarks>
public enum CustomerInvoiceStatus
{
    Open,
    Paid,
    Void,
}

/// <summary>
/// The Invoice as this Website needs it. <b>Deliberately the Website's own type</b>, mirroring the
/// API's JSON contract: the Website references no backend project (<c>CLAUDE.md</c> §1).
/// </summary>
/// <remarks>
/// <para>
/// Exactly what the API's public read returns, and nothing it does not. No internal id, no storage
/// key, no address, no payment. <paramref name="DocumentAvailable"/> is the one field Slice 3b added.
/// It decides whether the page offers the PDF and carries nothing about where the PDF is stored.
/// </para>
/// <para>
/// <paramref name="DueDate"/> is a calendar date the Admin chose (Invoice.DueDate). It is rendered
/// by its date part only, so no time-zone question arises.
/// </para>
/// </remarks>
public sealed record CustomerInvoice(
    string InvoiceNumber,
    string CustomerName,
    CustomerInvoiceStatus Status,
    decimal NetAmount,
    decimal VatAmount,
    decimal GrossAmount,
    DateTime DueDate,
    bool DocumentAvailable);

/// <summary>
/// The outcome of one lookup, and the Invoice when there is one. Nothing the API said crosses this
/// type: no status code, no ProblemDetails <c>detail</c>, no exception message.
/// </summary>
public sealed record CustomerInvoiceResult(CustomerInvoiceOutcome Outcome, CustomerInvoice? Invoice)
{
    public static CustomerInvoiceResult Available(CustomerInvoice invoice) =>
        new(CustomerInvoiceOutcome.Available, invoice);

    public static CustomerInvoiceResult NotFound() => new(CustomerInvoiceOutcome.NotFound, null);

    public static CustomerInvoiceResult Expired() => new(CustomerInvoiceOutcome.Expired, null);

    public static CustomerInvoiceResult Unavailable() => new(CustomerInvoiceOutcome.Unavailable, null);
}

/// <summary>
/// What happened when the Website asked the API for the archived PDF behind a token link.
/// </summary>
public enum CustomerInvoiceDocumentOutcome
{
    /// <summary>The API served a PDF that passed every check. 200.</summary>
    Available,

    /// <summary>No such link, or a link to something other than an Invoice. 404.</summary>
    NotFound,

    /// <summary>The link's validity window has closed. 410.</summary>
    Expired,

    /// <summary>
    /// The link is valid and the Invoice has no archived document. The API answers 409: the Invoice
    /// was sent before invoice documents were archived (D113), and none is ever made for it.
    /// </summary>
    NoDocument,

    /// <summary>
    /// The API could not answer, or answered with something that is not a PDF this Website will pass
    /// on: the wrong media type, no <c>%PDF-</c> signature, or more than
    /// <see cref="CustomerInvoiceDocument.MaxBytes"/>.
    /// </summary>
    Unavailable,
}

/// <summary>
/// An archived invoice PDF, fully read and checked, on its way to the customer.
/// </summary>
/// <remarks>
/// <b>Buffered, not streamed, deliberately (D114).</b> A body that has started streaming cannot be
/// turned back into an honest error page, and a PDF cannot be checked until it has been read. An
/// invoice is tens of kilobytes, so <see cref="MaxBytes"/> is far above any real document while still
/// bounding what this server will hold in memory for one request.
/// </remarks>
public sealed record CustomerInvoiceDocument(string FileName, byte[] Content)
{
    /// <summary>10 MiB: the most this Website reads for one document.</summary>
    public const int MaxBytes = 10 * 1024 * 1024;

    /// <summary>The media type the API declares and this Website serves.</summary>
    public const string PdfContentType = "application/pdf";
}

/// <summary>The outcome of one document request, and the checked document when there is one.</summary>
public sealed record CustomerInvoiceDocumentResult(
    CustomerInvoiceDocumentOutcome Outcome,
    CustomerInvoiceDocument? Document)
{
    public static CustomerInvoiceDocumentResult Available(CustomerInvoiceDocument document) =>
        new(CustomerInvoiceDocumentOutcome.Available, document);

    public static CustomerInvoiceDocumentResult Of(CustomerInvoiceDocumentOutcome outcome) =>
        outcome == CustomerInvoiceDocumentOutcome.Available
            ? throw new ArgumentException("An available result carries a document.", nameof(outcome))
            : new(outcome, null);
}
