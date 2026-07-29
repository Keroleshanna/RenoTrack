using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Common.Interfaces;

/// <summary>Write-side repository for the Inspection aggregate.</summary>
public interface IInspectionRepository
{
    Task AddAsync(Inspection inspection, CancellationToken cancellationToken);

    /// <summary>Added for CompleteInspectionCommand — the first use case that needs to load an existing Inspection.</summary>
    Task<Inspection?> GetByIdAsync(int id, CancellationToken cancellationToken);
}
