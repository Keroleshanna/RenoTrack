namespace RenoTrack.Domain.Enums;

/// <summary>
/// How a payment reached the company. SRS FR-8.4 names exactly these three ("Bank Transfer, Cash,
/// Other"), and ERD.md's <c>Payments.Method</c> column repeats them ("BankTransfer | Cash |
/// Other").
///
/// <para>
/// <b>No gateway value is declared here, deliberately.</b> SRS FR-8.5 requires the model to accept
/// a future online payment gateway "purely as a new payment method and callback" — that is a
/// statement about what adding one will cost later, not licence to add an unreachable value now.
/// Nothing in Phase 8 can produce one, and CLAUDE.md §4's grow-on-demand discipline applies to
/// enum values as much as to repository methods. (<c>TokenLinkEntityType.Invoice</c> was the
/// opposite case and is not a precedent for this: it was declared early because ERD.md documents
/// that column's domain as exactly two values, so a single-valued enum would have misrepresented
/// the column it maps to. ERD.md documents this column as exactly the three below.)
/// </para>
/// </summary>
public enum PaymentMethod
{
    BankTransfer,
    Cash,
    Other,
}
