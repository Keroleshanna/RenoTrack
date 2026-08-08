using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Tests.Fakes;

public sealed class FakeProjectRepository : IProjectRepository
{
    private readonly HashSet<int> _convertedAngebotIds = [];

    public List<Project> AddedProjects { get; } = [];

    public Task AddAsync(Project project, CancellationToken cancellationToken)
    {
        AddedProjects.Add(project);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsForAngebotAsync(int angebotId, CancellationToken cancellationToken) =>
        Task.FromResult(_convertedAngebotIds.Contains(angebotId));

    /// <summary>Marks an Angebot as already converted, without needing a persisted Project.</summary>
    public void SeedConverted(int angebotId) => _convertedAngebotIds.Add(angebotId);

    private readonly Dictionary<int, Project> _projects = [];

    public Task<Project?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(_projects.GetValueOrDefault(id));

    /// <summary>
    /// Simulates database-assigned identity, as every other fake repository's <c>Seed</c> does —
    /// reflection is sanctioned in test infrastructure only (CLAUDE.md §14).
    /// </summary>
    public Project Seed(Project project, int id)
    {
        typeof(Project).GetProperty(nameof(Project.Id))!.SetValue(project, id);
        _projects[id] = project;
        return project;
    }
}
