namespace RenoTrack.Application.Projects.Dtos;

/// <summary>
/// BR-3's running total, in exactly the three fields Sequence Diagram §8 returns
/// (<c>{ agreedTotal, alreadyInvoiced, remaining }</c>) and Wireframes E1/E2 render
/// ("€25,673.36 agreed / €0 invoiced / €25,673.36 remaining").
///
/// <para>
/// <b><see cref="Remaining"/> may be negative, and that negative is the entire warning.</b> BR-3
/// says the system "warns (does not hard-block)" when invoices do not sum to the agreed total, so
/// over-invoicing is a state the system must be able to *report* — clamping at zero would delete
/// the only signal BR-3 asks for, and adding a separate boolean warning flag would invent a
/// contract no document defines. There is deliberately no <c>IsOverInvoiced</c> field: the number
/// carries the information, and the dashboard decides how to render it.
/// </para>
/// <para>
/// <b>This is Project financial-summary data, not Invoice data.</b> `PermissionMatrix.md` §5 grants
/// it Admin <c>F</c> / Inspector <c>R</c> — the same grant as the Project detail read, read-only and
/// **unscoped**. It confers no Invoice-management permission of any kind.
/// </para>
/// </summary>
public sealed record ProjectInvoiceBalanceDto(
    int ProjectId,
    decimal AgreedTotal,
    decimal AlreadyInvoiced,
    decimal Remaining);
