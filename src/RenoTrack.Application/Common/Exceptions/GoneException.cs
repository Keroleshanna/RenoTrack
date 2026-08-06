namespace RenoTrack.Application.Common.Exceptions;

/// <summary>
/// Thrown when a resource existed, was reachable, and has now permanently lapsed — distinct from
/// NotFoundException ("no such thing, and there may never have been") and ConflictException ("it
/// exists, but its current state refuses this request"). The API layer maps this to 410 Gone.
///
/// Added for exactly one documented scenario, per CLAUDE.md §17's rule that exception types arrive
/// when a real case needs them: an expired customer token link. Sequence Diagram §6 names the
/// status explicitly ("404 / 410 Gone") and §12 requires the reason to be specific, so folding
/// expiry into a 404 would contradict both documents.
///
/// **Why distinguishing "expired" from "unknown" leaks nothing here.** Elsewhere this project
/// deliberately refuses to distinguish failures — every login failure returns an identical 401
/// (D60) — because email addresses are guessable and the distinction turns the endpoint into an
/// enumeration oracle. A token is 256 bits of CSPRNG output: an attacker cannot produce one to
/// probe with, so the only person who can ever see this status is someone genuinely holding a real
/// link. For them, "this link has expired, please contact us" (Sequence Diagram §6's own wording)
/// is the useful answer, and a 404 would send them hunting for a mistyped URL that does not exist.
/// </summary>
public sealed class GoneException(string message) : Exception(message);
