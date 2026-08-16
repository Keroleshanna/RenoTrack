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

    /// <summary>
    /// BR-3's running total for one Project — agreed, already invoiced, and the difference — or
    /// <see langword="null"/> if no such Project exists. Sequence Diagram §8.
    ///
    /// <para>
    /// <b><c>AlreadyInvoiced</c> excludes <c>Void</c> invoices and nothing else.</b>
    /// StateMachine.md §3.3's side-effect column says a voided invoice is "excluded from
    /// 'remaining balance' math going forward"; no document excludes any other status, so a
    /// <c>Draft</c> invoice counts exactly as a <c>Paid</c> one does.
    /// </para>
    /// <para>
    /// <b>No scope parameter</b>, for the same reason <see cref="GetByIdAsync"/> has none: §5
    /// grants Inspectors <c>R</c> on Project data — read-only but unscoped.
    /// </para>
    /// </summary>
    Task<ProjectInvoiceBalanceDto?> GetInvoiceBalanceAsync(int projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Projects, newest first, optionally filtered by status — the list the Projekte workspace and
    /// the Cockpit need. Before Phase 10 a Project was reachable only by id.
    /// </summary>
    /// <remarks>
    /// <b>No scope parameter:</b> PermissionMatrix.md §5 grants Project reads to both roles
    /// unscoped, so there is nothing to filter by. See <c>GetProjectsQueryHandler</c>.
    /// </remarks>
    Task<Common.PagedResult<ProjectListItemDto>> GetPagedAsync(
        Domain.Enums.ProjectStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
