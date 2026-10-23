using System.Diagnostics;

namespace Edvaniq.BuildingBlocks.Messaging;

// Carries the trace of a request over the broker (docs/architecture.md#nachverfolgung). The outbox stores the traceparent
// of the activity that stored the event, the transport sends it in the header traceparent, and the consumer handles the
// message in an activity below it. So the log lines of every service that handles the event carry the TraceId of the
// request that caused it.
public static class MessageTrace
{
    // The activities of the consumers. ServiceDefaults has OpenTelemetry listen to it.
    public const string SourceName = "Edvaniq.Messaging";

    public const string Header = "traceparent";

    private static readonly ActivitySource Source = new(SourceName);

    // The traceparent of the current activity, null outside of one.
    internal static string? Current() =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;

    // The activity in which a consumer handles a message, below the trace the sender stored. A message without a valid
    // traceparent starts a trace of its own. Without a listener the activity is made anyway, the log lines take their
    // TraceId from it.
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