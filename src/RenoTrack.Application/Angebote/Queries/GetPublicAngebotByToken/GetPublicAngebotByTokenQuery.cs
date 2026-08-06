namespace RenoTrack.Application.Angebote.Queries.GetPublicAngebotByToken;

/// <summary>
/// SRS FR-6.2 / Sequence Diagram §6. The token is the entire request: no user identity is ever
/// established for a customer (Architecture.md §7.2), so there is deliberately no caller id here —
/// the one query in this codebase with nothing server-derived to add, because there is no server-
/// known caller at all.
/// </summary>
public sealed record GetPublicAngebotByTokenQuery(string Token);
