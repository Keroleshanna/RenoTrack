using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Tests.Fakes;

public sealed class FakeAngebotRepository : IAngebotRepository
{
    public List<Angebot> AddedAngebote { get; } = [];
    public bool HasActiveAngebotForLead { get; set; }

    public Task AddAsync(Angebot angebot, CancellationToken cancellationToken)
    {
        AddedAngebote.Add(angebot);
        return Task.CompletedTask;
    }

    public Task<bool> HasActiveAngebotForLeadAsync(int leadId, CancellationToken cancellationToken) =>
        Task.FromResult(HasActiveAngebotForLead);
}
