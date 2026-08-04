namespace RenoTrack.Application.Angebote.Queries.GetLeadAngebote;

/// <summary>
/// Every Angebot on one Lead — the Lead detail page's Angebot list (Wireframe C1).
/// </summary>
/// <remarks>
/// Deliberately unpaged. StateMachine.md §2.4 permits only one non-terminal Angebot per Lead at a
/// time, so this collection is a short history bounded by how many times a Lead has been quoted —
/// not an unbounded list like the Lead pipeline, which is paged. It is still ordered
/// deterministically, since an unordered list can reorder between reads even when it is small.
/// </remarks>
public sealed record GetLeadAngeboteQuery(int LeadId, int? RequestingInspectorId);
