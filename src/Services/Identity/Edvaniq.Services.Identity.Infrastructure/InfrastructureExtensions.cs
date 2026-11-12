using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Identity.Application;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Identity.Infrastructure;

public static class InfrastructureExtensions
{
    // The service's own database. Aspire passes it as ConnectionStrings:identitydb (AddServiceDatabase in AppHost.cs).
    public const string DatabaseName = "identitydb";

    // The name of the service in lower case. The stream of its events and the subjects come from it.
    public const string ServiceKey = "identity";

    public static TBuilder AddInfrastructure<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddMySqlDbContext<IdentityDbContext>(DatabaseName);
        builder.Services.TryAddScoped<ICurrentUser, NoCurrentUser>();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IAccounts, Accounts>();
        builder.Services.AddScoped<AccountService>();
        builder.Services.AddScoped<AccountDeletion>();
        builder.Services.AddScoped<IInviteCodes, InviteCodes>();
        builder.Services.AddScoped<InviteService>();

        // Only the outbox and its dispatcher, Identity listens to nobody. Without ConnectionStrings:nats UserDeleted waits
        // in the outbox.
        builder.Services.AddMessaging<IdentityDbContext>(ServiceKey);

        // Ready only with a reachable database, alive without it.
        builder.Services.AddHealthChecks()
            .AddMySqlReadinessCheck(DatabaseName);

        return builder;
    }

    // Brings the database to the latest migration and returns the exit code.
    public static int RunMigrations(this IHost host) => host.RunMigrations<IdentityDbContext>();
}