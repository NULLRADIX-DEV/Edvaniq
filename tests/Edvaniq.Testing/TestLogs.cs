using System.Collections.Concurrent;
using System.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Testing;

// Collects what a service logs in a test, each entry with the TraceId of the activity it was written in. The console log
// of a service shows the same TraceId in its scopes.
public sealed class TestLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<TestLogEntry> entries = new();

    public IReadOnlyList<TestLogEntry> Entries => [.. entries];

    public ILogger CreateLogger(string categoryName) => new Collector(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Collector(TestLogs owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                owner.entries.Enqueue(new TestLogEntry(category, logLevel, formatter(state, exception), Activity.Current?.TraceId.ToHexString()));
            }
        }
    }
}

public sealed record TestLogEntry(string Category, LogLevel Level, string Message, string? TraceId);