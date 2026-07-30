namespace RenoTrack.Application.CatalogItems.Commands.RetireCatalogItem;

/// <summary>
/// PermissionMatrix.md §6: Admin-only. BR-12: retires (IsRetired = true), never deletes.
/// CatalogItem.Retire() is idempotent and parameterless — no other fields to carry.
/// </summary>
public sealed record RetireCatalogItemCommand(int Id, int RetiredByAdminUserId);
