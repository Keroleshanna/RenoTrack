using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Common.Interfaces;

/// <summary>Write-side repository for the Inspection aggregate. Starts minimal — AddAsync only, matching ILeadRepository's original shape.</summary>
public interface IInspectionRepository
{
    Task AddAsync(Inspection inspection, CancellationToken cancellationToken);
}
