namespace RenoTrack.Application.Inspections.Commands.UpdateInspectionNotes;

public sealed record UpdateInspectionNotesCommand(int InspectionId, string? Notes, int UpdatedByInspectorId);
