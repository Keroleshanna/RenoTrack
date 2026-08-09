namespace RenoTrack.Domain.Enums;

/// <summary>
/// StateMachine.md §3.1's five Invoice states, exactly — no more, no fewer. ERD.md's
/// <c>Invoices.Status</c> column documents the same set ("Draft | Sent | Paid | Overdue | Void")
/// and SRS FR-8.2 names four of them in prose.
///
/// <para>
/// <see cref="Overdue"/> is a real stored state rather than something derived at read time: §3.2
/// draws it as a node with its own outgoing transitions (<c>Overdue → Paid</c>,
/// <c>Overdue → Void</c>), and ERD.md §3 recommends an index on <c>(Status, DueDate)</c> whose
/// only stated purpose is the "Overdue-detection scheduled check". Both only make sense for a
/// column. <b>What drives the transition automatically is a separate question, deliberately left
/// open in Phase 8</b> — see <c>Invoice.MarkOverdue</c>.
/// </para>
/// <para>
/// <see cref="Paid"/> and <see cref="Void"/> are terminal; §3.2 gives neither an outgoing edge.
/// </para>
/// </summary>
public enum InvoiceStatus
{
    Draft,
    Sent,
    Paid,
    Overdue,
    Void,
}
