using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Tests.Fakes;

/// <summary>In-memory fake — no database, per Architecture.md §5.1's testable-in-isolation goal for the Application layer.</summary>
public sealed class FakeLeadRepository : ILeadRepository
{
    private readonly Dictionary<int, Lead> _leads = [];
    private int _nextId = 1;

    public List<Lead> AddedLeads { get; } = [];

    public Task AddAsync(Lead lead, CancellationToken cancellationToken)
    {
        AddedLeads.Add(lead);
        return Task.CompletedTask;
    }

    public Task<Lead?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(_leads.GetValueOrDefault(id));

    /// <summary>
    /// Test-only seam simulating a Lead that already exists in the database with a real,
    /// assigned Id — normally EF Core's job (Phase 3), not yet available. Uses reflection
    /// purely because Lead.Id has no public setter by design; this stays inside test code and
    /// never touches production behavior.
    /// </summary>
    public Lead Seed(Lead lead)
    {
        var id = _nextId++;
        typeof(Lead).GetProperty(nameof(Lead.Id))!.SetValue(lead, id);
        _leads[id] = lead;
        return lead;
    }
}
