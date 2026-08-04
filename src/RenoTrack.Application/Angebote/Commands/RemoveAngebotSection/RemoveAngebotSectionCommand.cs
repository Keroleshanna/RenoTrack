namespace RenoTrack.Application.Angebote.Commands.RemoveAngebotSection;

/// <summary>
/// Removes a section and its items from an editable Angebot (PermissionMatrix.md §3,
/// "Add/remove Sections &amp; Items — Inspector S").
/// </summary>
public sealed record RemoveAngebotSectionCommand(int AngebotId, int SectionId, int InspectorId);
