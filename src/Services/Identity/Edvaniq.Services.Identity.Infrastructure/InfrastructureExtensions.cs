using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Identity.Application;
using Edvaniq.Services.Identity.Infrastructure.Mail;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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
        builder.Services.AddScoped<PasswordChange>();
        builder.Services.AddScoped<IInviteCodes, InviteCodes>();
        builder.Services.AddScoped<InviteService>();
        builder.Services.AddScoped<IIdentityTransactions, IdentityTransactions>();
        builder.Services.AddScoped<Confirmation>();
        builder.Services.AddScoped<Approvals>();
        builder.Services.AddSingleton(LegalVersionsOf(builder.Configuration));
        builder.Services.AddSingleton<AccountSweep>();
        builder.Services.AddHostedService(services => services.GetRequiredService<AccountSweep>());
        builder.Services.AddScoped<Registration>();
        builder.AddMail();

        // Only the outbox and its dispatcher, Identity listens to nobody. Without ConnectionStrings:nats UserDeleted waits
        // in the outbox.
        builder.Services.AddMessaging<IdentityDbContext>(ServiceKey);

        // Ready only with a reachable database, alive without it.
        builder.Services.AddHealthChecks()
            .AddMySqlReadinessCheck(DatabaseName);

        return builder;
    }

    // System mails (#248): the outbox in the database, the sender of Mail:Provider and the worker between them. The
    // address of the web app is checked at the start, a mail with a wrong link must never go out.
    private static void AddMail(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<MailOptions>().BindConfiguration(MailOptions.Section).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<MailOptions>, MailOptionsValidation>();
        builder.Services.AddSingleton(services => new MailLinks(services.GetRequiredService<IOptions<MailOptions>>().Value.BaseAddress));
        builder.Services.AddScoped<IMailOutbox, MailOutbox>();
        builder.Services.AddScoped<IAccountTokens, AccountTokens>();
        builder.Services.AddScoped<SystemMails>();
        if (builder.Configuration.GetValue<MailProvider>($"{MailOptions.Section}:{nameof(MailOptions.Provider)}") == MailProvider.Recording)
        {
            builder.Services.AddSingleton<RecordingMailSender>();
            builder.Services.AddSingleton<IMailSender>(services => services.GetRequiredService<RecordingMailSender>());
        }

        builder.Services.AddSingleton<MailDispatcher>();
        builder.Services.AddHostedService(services => services.GetRequiredService<MailDispatcher>());
    }

    // The versions of the legal texts every registration agrees to (#261), section Legal. Required, a registration must
    // never store an empty version.
    private static LegalVersions LegalVersionsOf(IConfiguration configuration) => new(
        Required(configuration, "Legal:TermsVersion"),
        Required(configuration, "Legal:PrivacyVersion"));

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 and <= Domain.Account.LegalVersionMaxLength } value
            ? value
            : throw new InvalidOperationException($"{key} must be set, at most {Domain.Account.LegalVersionMaxLength} characters.");

    // Brings the database to the latest migration and returns the exit code.
    public static int RunMigrations(this IHost host) => host.RunMigrations<IdentityDbContext>();
}