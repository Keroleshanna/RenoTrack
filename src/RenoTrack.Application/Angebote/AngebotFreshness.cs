using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Angebote;

/// <summary>
/// The cross-request half of an Angebot's concurrency contract (<b>D115</b>): a write prepared
/// against an older view of the document is refused, rather than silently overwriting what someone
/// else changed since.
/// </summary>
/// <remarks>
/// <para>
/// <b>A recorded, narrow exception to CLAUDE.md §6</b>, which forbids a handler inspecting an
/// aggregate's state before calling it. That rule exists so a handler never duplicates a Domain
/// guard. This checks no business rule and duplicates none: it asks whether the <i>request</i> is
/// fresh — whether the caller has seen the Angebot as it now is. No Domain method could answer
/// that, because the aggregate has no notion of what a caller last read. D99 Part 2 is the
/// precedent for a handler-level check where no Domain guard exists to defer to. Business
/// invariants stay in the Domain, untouched.
/// </para>
/// <para>
/// <b>It closes the gap between two requests, not the gap inside one.</b> Between this comparison
/// and the commit, another writer can still win; the <c>Version</c> concurrency token refuses that
/// one at the database. The two together are the whole contract, and neither is enough alone.
/// </para>
/// <para>
/// Called after ownership, never before, so an Inspector who does not own the Angebot learns
/// nothing about its version — they get a 403 whatever they sent.
/// </para>
/// </remarks>
public static class AngebotFreshness
{
    /// <exception cref="ConflictException">
    /// The Angebot changed since the caller read it. The message names nothing (D59).
    /// </exception>
    public static void EnsureExpectedVersion(Angebot angebot, int expectedVersion)
    {
        if (angebot.Version != expectedVersion)
        {
            throw new ConflictException(ConflictException.ConcurrentChangeMessage);
        }
    }
}
