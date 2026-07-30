namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// Architecture.md §8. The Infrastructure implementation (Phase 3) must perform the increment
/// atomically within the same transaction as the entity being numbered, so concurrent requests
/// can never produce duplicate numbers — this interface stays simple, but that guarantee is a
/// hard requirement of whatever implements it.
/// </summary>
public interface INumberGeneratorService
{
    /// <summary>Returns a formatted, unique Angebot number for the given year, e.g. "ANG-2026-00042".</summary>
    Task<string> NextAngebotNumberAsync(int year, CancellationToken cancellationToken);
}
