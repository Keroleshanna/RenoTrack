using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Tests.Fakes;

public sealed class FakeInspectionRepository : IInspectionRepository
{
    private readonly Dictionary<int, Inspection> _inspections = [];
    private int _nextId = 1;

    public List<Inspection> AddedInspections { get; } = [];

    public Task AddAsync(Inspection inspection, CancellationToken cancellationToken)
    {
        AddedInspections.Add(inspection);
        return Task.CompletedTask;
    }

    public Task<Inspection?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(_inspections.GetValueOrDefault(id));

    /// <summary>Test-only seam — see FakeLeadRepository.Seed for the same rationale.</summary>
    public Inspection Seed(Inspection inspection)
    {
        var id = _nextId++;
        typeof(Inspection).GetProperty(nameof(Inspection.Id))!.SetValue(inspection, id);
        _inspections[id] = inspection;
        return inspection;
    }
}
