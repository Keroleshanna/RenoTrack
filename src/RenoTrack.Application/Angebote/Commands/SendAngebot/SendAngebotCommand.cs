namespace RenoTrack.Application.Angebote.Commands.SendAngebot;

/// <summary>
/// SRS FR-6.1 / Sequence Diagram §6 / StateMachine.md §2.3 (<c>ApprovedInternally → Sent</c>).
/// Both values are server-derived (D61): the Angebot id comes from the route, and the Admin id
/// from the JWT's <c>sub</c> claim — it describes who is acting, so it is never request input.
/// There is nothing left for a caller to supply, which is why no request record accompanies this
/// endpoint.
/// </summary>
public sealed record SendAngebotCommand(int AngebotId, int SentByAdminId);
