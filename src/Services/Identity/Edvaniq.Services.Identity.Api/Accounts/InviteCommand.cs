using System.Globalization;

using Edvaniq.Services.Identity.Application;
using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.Api.Accounts;

// dotnet Edvaniq.Services.Identity.Api.dll invite create [count] | invite list | invite revoke <label>, run by the
// operator on the server (docs/tokens.md#einladungscodes). There is no HTTP way to any of it. A code is printed once, by
// create, and never again: Identity keeps only its hash.
internal static partial class InviteCommand
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int Invalid = 2;
    public const int Refused = 3;

    private const string Usage = "Usage: invite create [count], invite list or invite revoke <label>.";

    public static bool Matches(string[] args) => args is ["invite", ..];

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            using var scope = services.CreateScope();
            var invites = scope.ServiceProvider.GetRequiredService<InviteService>();
            switch (args)
            {
                case ["invite", "create"]:
                    return await CreateAsync(invites, 1, output);
                case ["invite", "create", var text]
                    when int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                        && count is >= 1 and <= InviteService.MaxPerCreate:
                    return await CreateAsync(invites, count, output);
                case ["invite", "list"]:
                    foreach (var invite in await invites.ListAsync(CancellationToken.None))
                    {
                        await output.WriteLineAsync(Line(invite));
                    }

                    return Ok;
                case ["invite", "revoke", var label]:
                    var result = await invites.RevokeAsync(label, CancellationToken.None);
                    await output.WriteLineAsync(result switch
                    {
                        RevokeResult.Revoked => $"revoked {label}",
                        RevokeResult.AlreadyRedeemed => $"already redeemed {label}",
                        RevokeResult.AlreadyRevoked => $"already revoked {label}",
                        _ => $"no invite {label}",
                    });
                    return result == RevokeResult.Revoked ? Ok : Refused;
                default:
                    await error.WriteLineAsync($"{Usage} A count is 1 to {InviteService.MaxPerCreate}.");
                    return Invalid;
            }
        }
        catch (Exception exception)
        {
            LogFailed(services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(InviteCommand)), exception);
            await error.WriteLineAsync("The invite command failed, see the log above.");
            return Failed;
        }
    }

    private static async Task<int> CreateAsync(InviteService invites, int count, TextWriter output)
    {
        foreach (var (invite, code) in await invites.CreateAsync(count, CancellationToken.None))
        {
            await output.WriteLineAsync($"{invite.Label} {code}");
        }

        return Ok;
    }

    // Label, status and the time of the last change, UTC.
    private static string Line(InviteCode invite)
    {
        var at = invite.RevokedAt ?? invite.RedeemedAt ?? invite.CreatedAt;
        var status = invite.Status switch
        {
            InviteStatus.Redeemed => "redeemed",
            InviteStatus.Revoked => "revoked",
            _ => "open",
        };
        return $"{invite.Label} {status} {at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}Z";
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "The invite command failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}