namespace RenoTrack.Application.Projects.Commands.ConvertAngebotToProject;

/// <summary>
/// SRS FR-7.1 / Sequence Diagram §7. <c>AngebotId</c> comes from the route and
/// <c>PerformedByAdminId</c> from the authenticated principal (D61) — neither is request-body
/// input, so the API's request contract for this endpoint is empty.
/// </summary>
public sealed record ConvertAngebotToProjectCommand(int AngebotId, int PerformedByAdminId);
