namespace RenoTrack.Application.Angebote.Commands.ApproveAngebot;

public sealed record ApproveAngebotCommand(int AngebotId, int ReviewedByAdminId);
