namespace RenoTrack.Application.Angebote.Commands.DuplicateAngebot;

/// <summary>
/// SRS FR-4.11: start a new Angebot from a previous one, for a job very similar to a past one.
/// </summary>
/// <param name="SourceAngebotId">
/// The Angebot being copied. Restricted to one the requesting Inspector owns —
/// PermissionMatrix.md §3 says "only from Angebote the Inspector has access to", and names
/// "their own" as the v1 default.
/// </param>
/// <param name="TargetLeadId">
/// The Lead the new Draft is created for. A genuine input, not a server-derived value: it names
/// which job the copy is for, not who is acting (D61).
/// </param>
public sealed record DuplicateAngebotCommand(int SourceAngebotId, int TargetLeadId, int InspectorId);
