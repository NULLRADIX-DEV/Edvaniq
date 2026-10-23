using System.Text.Json;

using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Contracts;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.BuildingBlocks.Messaging;

// Adds the event to the DbContext of the request. The caller's SaveChanges stores it with the change.
internal sealed class EventOutbox(DbContext db, MessagingOptions options, TimeProvider time) : IEventOutbox
{
    public void Add(IIntegrationEvent integrationEvent)
    {
        var type = integrationEvent.GetType();
        var route = EventRoute.For(type);
        if (!route.Service.Equals(options.Service, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{type.FullName} belongs to {route.Service}, a service only sends its own events ({options.Service}).");
        }

        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            MessageId = Guid.NewGuid(),
            Subject = route.Subject,
            Payload = JsonSerializer.Serialize(integrationEvent, type, MessageJson.Options),
            OccurredAt = time.GetUtcNow().UtcDateTime,
            TraceParent = MessageTrace.Current(),
        });
    }
}