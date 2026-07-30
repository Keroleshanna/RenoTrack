namespace RenoTrack.Application.Angebote.Commands.CreateAngebot;

public sealed record CreateAngebotCommand(int LeadId, int? InspectionId, int CreatedByInspectorId);
