using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Common.Documents;

/// <summary>
/// Turns an Invoice and the Customer it bills into the <see cref="InvoiceDocument"/> the PDF
/// generator renders (Phase 14 Slice 2, <b>D111</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A pure function, with no repository and no caller yet.</b> Slice 3 loads the two aggregates
/// when it archives an invoice at send time and calls this; Slice 2 builds and tests it, and adds no
/// artificial caller to satisfy a usage rule. The caller is responsible for passing the Customer of
/// the Project the Invoice bills — neither aggregate can see the other (CLAUDE.md §2).
/// </para>
/// <para>
/// <b>It refuses an invoice the document would misrepresent, naming every gap at once.</b> Three
/// cases, all reachable today:
/// </para>
/// <list type="bullet">
///   <item>An empty description — every invoice created before Slice 2. None is invented for it.</item>
///   <item>
///     A positive gross with no VAT lines — the same historical rows. Their split was never stored,
///     and recomputing it from the Angebot now could print a figure the invoice never had.
///   </item>
///   <item>
///     A customer with no address — <c>Customer.Address</c> is optional because the website contact
///     form does not collect one, and BR-5 requires it. An Admin supplies it through
///     <c>Customer.CorrectAddress</c> (Phase 14 Slice 2b, D112); until then the refusal stands, and
///     printing around the gap is not an option. The address is read from the Customer passed in,
///     so a correction reaches every later render without any change to the Invoice.
///   </item>
/// </list>
/// <para>
/// The refusal is an <see cref="InvalidOperationException"/> — the same type the generator throws
/// for an incomplete company identity (D110) — whose message names the missing fields and never
/// their values.
/// </para>
/// <para>
/// <b>The invoice date is the calendar date of <c>IssueDate</c> in Europe/Berlin</b>, through
/// <see cref="InvoiceCalendar"/> (D111 Part 6) — the same calendar the invoice number's year came
/// from, so the printed date and the number can never disagree. The stored value stays the UTC
/// instant; a row read back from the database is <c>Unspecified</c> and is read as UTC. The due date
/// needs no conversion: it was entered as a calendar date and is printed as that date.
/// </para>
/// </remarks>
public static class InvoiceDocumentFactory
{
    /// <exception cref="InvalidOperationException">The invoice or customer lacks data the document requires.</exception>
    public static InvoiceDocument Create(Invoice invoice, Customer customer, InvoiceCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(calendar);

        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(invoice.Description))
            missing.Add("the invoice description");
        if (invoice.GrossAmount.Amount > 0 && invoice.VatLines.Count == 0)
            missing.Add("the invoice's VAT lines");
        if (string.IsNullOrWhiteSpace(customer.Address))
            missing.Add("the customer's address");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invoice {invoice.InvoiceNumber} cannot be rendered as a document. Missing: "
                + string.Join(", ", missing)
                + ". These are never invented or recomputed for a document.");
        }

        return new InvoiceDocument(
            invoice.InvoiceNumber,
            calendar.DateOf(invoice.IssueDate),
            DateOnly.FromDateTime(invoice.DueDate),
            invoice.ServicePeriodStart,
            invoice.ServicePeriodEnd,
            new DocumentRecipient(customer.Name, customer.Address),
            invoice.Description,
            invoice.VatLines
                .OrderBy(line => line.Rate)
                .Select(line => new DocumentVatLine(
                    DocumentFormatting.VatRate(line.Rate),
                    DocumentFormatting.Money(line.NetAmount.Amount),
                    DocumentFormatting.Money(line.VatAmount.Amount)))
                .ToList(),
            DocumentFormatting.Money(invoice.NetAmount.Amount),
            DocumentFormatting.Money(invoice.VatAmount.Amount),
            DocumentFormatting.Money(invoice.GrossAmount.Amount));
    }
}
