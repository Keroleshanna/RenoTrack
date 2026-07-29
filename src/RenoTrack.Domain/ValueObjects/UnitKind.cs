namespace RenoTrack.Domain.ValueObjects;

/// <summary>
/// The discriminator backing <see cref="ItemUnit"/>. Not used directly outside that type.
/// The five named values are the units explicitly listed in SRS.md FR-4.3
/// ("m², Stk, lfm, pauschal, m, etc.") — note the SRS deliberately leaves this list open
/// ("etc."), unlike LeadStatus/AngebotStatus which have an exhaustive, closed transition
/// table. <see cref="Custom"/> is the escape hatch for anything beyond those five, so the
/// domain stays strongly typed for the common cases without hard-coding an assumption the
/// SRS explicitly does not make.
/// </summary>
public enum UnitKind
{
    /// <summary>m² — square meters.</summary>
    SquareMeter,

    /// <summary>Stk — Stück (piece/item count).</summary>
    Piece,

    /// <summary>lfm — laufende Meter (running/linear meters).</summary>
    LinearMeter,

    /// <summary>pauschal — flat/lump-sum rate.</summary>
    LumpSum,

    /// <summary>m — meters.</summary>
    Meter,

    /// <summary>Anything outside the five standard units above; see <see cref="ItemUnit.CustomLabel"/>.</summary>
    Custom
}
