using Microsoft.Extensions.Logging;

namespace RenoTrack.Website.Tests;

/// <summary>
/// One captured log entry, with every part of it a log sink could write down.
/// </summary>
/// <param name="Category">The logger's category name.</param>
/// <param name="Level">The level the entry was written at.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Exception">The attached exception rendered with <c>ToString()</c>, when there was one.</param>
/// <param name="StateValues">
/// The structured values behind the message template. A structured sink stores these as fields
/// whether or not they appear in the formatted text, so a credential hidden here still leaks.
/// </param>
/// <param name="Scopes">
/// The ambient scopes active when the entry was written, each rendered as text. Recorded rather than
/// asserted by default — see <see cref="CapturingLoggerProvider"/>.
/// </param>
internal sealed record CapturedLogEntry(
    string Category,
    LogLevel Level,
    string Message,
    string? Exception,
    IReadOnlyList<string> StateValues,
    IReadOnlyList<string> Scopes)
{
    /// <summary>
    /// Message, exception and structured values — the parts every sink records regardless of how it
    /// is configured.
    /// </summary>
    public bool RecordedPartsContain(string value) =>
        Message.Contains(value, StringComparison.Ordinal)
        || (Exception?.Contains(value, StringComparison.Ordinal) ?? false)
        || StateValues.Any(text => text.Contains(value, StringComparison.Ordinal));

    public bool ScopesContain(string value) =>
        Scopes.Any(text => text.Contains(value, StringComparison.Ordinal));

    /// <summary>True when any active scope carries a value under <paramref name="key"/>.</summary>
    public bool HasScopeKey(string key) => ScopeValue(key) is not null;

    /// <summary>The value of <paramref name="key"/> in the innermost scope that carries it, if any.</summary>
    public string? ScopeValue(string key)
    {
        foreach (var scope in Scopes.Reverse())
        {
            foreach (var pair in scope.Split(", "))
            {
                if (pair.StartsWith($"{key}=", StringComparison.Ordinal))
                {
                    return pair[(key.Length + 1)..];
                }
            }
        }

        return null;
    }

    public override string ToString() =>
        $"[{Level}] {Category}: {Message}" +
        (Exception is null ? string.Empty : $" | exception: {Exception}") +
        (StateValues.Count == 0 ? string.Empty : $" | state: {string.Join(", ", StateValues)}") +
        (Scopes.Count == 0 ? string.Empty : $" | scopes: {string.Join(" => ", Scopes)}");
}

/// <summary>
/// Captures log entries from a real host, for tests whose claim is about what reaches a log sink.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written rather than a mocking framework (<c>CLAUDE.md</c> §14), and guarded by a lock because
/// the host logs from whatever thread serves the request.
/// </para>
/// <para>
/// <b>It deliberately does not filter.</b> <see cref="ILogger.IsEnabled"/> admits every level, so what
/// arrives here is decided by the host's own logging configuration, the same rules that decide what
/// reaches a production sink. A provider that filtered for itself would test its own filter, not
/// the application's.
/// </para>
/// <para>
/// It implements <see cref="ISupportExternalScope"/>, so it sees the ambient scopes a sink with
/// <c>IncludeScopes</c> would write, not only the message.
/// </para>
/// </remarks>
internal sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly List<CapturedLogEntry> _entries = [];
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    /// <summary>A snapshot, so a caller can enumerate it while the host keeps logging.</summary>
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

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

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
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var stateValues = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.Select(pair => $"{pair.Key}={pair.Value}").ToList()
                : [];

            var scopes = new List<string>();
            owner._scopes.ForEachScope(
                (scope, list) =>
                {
                    list.Add(scope is IEnumerable<KeyValuePair<string, object?>> scopePairs
                        ? string.Join(", ", scopePairs.Select(pair => $"{pair.Key}={pair.Value}"))
                        : scope?.ToString() ?? string.Empty);
                },
                scopes);

            owner.Add(new CapturedLogEntry(
                categoryName,
                logLevel,
                formatter(state, exception),
                exception?.ToString(),
                stateValues,
                scopes));
        }
    }
}
