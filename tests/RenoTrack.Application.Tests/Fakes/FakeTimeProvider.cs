namespace RenoTrack.Application.Tests.Fakes;

/// <summary>
/// A clock that reads whatever instant the test sets — a hand-written subclass of the BCL's
/// <see cref="TimeProvider"/>, never a mocking framework (CLAUDE.md §14). It is what lets a test put
/// the invoice handler at 23:30 UTC on New Year's Eve (D111 Part 6).
/// </summary>
public sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    /// <summary>How many times the code under test asked for the time.</summary>
    public int ReadCount { get; private set; }

    public override DateTimeOffset GetUtcNow()
    {
        ReadCount++;
        return UtcNow;
    }
}
