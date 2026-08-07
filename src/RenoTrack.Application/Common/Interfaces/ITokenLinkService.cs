namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// A freshly generated token and the expiry it should carry — the two values a caller needs in
/// order to construct a <c>TokenLink</c>, and nothing else.
/// </summary>
public sealed record GeneratedToken(string Token, DateTime ExpiresAt);

/// <summary>
/// Produces the cryptographically random token Architecture.md §7.2 requires ("32-byte, URL-safe
/// base64", explicitly "not derived from predictable data") together with the configured lifetime
/// SRS FR-6.4 requires to be configurable.
///
/// Shaped as a value provider rather than as a factory returning a <c>TokenLink</c>, matching
/// <see cref="INumberGeneratorService"/>: Infrastructure supplies a value the Domain could not
/// produce itself (randomness there, a database sequence here, both plus configuration), and the
/// handler still constructs the aggregate through its own factory, keeping step 4 of CLAUDE.md
/// §6's canonical handler shape visible at the call site.
///
/// Sequence Diagram §6 draws this as <c>GenerateToken(entityType, entityId, expiresIn)</c>. Those
/// three arguments are absent here because none of them affects the value produced: the token is
/// entity-agnostic random bytes, and the lifetime is configuration rather than a caller's choice.
/// They are supplied to <c>TokenLink.Create</c> instead, so the same data still ends up on the
/// same row — the diagram describes what happens, not which parameter sits on which method.
///
/// Synchronous because it performs no I/O, unlike <see cref="INumberGeneratorService"/>, which
/// must reach the database to reserve a number.
/// </summary>
public interface ITokenLinkService
{
    GeneratedToken Generate();
}
