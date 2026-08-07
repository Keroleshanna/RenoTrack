namespace RenoTrack.Domain.Enums;

/// <summary>
/// Which kind of entity a <see cref="Entities.TokenLink"/> points at. ERD.md documents the column
/// as "Angebot | Invoice", and Architecture.md §7.2 states the deliberate design intent: one
/// polymorphic table serves both, with no database-level FK, so a leaked link's blast radius stays
/// limited to that one entity.
///
/// <see cref="Invoice"/> is declared here from the start even though nothing produces it until
/// Phase 8, matching how <see cref="AngebotStatus"/> and <see cref="LeadSource"/> were both
/// declared complete in Phase 1 with several values unreachable for phases afterwards. The
/// alternative — a single-valued enum today — would make the type misrepresent the documented
/// domain of the column it maps to, and the entity-type check that guards every public endpoint
/// (Sequence Diagram §12) is only meaningful once more than one value can exist.
/// </summary>
public enum TokenLinkEntityType
{
    Angebot = 1,
    Invoice = 2,
}
