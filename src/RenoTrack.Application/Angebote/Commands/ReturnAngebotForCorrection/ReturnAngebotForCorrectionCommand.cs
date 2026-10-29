namespace RenoTrack.Application.Angebote.Commands.ReturnAngebotForCorrection;

/// <summary>
/// "Zur Korrektur zurückholen": an Admin takes an Angebot out of review to correct it themselves
/// (<c>InReview → Draft</c>, SRS FR-5.5, <b>D115</b>). Admin only, and <c>F</c> — no ownership rule
/// applies (CLAUDE.md §16).
/// </summary>
/// <param name="ExpectedVersion">The <c>Version</c> the Admin last read; a mismatch is a 409.</param>
/// <param name="PerformedByAdminId">The Admin, from the token (D61) — the audit actor.</param>
public sealed record ReturnAngebotForCorrectionCommand(int AngebotId, int ExpectedVersion, int PerformedByAdminId);
