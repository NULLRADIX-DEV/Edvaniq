using Edvaniq.Services.Identity.Application;
using Edvaniq.Services.Identity.Domain;
using Edvaniq.Testing;

namespace Edvaniq.Services.Identity.UnitTests;

public sealed class AccountServiceTests
{
    private const string Password = "a long enough password";

    private readonly InMemoryAccounts accounts = new();
    private readonly AccountService service;

    public AccountServiceTests() => service = new AccountService(accounts, new TestTime());

    [Fact]
    public async Task Login_WithTheRightPassword_GivesTheId()
    {
        var (account, created) = await service.SetPasswordAsync("ben", Password, TestContext.Current.CancellationToken);

        Assert.True(created);
        Assert.Equal(account.Id, await service.LoginAsync(" Ben ", Password, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("ben", "a wrong password")]
    [InlineData("anna", Password)]
    [InlineData(null, Password)]
    [InlineData("not a name", Password)]
    public async Task Login_WithAWrongNameOrPassword_IsNull(string? name, string password)
    {
        await service.SetPasswordAsync("ben", Password, TestContext.Current.CancellationToken);

        Assert.Null(await service.LoginAsync(name, password, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetPassword_Again_KeepsTheAccount()
    {
        var (first, _) = await service.SetPasswordAsync("ben", Password, TestContext.Current.CancellationToken);

        var (second, created) = await service.SetPasswordAsync("BEN", "another long password", TestContext.Current.CancellationToken);

        Assert.False(created);
        Assert.Same(first, second);
        Assert.Single(accounts.All);
        Assert.Null(await service.LoginAsync("ben", Password, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("two words", Password)]
    [InlineData("ben", "too short")]
    public async Task SetPassword_Invalid_Throws_AndSavesNothing(string name, string password)
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.SetPasswordAsync(name, password, TestContext.Current.CancellationToken));

        Assert.DoesNotContain(password, exception.Message, StringComparison.Ordinal);
        Assert.Empty(accounts.All);
    }

    // #244: a user account logs in with its address, spelled any way, and a team name with an @ is no name.
    [Theory]
    [InlineData("anna@example.org")]
    [InlineData(" Anna@Example.ORG ")]
    public async Task UserAccount_LogsInWithItsAddress(string login)
    {
        var account = AddUser("anna@example.org", AccountStatus.Active);

        var result = await service.AuthenticateAsync(login, Password, TestContext.Current.CancellationToken);

        Assert.Equal((account, (LoginRefusal?)null), (result.Account, result.Refusal));
        Assert.Equal(account.Id, await service.LoginAsync(login, Password, TestContext.Current.CancellationToken));
    }

    // Why an account can't log in yet is told only with the right password.
    [Theory]
    [InlineData(AccountStatus.Unconfirmed, LoginRefusal.Unconfirmed)]
    [InlineData(AccountStatus.AwaitingApproval, LoginRefusal.AwaitingApproval)]
    public async Task UserAccountNotActive_IsRefusedWithItsReason_OnlyWithTheRightPassword(AccountStatus status, LoginRefusal refusal)
    {
        AddUser("anna@example.org", status);

        var right = await service.AuthenticateAsync("anna@example.org", Password, TestContext.Current.CancellationToken);
        var wrong = await service.AuthenticateAsync("anna@example.org", "a wrong long password", TestContext.Current.CancellationToken);

        Assert.Equal(refusal, right.Refusal);
        Assert.Equal((null, (LoginRefusal?)LoginRefusal.InvalidCredentials), (wrong.Account, wrong.Refusal));
        Assert.Null(await service.LoginAsync("anna@example.org", Password, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("bob@example.org")]
    [InlineData("not@an address")]
    [InlineData("@")]
    public async Task UnknownOrInvalidAddress_IsInvalidCredentials(string login)
    {
        AddUser("anna@example.org", AccountStatus.Active);

        var result = await service.AuthenticateAsync(login, Password, TestContext.Current.CancellationToken);

        Assert.Equal((null, (LoginRefusal?)LoginRefusal.InvalidCredentials), (result.Account, result.Refusal));
    }

    private Account AddUser(string email, AccountStatus status)
    {
        var account = Account.CreateUser(email, "Anna", 2001, PasswordHasher.Hash(Password), DateTime.UnixEpoch);
        typeof(Account).GetProperty(nameof(Account.Status))!.SetValue(account, status);
        accounts.Add(account);
        return account;
    }

    private sealed class InMemoryAccounts : IAccounts
    {
        public List<Account> All { get; } = [];

        public Task<Account?> FindAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(All.FirstOrDefault(account => account.Name == name));

        public Task<Account?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult(All.FirstOrDefault(account => account.Email == email));

        public void Add(Account account) => All.Add(account);

        public void Remove(Account account) => All.Remove(account);

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}