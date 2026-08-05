namespace RenoTrack.Application.CatalogItems.Commands.SaveAngebotItemAsCatalogItem;

/// <summary>
/// SRS FR-4.10's one-click "Save as Catalog item": promotes a line item the Inspector typed by hand
/// into a reusable Catalog entry, so the Catalog grows from real usage instead of upfront data entry.
/// </summary>
/// <remarks>
/// Separate from <c>CreateCatalogItemCommand</c> rather than an overload of it, because the two are
/// different business actions with different actors: PermissionMatrix.md §6 splits "Create/curate
/// Catalog item directly" (Admin F) from "Add Catalog item via save-as" (Inspector F), and only this
/// one records provenance in <c>CreatedFromAngebotItemId</c>.
/// </remarks>
public sealed record SaveAngebotItemAsCatalogItemCommand(int AngebotItemId, int SavedByInspectorId);
