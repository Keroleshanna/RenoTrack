using RenoTrack.Domain.Entities;
using RenoTrack.Domain.Enums;

namespace RenoTrack.Application.Projects.Dtos;

/// <summary>
/// Header-level view only — the shape Sequence Diagram §7 returns from conversion. FR-7.4's
/// "originating Lead, Inspection, Angebot, and all associated Invoices in one place" is the
/// *detail read*, not this; that arrives with `GET /api/v1/projects/{id}` in Slice 4, and its
/// Invoice portion is deferred to Phase 8. Fields are added when a real use case returns them
/// (CLAUDE.md §7), not speculatively.
///
/// <c>AgreedTotal</c> is unwrapped from <see cref="RenoTrack.Domain.ValueObjects.Money"/> to a
/// plain <c>decimal</c>; <see cref="ProjectStatus"/> passes through as-is, since it serializes as
/// a name (D61) and carries no Domain behaviour worth hiding.
/// </summary>
public sealed record ProjectDto(
    int Id,
    int CustomerId,
    int AngebotId,
    ProjectStatus Status,
    decimal AgreedTotal,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public static class ProjectMappingExtensions
{
    public static ProjectDto ToDto(this Project project) => new(
        project.Id,
        project.CustomerId,
        project.AngebotId,
        project.Status,
        project.AgreedTotal.Amount,
        project.CreatedAt,
        project.CompletedAt);
}
