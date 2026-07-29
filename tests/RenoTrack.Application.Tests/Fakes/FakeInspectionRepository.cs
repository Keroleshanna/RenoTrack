using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Tests.Fakes;

public sealed class FakeInspectionRepository : IInspectionRepository
{
    public List<Inspection> AddedInspections { get; } = [];

    public Task AddAsync(Inspection inspection, CancellationToken cancellationToken)
    {
        AddedInspections.Add(inspection);
        return Task.CompletedTask;
    }
}
