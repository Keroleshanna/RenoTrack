namespace RenoTrack.Infrastructure.Persistence.Entities;

/// <summary>
/// Infrastructure-only persistence model — deliberately not a Domain entity (D51), same
/// reasoning as AuditLog (D49): a technical counter with no business invariant, never read or
/// mutated through a Domain rule. Rows are written exclusively via NumberGeneratorService's raw
/// SQL (D52) — EF Core's change tracker never loads or updates this entity through the normal
/// LINQ surface, so this class exists mainly to give the table a DbSet/model presence for
/// migrations, not as a mutation path.
/// </summary>
public sealed class NumberSequence
{
    public int Id { get; private set; }
    public string SequenceType { get; private set; } = null!;
    public int Year { get; private set; }
    public int LastValue { get; private set; }
}
