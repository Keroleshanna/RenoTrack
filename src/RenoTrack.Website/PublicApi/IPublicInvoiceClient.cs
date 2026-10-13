namespace RenoTrack.Website.PublicApi;

/// <summary>
/// The Website's one way to reach the API's customer-facing invoice surface
/// (<c>GET /api/v1/public/invoices/{token}</c>, SRS FR-8.3, and <c>GET .../document</c>, Phase 14
/// Slice 3b, D114).
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own interface rather than two more methods on <see cref="IPublicAngebotClient"/></b>, whose
/// name says what it reaches. It grows one method per real use case (<c>CLAUDE.md</c> §4). There
/// are two today: the page's read and the PDF download.
/// </para>
/// <para>
/// <b>Every call is server-side</b> (D97). The token never reaches a browser script, and the API's
/// origin is never disclosed to the customer's browser.
/// </para>
/// </remarks>
public interface IPublicInvoiceClient
{
    /// <summary>
    /// Fetches the Invoice behind <paramref name="token"/>. <b>Never throws for a failure the
    /// customer could cause or observe</b>: every one is an outcome.
    /// </summary>
    Task<CustomerInvoiceResult> GetInvoiceAsync(string token, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches the archived PDF behind <paramref name="token"/>, read in full and checked before
    /// anything is returned. <b>Never throws for a failure the customer could cause or observe.</b>
    /// </summary>
    Task<CustomerInvoiceDocumentResult> GetInvoiceDocumentAsync(string token, CancellationToken cancellationToken);
}
