using System.Diagnostics;

namespace Edvaniq.BuildingBlocks.Messaging.UnitTests;

// The trace of a request goes with its events: the outbox takes the traceparent of the current activity, and the consumer
// handles the message in an activity below it, so its log lines carry the TraceId of the request.
public class MessageTraceTests
{
    private const string Consumer = "beta-PingedHandler";

    [Fact]
    public void Current_IsTheTraceParentOfTheCurrentActivity()
    {
        using var request = new Activity("request").Start();

        Assert.Equal(request.Id, MessageTrace.Current());
        Assert.StartsWith($"00-{request.TraceId.ToHexString()}-", MessageTrace.Current());
    }

    [Fact]
    public void Current_OutsideAnActivity_IsNull()
    {
        Activity.Current = null;

        Assert.Null(MessageTrace.Current());
    }

    [Fact]
    public void Consumer_ContinuesTheTraceOfTheSender()
    {
        var sender = SenderTraceParent(out var traceId, out var spanId);

        using var consumer = MessageTrace.StartConsumer(Consumer, sender);

        Assert.Same(consumer, Activity.Current);
        Assert.Equal(traceId, consumer.TraceId);
        Assert.Equal(spanId, consumer.ParentSpanId);
        Assert.NotEqual(spanId, consumer.SpanId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("kein traceparent")]
    public void Consumer_WithoutAValidTraceParent_StartsATraceOfItsOwn(string? traceParent)
    {
        Activity.Current = null;

        using var consumer = MessageTrace.StartConsumer(Consumer, traceParent);

        Assert.NotEqual(default, consumer.TraceId);
        Assert.Equal(default, consumer.ParentSpanId);
    }

    // With OpenTelemetry listening, as in every service (ServiceDefaults), the activity comes from the messaging source.
    [Fact]
    public void Consumer_WithAListener_IsAConsumerActivityOfTheMessagingSource()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MessageTrace.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        var sender = SenderTraceParent(out var traceId, out _);

        using var consumer = MessageTrace.StartConsumer(Consumer, sender);

        Assert.Equal(MessageTrace.SourceName, consumer.Source.Name);
        Assert.Equal(ActivityKind.Consumer, consumer.Kind);
        Assert.Equal($"{Consumer} process", consumer.DisplayName);
        Assert.Equal(traceId, consumer.TraceId);
    }

    // The traceparent a sender stored with its event, from an activity that has ended since.
    private static string SenderTraceParent(out ActivityTraceId traceId, out ActivitySpanId spanId)
    {
        Activity.Current = null;
        using var request = new Activity("request").Start();
        traceId = request.TraceId;
        spanId = request.SpanId;
        var traceParent = MessageTrace.Current()!;
        request.Stop();
        Activity.Current = null;
        return traceParent;
    }
}