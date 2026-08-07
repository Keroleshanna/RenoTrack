using RenoTrack.Application.Projects.Dtos;

namespace RenoTrack.Application.Projects;

/// <summary>
/// Read-side access to Projects, bypassing aggregate hydration (D36). Grows on demand, exactly like
/// every repository interface in this project (CLAUDE.md §4) — one method today, because one
/// endpoint needs one shape.
///
/// <para>
/// Lives here rather than in <c>Common/Interfaces</c> because its return type is a feature DTO, so
/// <c>Common</c> would otherwise depend on a feature folder — the same reasoning as
/// <c>ILeadQueries</c> and <c>IAngebotQueries</c> (D23).
/// </para>
/// </summary>
public interface IProjectQueries
{
    /// <summary>
    /// One Project with the originating context Wireframe E1 renders, or <see langword="null"/> if
    /// no such Project exists.
    ///
    /// <para>
    /// <b>No scope parameter, deliberately.</b> `PermissionMatrix.md` §5 marks "View Project
    /// detail" Admin <c>F</c> / Inspector <c>R</c> — read-only but *unscoped*, unlike a Lead's
    /// <c>S</c>. Adding a <c>requestingInspectorId</c> here would invent a per-Inspector
    /// restriction the documents do not state, and the note against that row explains why it does
    /// not exist: an Inspector may look at the outcome of a Lead they worked.
    /// </para>
    /// </summary>
    Task<ProjectDetailDto?> GetByIdAsync(int id, CancellationToken cancellationToken);
}
