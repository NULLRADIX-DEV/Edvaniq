using Edvaniq.Services.Identity.Api.Accounts;
using Edvaniq.Services.Identity.Application;
using Edvaniq.Services.Identity.Infrastructure;

using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Identity.IntegrationTests;

// #245: the operator makes, lists and revokes invitation codes with a command on the server. A code shows once, the
// database holds only its hash, and a code is redeemed at most once, never after it was revoked.
public sealed class InviteCommandTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_PrintsEachCodeOnce_AndTheDatabaseHoldsOnlyItsHash()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);

        var (exitCode, output, error) = await RunAsync(identity, "invite", "create", "3");

        Assert.Equal((InviteCommand.Ok, ""), (exitCode, error));
        var made = Lines(output).Select(line => line.Split(' ')).ToList();
        Assert.Equal(3, made.Count);
        var codes = made.Select(parts => parts[1].Replace("-", "", StringComparison.Ordinal)).ToList();
        var labels = made.Select(parts => parts[0]).ToList();
        var stored = Query(identity, db => db.InviteCodes.Where(invite => labels.Contains(invite.Label)).ToList());
        Assert.Equal(3, stored.Count);
        Assert.All(stored, invite => Assert.DoesNotContain(invite.CodeHash, codes));
        var (_, list, _) = await RunAsync(identity, "invite", "list");
        Assert.All(labels, label => Assert.Contains($"{label} open ", list, StringComparison.Ordinal));
        Assert.All(codes, code => Assert.DoesNotContain(code, list, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Code_IsRedeemedOnce_AndTheListSaysSo()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var (label, code) = await CreateOneAsync(identity);

        Assert.Equal(RedeemResult.Redeemed, await RedeemAsync(identity, code.ToLowerInvariant()));
        Assert.Equal(RedeemResult.AlreadyRedeemed, await RedeemAsync(identity, code));

        var (_, list, _) = await RunAsync(identity, "invite", "list");
        Assert.Contains($"{label} redeemed ", list, StringComparison.Ordinal);
        var (revoke, revokeOutput, _) = await RunAsync(identity, "invite", "revoke", label);
        Assert.Equal((InviteCommand.Refused, $"already redeemed {label}"), (revoke, revokeOutput.Trim()));
    }

    [Fact]
    public async Task RevokedCode_IsNoLongerRedeemable()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var (label, code) = await CreateOneAsync(identity);

        var (exitCode, output, _) = await RunAsync(identity, "invite", "revoke", label.ToLowerInvariant());

        Assert.Equal((InviteCommand.Ok, $"revoked {label.ToLowerInvariant()}"), (exitCode, output.Trim()));
        Assert.Equal(RedeemResult.Revoked, await RedeemAsync(identity, code));
        var (_, list, _) = await RunAsync(identity, "invite", "list");
        Assert.Contains($"{label} revoked ", list, StringComparison.Ordinal);
        var (again, againOutput, _) = await RunAsync(identity, "invite", "revoke", label);
        Assert.Equal((InviteCommand.Refused, $"already revoked {label}"), (again, againOutput.Trim()));
    }

    // Many redemptions of one code at once: exactly one wins, the database decides.
    [Fact]
    public async Task RedemptionsAtOnce_OnlyOneWins()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var (_, code) = await CreateOneAsync(identity);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => RedeemAsync(identity, code), Cancel)));

        Assert.Single(results, result => result == RedeemResult.Redeemed);
        Assert.All(results.Where(result => result != RedeemResult.Redeemed), result => Assert.Equal(RedeemResult.AlreadyRedeemed, result));
    }

    [Theory]
    [InlineData("ZZZZ-ZZZZ-ZZZZ")]
    [InlineData("not a code")]
    [InlineData(null)]
    public async Task UnknownCode_IsUnknown(string? code)
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);

        Assert.Equal(RedeemResult.Unknown, await RedeemAsync(identity, code));
    }

    [Fact]
    public async Task UnknownLabel_IsRefused()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);

        var (exitCode, output, _) = await RunAsync(identity, "invite", "revoke", "NOPE00");

        Assert.Equal((InviteCommand.Refused, "no invite NOPE00"), (exitCode, output.Trim()));
    }

    [Theory]
    [InlineData("invite")]
    [InlineData("invite", "create", "0")]
    [InlineData("invite", "create", "101")]
    [InlineData("invite", "create", "-1")]
    [InlineData("invite", "create", "zwei")]
    [InlineData("invite", "revoke")]
    [InlineData("invite", "delete", "ABC123")]
    public async Task OtherArguments_PrintTheUsage(params string[] args)
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);

        var (exitCode, output, error) = await RunAsync(identity, args);

        Assert.Equal((InviteCommand.Invalid, ""), (exitCode, output));
        Assert.Contains("invite create [count]", error, StringComparison.Ordinal);
    }

    // Only the operator on the server: no route of Identity knows invitation codes.
    [Fact]
    public async Task NoEndpoint_TouchesInvitationCodes()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        // Starts the app, so all routes are mapped.
        using var client = identity.CreateClient();

        var routes = identity.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? "");

        Assert.DoesNotContain(routes, route => route.Contains("invite", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<(string Label, string Code)> CreateOneAsync(IdentityFactory identity)
    {
        var (_, output, _) = await RunAsync(identity, "invite", "create");
        var parts = output.Trim().Split(' ');
        return (parts[0], parts[1]);
    }

    private static async Task<RedeemResult> RedeemAsync(IdentityFactory identity, string? code)
    {
        using var scope = identity.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<InviteService>().RedeemAsync(code, Cancel);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(IdentityFactory identity, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await InviteCommand.RunAsync(identity.Services, args, output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static T Query<T>(IdentityFactory identity, Func<IdentityDbContext, T> query)
    {
        using var scope = identity.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<IdentityDbContext>());
    }

    private static string[] Lines(string output) => output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}