using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// Write-side repository for the Lead aggregate (Architecture.md §5.1's read/write split).
/// Deliberately starts with only what <c>CreateLeadCommand</c> needs — <c>GetByIdAsync</c> is
/// added when the first command that needs it (e.g. ScheduleInspectionCommand) is built,
/// rather than speculatively now.
/// </summary>
public interface ILeadRepository
{
    Task AddAsync(Lead lead, CancellationToken cancellationToken);
}
