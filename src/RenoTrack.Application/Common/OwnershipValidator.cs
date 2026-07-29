using RenoTrack.Application.Common.Exceptions;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Common;

/// <summary>
/// Implemented directly in RenoTrack.Application, not Infrastructure — unlike every other
/// service interface here (IFileStorage, IEmailSender, IAuditService), this one has no
/// external dependency (no EF Core, no disk, no network) to justify an Infrastructure-side
/// implementation or a future swappable alternative. It exists as an interface so handlers
/// depend on <see cref="IOwnershipValidator"/> (consistent DI registration, and so handler
/// tests can substitute a fake if ever useful), not because a second implementation is
/// expected.
/// </summary>
public sealed class OwnershipValidator : IOwnershipValidator
{
    public void EnsureInspectionOwnership(Inspection inspection, int inspectorId)
    {
        if (inspection.InspectorId != inspectorId)
        {
            throw new ForbiddenException($"Inspector {inspectorId} is not assigned to Inspection {inspection.Id}.");
        }
    }
}
