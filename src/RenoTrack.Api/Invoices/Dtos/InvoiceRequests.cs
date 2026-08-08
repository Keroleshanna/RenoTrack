namespace RenoTrack.Api.Invoices.Dtos;

/// <summary>
/// The body of <c>POST /api/v1/projects/{projectId}/invoices</c> — exactly the two fields Sequence
/// Diagram §8 sends (<c>{ grossAmount, dueDate }</c>) and Wireframe E2 collects.
///
/// <para>
/// A strict subset of <c>CreateInvoiceCommand</c>, which is what justifies the record existing at
/// all (D61): the Project id comes from the route, and the creating Admin from the token's subject
/// claim. Neither is accepted from the caller.
/// </para>
/// <para>
/// <b>The invoice number is absent, deliberately</b> — it is reserved server-side from the
/// <c>NumberSequences</c> table (Architecture.md §8). A caller-supplied number could collide with a
/// reserved one or reuse a voided one, which BR-9 forbids outright.
/// </para>
/// </summary>
public sealed record CreateInvoiceRequest(decimal GrossAmount, DateTime DueDate);
