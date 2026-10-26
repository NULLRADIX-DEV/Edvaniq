using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Testing;

// What a process logs in a test, each entry with the TraceId its console line would show. Text holds everything an
// entry carries, the message with its values, the exception and the scopes, so a test can prove a value never gets
// into the log, whatever the log on the server shows of an entry.
public sealed class TestLogs : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<TestLogEntry> entries = new();
    private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();

    public IReadOnlyList<TestLogEntry> Entries => [.. entries];

    // The entries that contain one of the values anywhere.
    public IReadOnlyList<TestLogEntry> Containing(params string[] values) =>
        [.. entries.Where(entry => values.Any(value => entry.Text.Contains(value, StringComparison.Ordinal)))];

    public ILogger CreateLogger(string categoryName) => new Collector(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;

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
                var message = formatter(state, exception);
                var text = new StringBuilder(message);
                Append(text, state);
                text.Append(' ').Append(exception);
                owner.scopes.ForEachScope((scope, builder) => Append(builder, scope), text);
                owner.entries.Enqueue(new TestLogEntry(category, logLevel, message, Activity.Current?.TraceId.ToHexString(), text.ToString()));
            }
        }

        // A state or scope with values adds each of them, an exporter could send them on their own.
        private static void Append(StringBuilder text, object? value)
        {
            text.Append(' ').Append(value);
            if (value is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var (key, item) in pairs)
                {
                    text.Append(' ').Append(key).Append('=').Append(item);
                }
            }
        }
    }
}

public sealed record TestLogEntry(string Category, LogLevel Level, string Message, string? TraceId, string Text);