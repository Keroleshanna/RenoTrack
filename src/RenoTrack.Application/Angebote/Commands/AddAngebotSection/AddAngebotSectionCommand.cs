namespace RenoTrack.Application.Angebote.Commands.AddAngebotSection;

public sealed record AddAngebotSectionCommand(int AngebotId, string Title, int SortOrder, int InspectorId);
