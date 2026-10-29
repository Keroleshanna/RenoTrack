namespace RenoTrack.Application.Angebote.Commands.RemoveAngebotSection;

/// <summary>
/// Removes a section and its items from an editable Angebot (PermissionMatrix.md §3,
/// "Add/remove Sections &amp; Items — Admin F, Inspector S", D115).
/// </summary>
/// <remarks>
/// <c>RequestingInspectorId</c> is the scope (<c>null</c> for an Admin), <c>PerformedByUserId</c>
/// the actor (D88), and <c>ExpectedVersion</c> arrives as a query parameter because the route is a
/// <c>DELETE</c>.
/// </remarks>
public sealed record RemoveAngebotSectionCommand(
    int AngebotId,
    int SectionId,
    int ExpectedVersion,
    int? RequestingInspectorId,
    int PerformedByUserId);
