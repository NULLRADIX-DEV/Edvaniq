using System.Collections.Concurrent;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Edvaniq.BuildingBlocks.Messaging;

// The messages a consumer of this process can't handle right now, since their first failure. The broker delivers such a
// message again and again, so it leaves the list once a delivery works, or once it's dropped on purpose. A restart of
// the process forgets the list, the next failing delivery starts it again.
internal sealed class ConsumerFailures(TimeProvider time)
{
    private readonly ConcurrentDictionary<(string Consumer, Guid MessageId), ConsumerFailure> failing = new();

    public void Failed(string consumer, string eventType, Guid messageId) =>
        failing.TryAdd((consumer, messageId), new ConsumerFailure(consumer, eventType, time.GetUtcNow()));

    public void Settled(string consumer, Guid messageId) => failing.TryRemove((consumer, messageId), out _);

    public IReadOnlyList<ConsumerFailure> Current() => [.. failing.Values];
}

internal sealed record ConsumerFailure(string Consumer, string EventType, DateTimeOffset Since);

// Unhealthy once a consumer has failed at one message for longer than Messaging:FailureAlarmAfter. The description names
// consumer, event type and how long, never the message, its content or the exception: those can carry user data and stay
// in the log. A broker that's away is no failure here, the broker has its own check. Tagged "details": /health shows the
// description (ServiceDefaults).
internal sealed class ConsumerHealthCheck(ConsumerFailures failures, IOptions<MessagingOptions> options, TimeProvider time) : IHealthCheck
{
    public const string Name = "messaging";

    // Same as HealthDetailsTag of ServiceDefaults, which BuildingBlocks don't reference.
    public const string DetailsTag = "details";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var stuck = failures.Current()
            .Where(failure => now - failure.Since >= options.Value.FailureAlarmAfter)
            .GroupBy(failure => (failure.Consumer, failure.EventType))
            .Select(group => $"{group.Key.Consumer} cannot handle {group.Key.EventType} for {(int)(now - group.Min(failure => failure.Since)).TotalMinutes} min")
            .Order(StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(stuck.Count == 0
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(context.Registration.FailureStatus, string.Join("; ", stuck)));
    }
}