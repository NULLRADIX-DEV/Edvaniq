using System.Collections.Concurrent;
using System.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Testing;

// What a service logs in a test, each entry with the TraceId its console line would show.
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