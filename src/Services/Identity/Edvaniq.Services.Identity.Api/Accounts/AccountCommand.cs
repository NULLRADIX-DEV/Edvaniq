using Edvaniq.Services.Identity.Application;

namespace Edvaniq.Services.Identity.Api.Accounts;

// dotnet Edvaniq.Services.Identity.Api.dll account set <name>, with the password on stdin (docs/tokens.md#teamkonto-anlegen).
// Creates the account or gives it a new password, so running it twice changes nothing but the hash. The password is
// never an argument, which would show up in the process list and the shell history, and never in the output.
internal static partial class AccountCommand
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int Invalid = 2;

    public static bool Matches(string[] args) => args is ["account", ..];

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextReader input, TextWriter output, TextWriter error)
    {
        if (args is not ["account", "set", var name])
        {
            await error.WriteLineAsync("Usage: account set <name>, with the password on stdin.");
            return Invalid;
        }

        // One line, the newline from echo or a file is not part of the password.
        var password = (await input.ReadToEndAsync()).TrimEnd('\r', '\n');
        try
        {
            using var scope = services.CreateScope();
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
            var (account, created) = await accounts.SetPasswordAsync(name, password, CancellationToken.None);
            await output.WriteLineAsync($"{(created ? "created" : "updated")} {account.Name} {account.Id:D}");
            return Ok;
        }
        catch (ArgumentException exception)
        {
            await error.WriteLineAsync(exception.Message);
            return Invalid;
        }
        catch (Exception exception)
        {
            LogFailed(services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AccountCommand)), exception);
            await error.WriteLineAsync("Setting the account failed, see the log above.");
            return Failed;
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Setting the account failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}