using System.Text.Json;

using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Contracts;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.BuildingBlocks.Messaging;

// One handler of one event type, registered with AddIntegrationEventHandler. The consumer host starts a durable
// consumer for each.
internal abstract class EventSubscription(Type eventType, Type handlerType)
{
    public Type HandlerType { get; } = handlerType;

    public EventRoute Route { get; } = EventRoute.For(eventType);

    // Reads the event from its JSON and hands it to the handler, which the scope provides.
    public abstract Task HandleAsync(IServiceProvider scope, string payload, CancellationToken cancellationToken);
}

internal sealed class EventSubscription<TEvent, THandler>() : EventSubscription(typeof(TEvent), typeof(THandler))
    where TEvent : IIntegrationEvent
    where THandler : IIntegrationEventHandler<TEvent>
{
    public override Task HandleAsync(IServiceProvider scope, string payload, CancellationToken cancellationToken)
    {
        var integrationEvent = JsonSerializer.Deserialize<TEvent>(payload, MessageJson.Options)
            ?? throw new JsonException($"The message is no {typeof(TEvent).Name}.");

        return scope.GetRequiredService<THandler>().HandleAsync(integrationEvent, cancellationToken);
    }
}