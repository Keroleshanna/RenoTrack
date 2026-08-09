namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// Architecture.md §8. The Infrastructure implementation (Phase 3) must perform the increment
/// atomically within the same transaction as the entity being numbered, so concurrent requests
/// can never produce duplicate numbers — this interface stays simple, but that guarantee is a
/// hard requirement of whatever implements it.
/// </summary>
public interface INumberGeneratorService
{
    /// <summary>Returns a formatted, unique Angebot number for the given year, e.g. "ANG-2026-00042".</summary>
    Task<string> NextAngebotNumberAsync(int year, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a formatted, unique Invoice number for the given year, e.g. "RE-2026-00017"
    /// (Architecture.md §8's <c>RE-{YYYY}-{sequence:D5}</c>, ERD.md's <c>RE-YYYY-NNNNN</c>).
    /// A second method on the same abstraction, over the same <c>NumberSequences</c> table keyed by
    /// <c>(SequenceType, Year)</c> — not a second mechanism.
    ///
    /// <para>
    /// <b>What this guarantees, and what it does not.</b> Numbers are unique and are never reused,
    /// which is what BR-9 requires. They are **not** guaranteed to be gapless: the increment commits
    /// independently of the caller's own unit of work (D52), so a failure after reservation leaves
    /// that number unused. Callers reduce the window by reserving as late as possible — after every
    /// guard that can be evaluated first — but cannot close it. See D66.
    /// </para>
    /// </summary>
    Task<string> NextInvoiceNumberAsync(int year, CancellationToken cancellationToken);
}
