namespace RenoTrack.Application.Angebote.Commands.RequestAngebotChanges;

/// <param name="ExpectedVersion">
/// The <c>Version</c> of the Angebot the Admin reviewed (D115), so a comment is never sent about a
/// document that has changed since it was read.
/// </param>
public sealed record RequestAngebotChangesCommand(int AngebotId, string Comment, int ExpectedVersion, int ReviewedByAdminId);
