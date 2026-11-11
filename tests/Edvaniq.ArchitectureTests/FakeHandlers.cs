using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Contracts.Fakes;
using Edvaniq.Services.Planning.Contracts;

namespace Edvaniq.Services.Content.Application.Fakes;

// Handlers that look as if Content had them, for DuplicateDeliveryTests. Like the fake events they live in this test
// project only, so the check over the real services never sees them.
public sealed class DocumentSharedHandler : IIntegrationEventHandler<DocumentShared>
{
    public Task HandleAsync(DocumentShared integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class UserDeletedHandler : IIntegrationEventHandler<UserDeleted>
{
    public Task HandleAsync(UserDeleted integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
}

// The template's example, which needs no row.
public sealed class ExampleItemCreatedHandler : IIntegrationEventHandler<ExampleItemCreated>
{
    public Task HandleAsync(ExampleItemCreated integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
}

public abstract class HandlerBase : IIntegrationEventHandler<DocumentShared>
{
    public abstract Task HandleAsync(DocumentShared integrationEvent, CancellationToken cancellationToken);
}