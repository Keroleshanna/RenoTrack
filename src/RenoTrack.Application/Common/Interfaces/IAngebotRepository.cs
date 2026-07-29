using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Common.Interfaces;

/// <summary>Write-side repository for the Angebot aggregate.</summary>
public interface IAngebotRepository
{
    Task AddAsync(Angebot angebot, CancellationToken cancellationToken);

    /// <summary>
    /// StateMachine.md §2.4: "Only one Angebot per Lead may be in a non-terminal state
    /// (Draft/InReview/ChangesRequested/ApprovedInternally/Sent) at a time." A purpose-built
    /// query rather than a generic "get Angebote by Lead" — this is the only shape
    /// CreateAngebotCommand needs, and only Angebot itself knows what "non-terminal" means, so
    /// the Infrastructure implementation filters by every AngebotStatus except
    /// CustomerApproved/CustomerRejected.
    /// </summary>
    Task<bool> HasActiveAngebotForLeadAsync(int leadId, CancellationToken cancellationToken);
}
