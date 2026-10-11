using Microsoft.Extensions.Logging;

namespace RenoTrack.Application.Tests.Fakes;

/// <summary>One entry a <see cref="CapturingLogger{T}"/> received, already formatted.</summary>
public sealed record CapturedLogEntry(LogLevel Level, string Message, Exception? Exception);

/// <summary>
/// Hand-written logger fake (CLAUDE.md §14) for asserting what a handler writes to its log — and,
/// as importantly, what it never writes, such as a storage key (D113).
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<CapturedLogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add(new CapturedLogEntry(logLevel, formatter(state, exception), exception));
}
