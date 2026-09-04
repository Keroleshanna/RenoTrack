using RenoTrack.Domain.Enums;
using RenoTrack.Application.Common;
using RenoTrack.Application.Projects;
using RenoTrack.Application.Projects.Dtos;

namespace RenoTrack.Application.Tests.Fakes;

/// <summary>
/// Promoted out of <c>GetProjectByIdQueryHandlerTests</c> in Phase 8 Slice 3, when a second handler
/// (<c>GetProjectInvoiceBalanceQueryHandler</c>) needed the same interface — the same
/// third-occurrence discipline D28 applied to <c>IOwnershipValidator</c>, one interface earlier.
/// </summary>
public sealed class FakeProjectQueries : IProjectQueries
{
    private readonly Dictionary<int, ProjectDetailDto> _projects = [];
    private readonly Dictionary<int, ProjectInvoiceBalanceDto> _balances = [];

    public void Seed(ProjectDetailDto project) => _projects[project.Id] = project;

    public void Seed(ProjectInvoiceBalanceDto balance) => _balances[balance.ProjectId] = balance;

    public Task<ProjectDetailDto?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(_projects.GetValueOrDefault(id));

    public Task<ProjectInvoiceBalanceDto?> GetInvoiceBalanceAsync(int projectId, CancellationToken cancellationToken) =>
        Task.FromResult(_balances.GetValueOrDefault(projectId));
/// <summary>Records the list call's filter and paging, and returns a canned page.</summary>
    public List<(ProjectStatus? Status, int Page, int PageSize)> PagedCalls { get; } = [];

    public PagedResult<ProjectListItemDto> PagedResult { get; set; } = new([], 1, 25, 0);

    public Task<PagedResult<ProjectListItemDto>> GetPagedAsync(
        ProjectStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        PagedCalls.Add((status, page, pageSize));
        return Task.FromResult(PagedResult);
    }
}
