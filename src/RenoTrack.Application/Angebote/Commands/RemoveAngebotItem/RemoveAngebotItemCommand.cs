namespace RenoTrack.Application.Angebote.Commands.RemoveAngebotItem;

/// <summary>
/// Removes a single item, leaving its section in place (PermissionMatrix.md §3).
/// </summary>
/// <remarks>
/// Carries no <c>SectionId</c>: the item id is unique within the Angebot, and the route
/// (<c>DELETE /angebote/{id}/items/{itemId}</c>) reflects that. The handler resolves the owning
/// section from the loaded tree, which is what the Domain method requires.
///
/// <para>
/// Owning Inspector or any Admin (D115): <c>RequestingInspectorId</c> is the scope (<c>null</c> for
/// an Admin), <c>PerformedByUserId</c> the actor (D88), and <c>ExpectedVersion</c> arrives as a
/// query parameter because the route is a <c>DELETE</c>.
/// </para>
/// </remarks>
public sealed record RemoveAngebotItemCommand(
    int AngebotId,
    int ItemId,
    int ExpectedVersion,
    int? RequestingInspectorId,
    int PerformedByUserId);
