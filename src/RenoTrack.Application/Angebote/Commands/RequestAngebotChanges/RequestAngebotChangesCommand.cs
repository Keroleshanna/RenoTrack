namespace RenoTrack.Application.Angebote.Commands.RequestAngebotChanges;

public sealed record RequestAngebotChangesCommand(int AngebotId, string Comment, int ReviewedByAdminId);
