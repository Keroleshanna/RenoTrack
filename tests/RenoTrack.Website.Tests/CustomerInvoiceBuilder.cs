using RenoTrack.Website.PublicApi;

namespace RenoTrack.Website.Tests;

/// <summary>
/// Invoices and invoice documents for the Website tests (Phase 14 Slice 3b). The figures are
/// fictional and internally consistent. The JSON mirrors what the API's public read actually
/// serializes: camelCase, enums by name.
/// </summary>
internal static class CustomerInvoiceBuilder
{
    public const string Number = "RE-2026-00017";
    public const string CustomerName = "M. Klein";

    public static CustomerInvoice Open(bool documentAvailable = true) => With(CustomerInvoiceStatus.Open, documentAvailable);

    public static CustomerInvoice With(CustomerInvoiceStatus status, bool documentAvailable = true) => new(
        Number,
        CustomerName,
        status,
        NetAmount: 6_722.69m,
        VatAmount: 1_277.31m,
        GrossAmount: 8_000.00m,
        DueDate: new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc),
        DocumentAvailable: documentAvailable);

    /// <summary>A minimal byte sequence that starts like a real PDF and ends like one.</summary>
    public static byte[] PdfBytes { get; } = "%PDF-1.7\n%âãÏÓ\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"u8.ToArray();

    public static CustomerInvoiceDocument Document() => new($"Rechnung-{Number}.pdf", PdfBytes);

    public static string Json(
        string status = "Open",
        bool documentAvailable = true,
        string invoiceNumber = Number) =>
        $$"""
        {
          "invoiceNumber": "{{invoiceNumber}}",
          "customerName": "{{CustomerName}}",
          "status": "{{status}}",
          "netAmount": 6722.69,
          "vatAmount": 1277.31,
          "grossAmount": 8000.00,
          "dueDate": "2026-09-15T00:00:00",
          "documentAvailable": {{(documentAvailable ? "true" : "false")}}
        }
        """;
}
