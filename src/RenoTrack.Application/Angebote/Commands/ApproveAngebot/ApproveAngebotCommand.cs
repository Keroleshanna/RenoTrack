namespace RenoTrack.Application.Angebote.Commands.ApproveAngebot;

/// <param name="ExpectedVersion">
/// The <c>Version</c> of the Angebot the Admin looked at (D115). Approval participates in the
/// freshness contract because a stale approval would approve a document the Admin never saw — for
/// example one another Admin corrected and resubmitted meanwhile.
/// </param>
public sealed record ApproveAngebotCommand(int AngebotId, int ExpectedVersion, int ReviewedByAdminId);
