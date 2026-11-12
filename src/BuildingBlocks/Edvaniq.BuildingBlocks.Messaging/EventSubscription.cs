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

    // The event's type name, as the catalog names it (docs/spec/events.md).
    public string EventName { get; } = eventType.Name;

    public EventRoute Route { get; } = EventRoute.For(eventType);

    // Reads the event from its JSON.
    public abstract IIntegrationEvent Read(string payload);

    // Hands the event to the handler, which the scope provides.
    public abstract Task HandleAsync(IServiceProvider scope, IIntegrationEvent integrationEvent, CancellationToken cancellationToken);
}

internal sealed class EventSubscription<TEvent, THandler>() : EventSubscription(typeof(TEvent), typeof(THandler))
    where TEvent : IIntegrationEvent
    where THandler : IIntegrationEventHandler<TEvent>
{
    public override IIntegrationEvent Read(string payload) =>
        JsonSerializer.Deserialize<TEvent>(payload, EventJson.Options)
            ?? throw new JsonException($"The message is no {typeof(TEvent).Name}.");

    public override Task HandleAsync(IServiceProvider scope, IIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        scope.GetRequiredService<THandler>().HandleAsync((TEvent)integrationEvent, cancellationToken);
}