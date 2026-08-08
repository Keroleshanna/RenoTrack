using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Common.Interfaces;

namespace RenoTrack.Infrastructure.Persistence;

/// <summary>
/// D52: a deliberate, narrowly-scoped exception to this project's otherwise EF-Core-only
/// Infrastructure convention. EF Core's read/track/write model cannot express "atomically
/// increment this row and return the new value" as one database round trip — even a
/// load-then-increment-then-SaveChanges sequence is two round trips with an in-memory gap
/// between them, which is exactly the race window this method must not have. A single
/// `UPDATE ... OUTPUT` statement, executed with no ambient/explicit EF transaction open, runs as
/// its own SQL Server auto-commit unit: the row-level exclusive lock it takes is held only for
/// that one statement, not across any of the rest of CreateAngebotCommandHandler's work.
///
/// This increment is NOT inside the same transaction as the Angebot's own SaveChangesAsync —
/// that is not achievable given CreateAngebotCommandHandler's call order (the Angebot doesn't
/// exist yet when this runs), and it isn't necessary either: uniqueness is fully guaranteed by
/// this statement alone. Gaps in Angebot numbering are acceptable (no BusinessRules.md rule
/// forbids them, unlike Invoice numbers/BR-9) — if the rest of the command later fails, this
/// already-reserved number is simply never reused.
/// </summary>
public sealed class NumberGeneratorService(RenoTrackDbContext dbContext) : INumberGeneratorService
{
    // ERD.md documents NumberSequences.SequenceType as exactly "Angebot | Invoice", and the table
    // is keyed on (SequenceType, Year) — so a second document type is a second row, not a second
    // mechanism. Everything below is shared verbatim, including D52's atomicity guarantee.
    private const string AngebotSequenceType = "Angebot";
    private const string InvoiceSequenceType = "Invoice";

    private const string IncrementExistingSql =
        """
        UPDATE NumberSequences
        SET LastValue = LastValue + 1
        OUTPUT INSERTED.LastValue
        WHERE SequenceType = {0} AND Year = {1};
        """;

    private const string InsertFirstSql =
        """
        INSERT INTO NumberSequences (SequenceType, Year, LastValue)
        OUTPUT INSERTED.LastValue
        VALUES ({0}, {1}, 1);
        """;

    public async Task<string> NextAngebotNumberAsync(int year, CancellationToken cancellationToken) =>
        $"ANG-{year}-{await NextValueAsync(AngebotSequenceType, year, cancellationToken):D5}";

    /// <summary>
    /// Architecture.md §8's <c>RE-{YYYY}-{sequence:D5}</c>. Identical machinery to the Angebot
    /// number, on its own sequence row — so BR-9's "never reused" holds for exactly the same reason,
    /// and gaps remain possible for exactly the same reason (D52, D66).
    /// </summary>
    public async Task<string> NextInvoiceNumberAsync(int year, CancellationToken cancellationToken) =>
        $"RE-{year}-{await NextValueAsync(InvoiceSequenceType, year, cancellationToken):D5}";

    private async Task<int> NextValueAsync(string sequenceType, int year, CancellationToken cancellationToken) =>
        await IncrementExistingAsync(sequenceType, year, cancellationToken)
        ?? await InsertFirstOrRetryIncrementAsync(sequenceType, year, cancellationToken);

    private async Task<int?> IncrementExistingAsync(string sequenceType, int year, CancellationToken cancellationToken)
    {
        var results = await dbContext.Database
            .SqlQueryRaw<int>(IncrementExistingSql, sequenceType, year)
            .ToListAsync(cancellationToken);

        return results.Count == 1 ? results[0] : null;
    }

    private async Task<int> InsertFirstOrRetryIncrementAsync(string sequenceType, int year, CancellationToken cancellationToken)
    {
        try
        {
            var inserted = await dbContext.Database
                .SqlQueryRaw<int>(InsertFirstSql, sequenceType, year)
                .ToListAsync(cancellationToken);

            return inserted[0];
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // A concurrent request won the "first of the year" race and inserted the row first —
            // it now exists, so the increment is guaranteed to succeed on retry.
            var retried = await dbContext.Database
                .SqlQueryRaw<int>(IncrementExistingSql, sequenceType, year)
                .ToListAsync(cancellationToken);

            return retried.Single();
        }
    }
}
