using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// Write-side repository for the Lead aggregate (Architecture.md §5.1's read/write split).
/// </summary>
public interface ILeadRepository
{
    Task AddAsync(Lead lead, CancellationToken cancellationToken);

    /// <summary>Added for ScheduleInspectionCommand — the first use case that needs to load an existing Lead.</summary>
    Task<Lead?> GetByIdAsync(int id, CancellationToken cancellationToken);
}
