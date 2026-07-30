namespace RenoTrack.Application.Inspections.Commands.CompleteInspection;

public sealed record CompleteInspectionCommand(int InspectionId, int CompletedByInspectorId);
