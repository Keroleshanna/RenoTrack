using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// Write-side repository for the Invoice aggregate. Every method has a named consumer:
/// <c>AddAsync</c> for <c>CreateInvoiceCommand</c> (Slice 3), <c>GetByIdAsync</c> for
/// <c>SendInvoiceCommand</c> (Slice 4) and reused by mark-paid/void (Slice 5), and
/// <c>HasCompletionBlockingInvoicesForProjectAsync</c> for <c>CompleteProjectCommand</c> (Slice 6)
/// — never speculatively (CLAUDE.md §4).
/// </summary>
public interface IInvoiceRepository
{
    Task AddAsync(Invoice invoice, CancellationToken cancellationToken);

    /// <summary>
    /// Loads an Invoice with its full aggregate — the Payments collection included (CLAUDE.md §4:
    /// there is no partial-load contract for an aggregate root). Added in Phase 8 Slice 4, when
    /// <c>SendInvoiceCommand</c> first needed to load and mutate an existing Invoice.
    /// </summary>
    Task<Invoice?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Whether this Project's Invoices currently block it from being completed — the exact
    /// business question <c>CompleteProjectCommand</c> asks (StateMachine.md §5 assigns the guard
    /// to that command by name), rather than a generic "get invoices by project" the caller would
    /// then have to interpret (CLAUDE.md §4).
    ///
    /// <para>
    /// <b>The predicate has two clauses, and both are deliberate:</b> a Project is blocked when it
    /// has <b>no Invoices at all</b>, <b>or</b> when at least one Invoice is <c>Draft</c>,
    /// <c>Sent</c> or <c>Overdue</c>. <c>Paid</c> and <c>Void</c> never block.
    /// </para>
    /// <para>
    /// <b>The status clause resolves a contradiction inside StateMachine.md itself.</b> §4.3's
    /// guard reads "All Invoices.Status == <c>Paid</c> (or <c>Void</c>)", which blocks a
    /// <c>Draft</c>; §3.4's invariant reads "while any of its Invoices are in <c>Sent</c> or
    /// <c>Overdue</c>", which does not; Sequence Diagram §10's "any invoice not Paid" would block a
    /// <c>Void</c>, contradicting both. Resolved in favour of §4.3 (Phase 8 Slice 6, decision K-1)
    /// and reconciled in StateMachine.md rather than silently picked.
    /// </para>
    /// <para>
    /// <b>The zero-Invoice clause is a Slice 6 decision, not a document's wording.</b> §4.3's "all
    /// Invoices are Paid or Void" is vacuously true over an empty set, which would let a Project
    /// that was never invoiced complete silently; SRS FR-7.3 ("once its final invoice has been
    /// paid") presupposes one exists. Such a Project is therefore completable only through the
    /// FR-8.6 override, with a reason.
    /// </para>
    /// <para>
    /// Declared here, in the Application layer, precisely so the rule is readable where the use
    /// case lives — Infrastructure supplies the query, never the policy.
    /// </para>
    /// </summary>
    Task<bool> HasCompletionBlockingInvoicesForProjectAsync(int projectId, CancellationToken cancellationToken);
}
