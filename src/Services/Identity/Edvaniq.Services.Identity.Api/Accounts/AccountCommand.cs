using Edvaniq.Services.Identity.Application;

namespace Edvaniq.Services.Identity.Api.Accounts;

// dotnet Edvaniq.Services.Identity.Api.dll account set <name> [--test-double], with the password on stdin (docs/tokens.md#teamkonto-anlegen),
// or account delete <name>.
// Creates the account or gives it a new password, so running it twice changes nothing but the hash. The password is
// never an argument, which would show up in the process list and the shell history, and never in the output.
internal static partial class AccountCommand
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int Invalid = 2;

    // Marks the account for automated tests (docs/tokens.md). Without it, set clears the mark.
    public const string TestDoubleOption = "--test-double";

    public static bool Matches(string[] args) => args is ["account", ..];

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextReader input, TextWriter output, TextWriter error)
    {
        if (args is ["account", "delete", var deleted])
        {
            return await DeleteAsync(services, deleted, output, error);
        }

        var (name, usesTestDouble) = args switch
        {
            ["account", "set", var plain] => ((string?)plain, false),
            ["account", "set", var marked, TestDoubleOption] => (marked, true),
            _ => (null, false),
        };
        if (name is null)
        {
            await error.WriteLineAsync(
                $"Usage: account set <name> [{TestDoubleOption}], with the password on stdin, or account delete <name>.");
            return Invalid;
        }

        // One line, the newline from echo or a file is not part of the password.
        var password = (await input.ReadToEndAsync()).TrimEnd('\r', '\n');
        try
        {
            using var scope = services.CreateScope();
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
            var (account, created) = await accounts.SetPasswordAsync(name, password, CancellationToken.None, usesTestDouble);
            await output.WriteLineAsync(
                $"{(created ? "created" : "updated")} {account.Name} {account.Id:D}{(usesTestDouble ? " " + TestDoubleOption : "")}");
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

    // Deletes the account and sends UserDeleted through the outbox, the running identity-api dispatches it. Prints only
    // the name: no id, no hash. An unknown name changes nothing and is no error, so running it twice is fine.
    private static async Task<int> DeleteAsync(IServiceProvider services, string name, TextWriter output, TextWriter error)
    {
        try
        {
            using var scope = services.CreateScope();
            var deleted = await scope.ServiceProvider.GetRequiredService<AccountDeletion>().DeleteAsync(name, CancellationToken.None);
            await output.WriteLineAsync($"{(deleted ? "deleted" : "no account")} {name}");
            return Ok;
        }
        catch (Exception exception)
        {
            LogDeleteFailed(services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AccountCommand)), exception);
            await error.WriteLineAsync("Deleting the account failed, see the log above.");
            return Failed;
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Setting the account failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Deleting the account failed")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception);
}