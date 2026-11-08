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

    private sealed class InMemoryAccounts : IAccounts
    {
        public List<Account> All { get; } = [];

        public Task<Account?> FindAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(All.FirstOrDefault(account => account.Name == name));

        public void Add(Account account) => All.Add(account);

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}