using RenoTrack.Application.Common.Interfaces;

namespace RenoTrack.Application.Tests.Fakes;

public sealed class FakeNumberGeneratorService : INumberGeneratorService
{
    public string NextAngebotNumber { get; set; } = "ANG-2026-00001";
    public string NextInvoiceNumber { get; set; } = "RE-2026-00001";
    public List<int> RequestedYears { get; } = [];

    /// <summary>
    /// How many numbers have been handed out. A reservation is irreversible in production — the
    /// sequence only ever increments (D52) — so a handler that reserves one and then rejects the
    /// request has burned it. Tests assert this stays at zero on every rejection path (D66).
    /// </summary>
    public int ReservationCount { get; private set; }

    public Task<string> NextAngebotNumberAsync(int year, CancellationToken cancellationToken)
    {
        RequestedYears.Add(year);
        ReservationCount++;
        return Task.FromResult(NextAngebotNumber);
    }

    public Task<string> NextInvoiceNumberAsync(int year, CancellationToken cancellationToken)
    {
        RequestedYears.Add(year);
        ReservationCount++;
        return Task.FromResult(NextInvoiceNumber);
    }
}
