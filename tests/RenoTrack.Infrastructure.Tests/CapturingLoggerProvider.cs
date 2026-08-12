using Microsoft.Extensions.Logging;

namespace RenoTrack.Infrastructure.Tests;

/// <summary>
/// One captured log entry. <see cref="Category"/> is the logger's category name — for an
/// <c>ILogger&lt;T&gt;</c> that is <c>typeof(T).FullName</c> — and it is what lets an assertion say
/// "<em>this component</em> logged nothing" rather than "nothing in the process logged anything".
/// </summary>
/// <param name="Exception">
/// The exception passed to <see cref="ILogger.Log{TState}"/>, when there was one. Captured as of
/// Phase 9 Slice 2: the approved failure boundary must attach the original exception so its stack
/// trace survives being swallowed (D59's rule), and "the message mentioned a failure" would not
/// prove that. Optional so every existing construction site and assertion is unaffected.
/// </param>
internal sealed record CapturedLogEntry(string Category, LogLevel Level, string Message, Exception? Exception = null);

/// <summary>
/// Captures log entries so a test can assert on what was reported, for the cases where the log
/// <em>is</em> the behaviour under test — a component that deliberately reports a problem instead of
/// fixing it has no other observable effect to assert on.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written rather than a mocking framework (CLAUDE.md §14). Entries are guarded by a lock
/// because <c>DevelopmentBootstrapTests</c>' concurrency test drives several instances at once
/// through one provider.
/// </para>
/// <para>
/// <b>The category is captured, not discarded.</b> This provider is registered on the real container,
/// and <see cref="CapturingLogger.IsEnabled"/> admits every level, so it sees EF Core's and
/// Identity's logging as well as the component under test. Without the category, a "logged no
/// warning" assertion would in fact assert that <em>no library anywhere</em> logged a warning —
/// coupling a test about one component to the future logging behaviour of the frameworks beneath it.
/// Use <see cref="EntriesFrom{T}"/> to scope an assertion to the component that owns the claim.
/// </para>
/// </remarks>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<CapturedLogEntry> _entries = [];

    /// <summary>A snapshot, so a caller can enumerate it while logging continues.</summary>
    public IReadOnlyList<CapturedLogEntry> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToList();
            }
        }
    }

    /// <summary>
    /// Entries logged through an <c>ILogger&lt;T&gt;</c>, whose category name is
    /// <c>typeof(T).FullName</c>. Encoding that convention here keeps it out of every test that
    /// needs to scope an assertion.
    /// </summary>
    public IReadOnlyList<CapturedLogEntry> EntriesFrom<T>() =>
        Entries.Where(entry => entry.Category == typeof(T).FullName).ToList();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(CapturedLogEntry entry)
    {
        lock (_entries)
        {
            _entries.Add(entry);
        }
    }

    private sealed class CapturingLogger(CapturingLoggerProvider owner, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // Everything is enabled, so a test can assert on Debug-level entries without configuring
        // filters — the production filter level is not what these tests are about.
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            owner.Add(new CapturedLogEntry(categoryName, logLevel, formatter(state, exception), exception));
    }
}
