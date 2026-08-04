namespace RenoTrack.Application.Angebote.Commands.RemoveAngebotItem;

/// <summary>
/// Removes a single item, leaving its section in place (PermissionMatrix.md §3).
/// </summary>
/// <remarks>
/// Carries no <c>SectionId</c>: the item id is unique within the Angebot, and the route
/// (<c>DELETE /angebote/{id}/items/{itemId}</c>) reflects that. The handler resolves the owning
/// section from the loaded tree, which is what the Domain method requires.
/// </remarks>
public sealed record RemoveAngebotItemCommand(int AngebotId, int ItemId, int InspectorId);
