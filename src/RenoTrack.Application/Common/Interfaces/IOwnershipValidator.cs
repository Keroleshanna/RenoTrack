using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// Architecture.md §7.3: resource-ownership rules are Application-layer business invariants,
/// not authorization attributes. Each method here names one specific business relationship
/// (e.g. "this Inspector is the one this Inspection is assigned to") rather than exposing a
/// single generic id-equality check — a call site should read as business intent, not as
/// integer comparison. New entities that gain an ownership rule get their own named method
/// here (e.g. a future <c>EnsureAngebotOwnership</c>), even though the underlying check may be
/// identical in shape to an existing one.
/// </summary>
public interface IOwnershipValidator
{
    /// <exception cref="Exceptions.ForbiddenException">Thrown if <paramref name="inspectorId"/> is not the Inspector this Inspection is assigned to.</exception>
    void EnsureInspectionOwnership(Inspection inspection, int inspectorId);
}
