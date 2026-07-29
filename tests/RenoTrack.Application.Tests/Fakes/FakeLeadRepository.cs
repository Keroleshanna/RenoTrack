using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Tests.Fakes;

/// <summary>In-memory fake — no database, per Architecture.md §5.1's testable-in-isolation goal for the Application layer.</summary>
public sealed class FakeLeadRepository : ILeadRepository
{
    public List<Lead> AddedLeads { get; } = [];

    public Task AddAsync(Lead lead, CancellationToken cancellationToken)
    {
        AddedLeads.Add(lead);
        return Task.CompletedTask;
    }
}
