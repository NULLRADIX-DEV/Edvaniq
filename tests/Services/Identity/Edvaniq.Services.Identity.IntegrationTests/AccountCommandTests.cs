using Edvaniq.Services.Identity.Api.Accounts;
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

    private static async Task<Domain.Account> FindAsync(IdentityFactory identity, string name) =>
        await FindOrNullAsync(identity, name) ?? throw new InvalidOperationException($"No account {name}.");

    private static async Task<Domain.Account?> FindOrNullAsync(IdentityFactory identity, string name)
    {
        using var scope = identity.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Accounts.AsNoTracking()
            .FirstOrDefaultAsync(account => account.Name == name, TestContext.Current.CancellationToken);
    }
}