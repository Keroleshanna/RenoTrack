namespace RenoTrack.Domain.Enums;

/// <summary>
/// The closed set of states a Project can be in. Authoritative source: StateMachine.md §4,
/// whose transition table (§4.3) is the only place legal moves are defined. SRS FR-7.2 requires
/// a Project to carry a status; §4.1 names exactly these three and no others.
/// </summary>
public enum ProjectStatus
{
    /// <summary>Work is underway / the Project has just been created. StateMachine.md §4.1.</summary>
    Active,

    /// <summary>Temporarily paused (e.g. waiting on materials or the customer). StateMachine.md §4.1.</summary>
    OnHold,

    /// <summary>Terminal — all work finished and (normally) all invoices paid. StateMachine.md §4.1, FR-7.3.</summary>
    Completed
}
