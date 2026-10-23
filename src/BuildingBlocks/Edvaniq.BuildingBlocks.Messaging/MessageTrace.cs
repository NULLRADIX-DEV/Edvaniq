using System.Diagnostics;

namespace Edvaniq.BuildingBlocks.Messaging;

// Carries a request's trace over the broker: the outbox stores the traceparent, the consumer continues it. So a
// consumer's log lines carry the TraceId of the request behind the event.
public static class MessageTrace
{
    // ServiceDefaults has OpenTelemetry listen to it.
    public const string SourceName = "Edvaniq.Messaging";

    public const string Header = "traceparent";

    private static readonly ActivitySource Source = new(SourceName);

    // null outside an activity
    internal static string? Current() =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;

    // No valid traceparent means a trace of its own. Made even without a listener, the log lines need its TraceId.
    internal static Activity StartConsumer(string consumer, string? traceParent)
    {
        var name = $"{consumer} process";
        ActivityContext.TryParse(traceParent, null, isRemote: true, out var parent);
        if (Source.StartActivity(name, ActivityKind.Consumer, parent) is { } activity)
        {
            return activity;
        }

        var unheard = new Activity(name);
        if (parent != default)
        {
            unheard.SetParentId(parent.TraceId, parent.SpanId, parent.TraceFlags);
        }

        return unheard.Start();
    }
}