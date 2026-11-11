using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Identity.Api.Accounts;
using Edvaniq.Services.Identity.Contracts;
using Edvaniq.Services.Identity.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Identity.IntegrationTests;

// account set <name> with the password on stdin sets up a team account on the server, reproducibly: a second run with
// the same name keeps the account and its id. Nobody writes rows into the database by hand.
public sealed class AccountCommandTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    private const string Password = "a long enough password";

    [Fact]
    public async Task NewName_CreatesTheAccount_WithAHashInsteadOfThePassword()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);

        var (exitCode, output, error) = await identity.SetAccountAsync("Created", Password);

        Assert.Equal(AccountCommand.Ok, exitCode);
        Assert.Empty(error);
        var account = await FindAsync(identity, "created");
        Assert.Equal($"created created {account.Id:D}", output.Trim());
        Assert.StartsWith("pbkdf2-sha256$600000$", account.PasswordHash, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, account.PasswordHash, StringComparison.Ordinal);
        Assert.Equal(DateTimeKind.Utc, account.CreatedAt.Kind);
    }

    [Fact]
    public async Task SameName_Again_KeepsTheAccountAndItsId()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        await identity.SetAccountAsync("again", Password);
        var first = await FindAsync(identity, "again");

        var (exitCode, output, _) = await identity.SetAccountAsync("AGAIN", "another long password");

        Assert.Equal(AccountCommand.Ok, exitCode);
        Assert.Equal($"updated again {first.Id:D}", output.Trim());
        var second = await FindAsync(identity, "again");
        Assert.Equal(first.Id, second.Id);
        Assert.NotEqual(first.PasswordHash, second.PasswordHash);
    }

    [Theory]
    [InlineData("not a name", Password, "a-z")]
    [InlineData("", Password, "a-z")]
    [InlineData("short", "too short", "12")]
    public async Task InvalidNameOrPassword_ChangesNothing(string name, string password, string message)
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);

        var (exitCode, output, error) = await identity.SetAccountAsync(name, password);

        Assert.Equal(AccountCommand.Invalid, exitCode);
        Assert.Empty(output);
        Assert.Contains(message, error, StringComparison.Ordinal);
        Assert.DoesNotContain(password, error, StringComparison.Ordinal);
        Assert.Null(await FindOrNullAsync(identity, "short"));
    }

    [Theory]
    [InlineData("account")]
    [InlineData("account", "set")]
    [InlineData("account", "remove", "someone")]
    public async Task OtherArguments_PrintTheUsage(params string[] args)
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await AccountCommand.RunAsync(identity.Services, args, new StringReader(Password), output, error);

        Assert.Equal(AccountCommand.Invalid, exitCode);
        Assert.Contains("account set <name>", error.ToString(), StringComparison.Ordinal);
    }

    // account delete removes the account and sends UserDeleted with the sub of its tokens in the same save. Other
    // accounts stay, a second run finds nothing and sends nothing, and the output names nothing but the account name.
    [Fact]
    public async Task Delete_RemovesTheAccount_AndSendsUserDeletedOnce()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var name = $"gone-{Guid.NewGuid():N}"[..20];
        var other = $"kept-{Guid.NewGuid():N}"[..20];
        await identity.SetAccountAsync(name, Password);
        await identity.SetAccountAsync(other, Password);
        var account = await FindAsync(identity, name);

        var (exitCode, output, error) = await DeleteAsync(identity, name);
        var (again, againOutput, _) = await DeleteAsync(identity, name);

        Assert.Equal((AccountCommand.Ok, AccountCommand.Ok), (exitCode, again));
        Assert.Empty(error);
        Assert.Equal($"deleted {name}", output.Trim());
        Assert.Equal($"no account {name}", againOutput.Trim());
        Assert.DoesNotContain(account.Id.ToString("D"), output, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await FindOrNullAsync(identity, name));
        Assert.NotNull(await FindOrNullAsync(identity, other));
        Assert.Single(await DeletedUsersAsync(identity), user => user == account.Id.ToString("D"));
    }

    private static async Task<(int ExitCode, string Output, string Error)> DeleteAsync(IdentityFactory identity, string name)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await AccountCommand.RunAsync(identity.Services, ["account", "delete", name], new StringReader(""), output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    // The user ids of every UserDeleted in the outbox, sent or not.
    private static async Task<List<string>> DeletedUsersAsync(IdentityFactory identity)
    {
        using var scope = identity.Services.CreateScope();
        var subject = EventRoute.For(typeof(UserDeleted)).Subject;
        var payloads = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Set<OutboxMessage>()
            .Where(message => message.Subject == subject)
            .Select(message => message.Payload)
            .ToListAsync(TestContext.Current.CancellationToken);
        return [.. payloads.Select(payload => JsonSerializer.Deserialize<UserDeleted>(payload, JsonSerializerOptions.Web)!.UserId)];
    }

    private static async Task<Domain.Account> FindAsync(IdentityFactory identity, string name) =>
        await FindOrNullAsync(identity, name) ?? throw new InvalidOperationException($"No account {name}.");

    private static async Task<Domain.Account?> FindOrNullAsync(IdentityFactory identity, string name)
    {
        using var scope = identity.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Accounts.AsNoTracking()
            .FirstOrDefaultAsync(account => account.Name == name, TestContext.Current.CancellationToken);
    }
}