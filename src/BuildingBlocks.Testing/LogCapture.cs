using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Testing;

/// <summary>
/// Captures every log entry that reaches this provider, so a test can assert that something was
/// — or, just as often, was deliberately NOT — logged, rather than only that a call did not
/// throw.
/// </summary>
/// <remarks>
/// Register it after <c>ClearProviders()</c> so it is the only provider on the host under test:
/// <c>builder.Logging.ClearProviders(); builder.Logging.AddProvider(logCapture);</c> — then read
/// <see cref="Entries"/>.
///
/// GL-37: promoted here after three services (Identity, GiftLists, Reservations
/// IntegrationTests) each carried a byte-for-byte copy, the third one ported from the second
/// rather than shared. All three always reported <see cref="ILogger.IsEnabled"/> as
/// <c>true</c> and rendered every entry through the caller's own formatter, so nothing here is a
/// behaviour change for any of them — only where the type lives.
///
/// <b>What this does and does not prove.</b> It sees every log call made through the
/// <see cref="ILogger"/> pipeline of the specific host it is attached to, at every
/// <see cref="LogLevel"/> — <see cref="IsEnabled"/> always returns <c>true</c>, so this provider
/// never filters. It does <i>not</i> see anything written outside that pipeline (a raw
/// <c>Console.WriteLine</c>, a different process, another host in the same test run), and it does
/// not override the host's own minimum-level filtering: if a filter rule configured on the
/// <c>ILoggingBuilder</c> excludes a category or level before the log call reaches any provider,
/// this one never receives it either, so a missing entry proves only "not logged to this
/// provider", not "never logged anywhere at any level". For that reason, treat an assertion that
/// something was NOT captured as meaningful only when the test also controls (or has otherwise
/// ruled out) the host's filter configuration.
/// </remarks>
public sealed class LogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

    /// <summary>A snapshot of every entry captured so far. Safe to enumerate while logging continues.</summary>
    public IReadOnlyCollection<(LogLevel Level, string Message)> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<(LogLevel, string)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((logLevel, formatter(state, exception)));
    }
}
