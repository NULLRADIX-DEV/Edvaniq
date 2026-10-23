using System.Collections.Concurrent;
using System.Diagnostics;

using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Sender.Contracts;

namespace Edvaniq.BuildingBlocks.Messaging.IntegrationTests;

// What a test tells the handlers to do, and what they did.
public sealed class HandlerBehavior
{
    private int calls;

    public int Calls => Volatile.Read(ref calls);

    public ConcurrentQueue<ActivityTraceId> TraceIds { get; } = new();

    // The first call changes the DbContext and then throws, the way a handler fails halfway.
    public bool FailFirstCall { get; set; }

    internal void Enter()
    {
        TraceIds.Enqueue(Activity.Current?.TraceId ?? default);
        if (Interlocked.Increment(ref calls) == 1 && FailFirstCall)
        {
            throw new InvalidOperationException("Not now.");
        }
    }
}

// Writes a notification for the event. It leaves the SaveChanges to the consumer.
public sealed class GreetedHandler(ReceiverDbContext db, HandlerBehavior behavior) : IIntegrationEventHandler<Greeted>
{
    public Task HandleAsync(Greeted integrationEvent, CancellationToken cancellationToken)
    {
        db.Notifications.Add(new Notification { Id = Guid.NewGuid(), GreetingId = integrationEvent.GreetingId, Text = integrationEvent.Text });
        behavior.Enter();
        return Task.CompletedTask;
    }
}

public sealed class SecondGreetedHandler(ReceiverDbContext db) : IIntegrationEventHandler<Greeted>
{
    public Task HandleAsync(Greeted integrationEvent, CancellationToken cancellationToken)
    {
        db.Notifications.Add(new Notification { Id = Guid.NewGuid(), GreetingId = integrationEvent.GreetingId, Text = $"zweiter: {integrationEvent.Text}" });
        return Task.CompletedTask;
    }
}