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
}
