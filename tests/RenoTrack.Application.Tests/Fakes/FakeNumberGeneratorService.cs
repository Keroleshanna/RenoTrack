using RenoTrack.Application.Common.Interfaces;

namespace RenoTrack.Application.Tests.Fakes;

public sealed class FakeNumberGeneratorService : INumberGeneratorService
{
    public string NextAngebotNumber { get; set; } = "ANG-2026-00001";
    public List<int> RequestedYears { get; } = [];

    public Task<string> NextAngebotNumberAsync(int year, CancellationToken cancellationToken)
    {
        RequestedYears.Add(year);
        return Task.FromResult(NextAngebotNumber);
    }
}
