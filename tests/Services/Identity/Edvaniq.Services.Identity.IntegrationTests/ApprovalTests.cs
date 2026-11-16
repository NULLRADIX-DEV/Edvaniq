using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Identity.Api.Accounts;
using Edvaniq.Services.Identity.Application;
using Edvaniq.Services.Identity.Domain;
using Edvaniq.Services.Identity.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Identity.IntegrationTests;

// #256, #268: of age by the year of birth, an account needs no further step. A minor's waits after confirming until the
// operator lets it in (account approve), and the operator can take that back (account revoke). account pending lists who
// waits. Only the date of the approval is kept, addresses reach the console and never the log.
public sealed class ApprovalTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    private const string Password = "ein langes Passwort";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OfAge_LogsInRightAfterConfirming()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var email = await ConfirmedAsync(identity, DateTime.UtcNow.Year - AccountProfile.AgeOfMajority);

        using var login = await identity.LoginAsync(email, Password);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Null(Find(identity, email).ApprovedAt);
    }

    [Fact]
    public async Task Minor_LogsInOnlyAfterApproval_UntilItIsTakenBack()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var email = await ConfirmedAsync(identity, DateTime.UtcNow.Year - 12);

        using (var waiting = await identity.LoginAsync(email, Password))
        {
            Assert.Equal("awaiting_approval", (await waiting.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("reason").GetString());
        }

        Assert.Contains(email, await CommandAsync(identity, "pending"), StringComparison.Ordinal);
        Assert.Equal("approved", await CommandAsync(identity, "approve", email));
        Assert.Equal("no account", await CommandAsync(identity, "approve", email));
        Assert.DoesNotContain(email, await CommandAsync(identity, "pending"), StringComparison.Ordinal);
        Assert.NotNull(Find(identity, email).ApprovedAt);
        using (var approved = await identity.LoginAsync(email, Password))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        Assert.Equal("revoked", await CommandAsync(identity, "revoke", email));
        Assert.Equal("no account", await CommandAsync(identity, "revoke", email));
        var account = Find(identity, email);
        Assert.Equal((AccountStatus.AwaitingApproval, (DateTime?)null), (account.Status, account.ApprovedAt));
        using var revoked = await identity.LoginAsync(email, Password);
        Assert.Equal("awaiting_approval", (await revoked.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("reason").GetString());

        Assert.DoesNotContain(identity.Logs.Entries, entry => entry.Text.Contains(email, StringComparison.OrdinalIgnoreCase));
    }

    // Of age, unconfirmed or unknown: nothing to approve or take back.
    [Fact]
    public async Task OnlyAWaitingMinor_IsApproved()
    {
        await using var identity = new IdentityFactory(connectionString: database.ConnectionString);
        var adult = await ConfirmedAsync(identity, 2000);
        var unconfirmed = $"open-{Guid.NewGuid():N}@example.org";
        await AddAsync(identity, unconfirmed, 2015);

        Assert.Equal("no account", await CommandAsync(identity, "approve", adult));
        Assert.Equal("no account", await CommandAsync(identity, "revoke", adult));
        Assert.Equal("no account", await CommandAsync(identity, "approve", unconfirmed));
        Assert.Equal("no account", await CommandAsync(identity, "approve", "nobody@example.org"));
        Assert.Equal(AccountStatus.Unconfirmed, Find(identity, unconfirmed).Status);
    }

    // A confirmed account as the link leaves it, straight into the database.
    private static async Task<string> ConfirmedAsync(IdentityFactory identity, int birthYear)
    {
        var email = $"lena-{Guid.NewGuid():N}@example.org";
        var account = await AddAsync(identity, email, birthYear);
        using var scope = identity.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Accounts.Single(item => item.Id == account.Id).Confirm(DateTime.UtcNow);
        await db.SaveChangesAsync(Cancel);
        return email;
    }

    private static async Task<Account> AddAsync(IdentityFactory identity, string email, int birthYear)
    {
        var account = Account.CreateUser(email, "Lernfuchs", birthYear, PasswordHasher.Hash(Password), DateTime.UtcNow);
        using var scope = identity.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Accounts.Add(account);
        await db.SaveChangesAsync(Cancel);
        return account;
    }

    private static async Task<string> CommandAsync(IdentityFactory identity, params string[] args)
    {
        using var output = new StringWriter();
        Assert.Equal(AccountCommand.Ok, await AccountCommand.RunAsync(identity.Services, ["account", .. args], TextReader.Null, output, TextWriter.Null));
        return output.ToString().Trim();
    }

    private static Account Find(IdentityFactory identity, string email)
    {
        using var scope = identity.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Accounts.AsNoTracking().Single(account => account.Email == email);
    }
}