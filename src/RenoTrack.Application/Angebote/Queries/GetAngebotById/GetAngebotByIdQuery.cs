namespace RenoTrack.Application.Angebote.Queries.GetAngebotById;

/// <summary>
/// The full Angebot tree, for the builder screen and the Admin review screen (Wireframes D1–D3).
/// </summary>
/// <param name="RequestingInspectorId">
/// The Inspector whose ownership must hold, or <see langword="null"/> for an Admin, who has full
/// access to any Angebot (PermissionMatrix.md §4).
/// </param>
public sealed record GetAngebotByIdQuery(int AngebotId, int? RequestingInspectorId);
