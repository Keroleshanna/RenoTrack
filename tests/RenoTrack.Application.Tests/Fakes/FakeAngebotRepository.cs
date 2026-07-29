using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Tests.Fakes;

public sealed class FakeAngebotRepository : IAngebotRepository
{
    private readonly Dictionary<int, Angebot> _angebote = [];
    private int _nextId = 1;

    public List<Angebot> AddedAngebote { get; } = [];
    public bool HasActiveAngebotForLead { get; set; }

    public Task AddAsync(Angebot angebot, CancellationToken cancellationToken)
    {
        AddedAngebote.Add(angebot);
        return Task.CompletedTask;
    }

    public Task<Angebot?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(_angebote.GetValueOrDefault(id));

    public Task<bool> HasActiveAngebotForLeadAsync(int leadId, CancellationToken cancellationToken) =>
        Task.FromResult(HasActiveAngebotForLead);

    /// <summary>Test-only seam — see FakeLeadRepository.Seed for the same rationale.</summary>
    public Angebot Seed(Angebot angebot)
    {
        var id = _nextId++;
        typeof(Angebot).GetProperty(nameof(Angebot.Id))!.SetValue(angebot, id);
        _angebote[id] = angebot;
        return angebot;
    }
}
