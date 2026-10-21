using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.ServiceName.Contracts;

namespace Edvaniq.Services.ServiceName.Application;

// Example of a consumer, registered in InfrastructureExtensions. This one hears the service's own example event, a real
// handler hears an event of another service through a reference to its .Contracts and changes its own data. It must not
// call SaveChanges: the consumer saves its changes together with the inbox entry.
public sealed class ExampleItemCreatedHandler : IIntegrationEventHandler<ExampleItemCreated>
{
    public Task HandleAsync(ExampleItemCreated integrationEvent, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}