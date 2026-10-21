using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Contracts;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Edvaniq.BuildingBlocks.Messaging;

public static class MessagingExtensions
{
    // The address of the broker only ever comes from here, from outside (the AppHost sets it). Without it the service
    // runs as before, and the dispatcher and the consumers stay idle.
    public const string NatsConnectionName = "nats";

    // Outbox, dispatcher and consumers of a service. service is its name in lower case (planning), TContext the
    // DbContext with its outbox and inbox tables (ServiceDbContext maps them). Like the connection string of the
    // database, the address of the broker is read when it is first needed, not at startup.
    public static IServiceCollection AddMessaging<TContext>(this IServiceCollection services, string service)
        where TContext : DbContext
    {
        services.AddOptions<MessagingOptions>()
            .BindConfiguration(MessagingOptions.Section)
            .PostConfigure(options => options.Service = service);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMessageTransport>(provider =>
            provider.GetRequiredService<IConfiguration>().GetConnectionString(NatsConnectionName) is { Length: > 0 } url
                ? ActivatorUtilities.CreateInstance<NatsTransport>(provider, url)
                : NoMessageTransport.Instance);

        services.AddSingleton(new MessagingStore(typeof(TContext)));
        services.AddScoped<IEventOutbox>(provider => new EventOutbox(
            provider.GetRequiredService<TContext>(),
            provider.GetRequiredService<IOptions<MessagingOptions>>().Value,
            provider.GetRequiredService<TimeProvider>()));

        services.AddHostedService<OutboxDispatcher>();
        services.AddHostedService<ConsumerHost>();
        services.AddHostedService<MessagingCleanup>();
        return services;
    }

    // Starts a durable consumer for THandler. The event type, which belongs in the Contracts of the sending service,
    // tells which stream and subject it listens to.
    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        services.AddScoped<THandler>();
        services.AddSingleton<EventSubscription>(new EventSubscription<TEvent, THandler>());
        return services;
    }

    // Swaps the broker for one inside the process. For tests: the services then send and receive without NATS.
    public static IServiceCollection AddInMemoryTransport(this IServiceCollection services, InMemoryTransport? transport = null)
    {
        transport ??= new InMemoryTransport();
        services.RemoveAll<IMessageTransport>();
        services.AddSingleton(transport);
        services.AddSingleton<IMessageTransport>(transport);
        return services;
    }
}