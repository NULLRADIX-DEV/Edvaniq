using Edvaniq.Services.Identity.Application;
using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.UnitTests;

public sealed class PasswordChangeTests
{
    private const string Current = "the current password";
    private const string Next = "the brand new password";

    private readonly InMemoryAccounts accounts = new();
    private readonly CountingAttempts attempts = new();
    private readonly PasswordChange change;

    public PasswordChangeTests() => change = new PasswordChange(accounts, attempts);

    [Fact]
    public async Task RightPassword_ReplacesTheHash_ForATeamAccount()
    {
        var account = AddTeam("ben");
        var before = account.PasswordHash;

        var result = await ChangeAsync(account, Current, Next);

        Assert.Equal(PasswordChangeOutcome.Changed, result.Outcome);
        Assert.NotEqual(before, account.PasswordHash);
        Assert.True(PasswordHasher.Verify(Next, account.PasswordHash));
        Assert.False(PasswordHasher.Verify(Current, account.PasswordHash));
        Assert.Empty(attempts.Failures);
    }

    [Fact]
    public async Task RightPassword_ReplacesTheHash_ForAUserAccount()
    {
        var account = AddUser("anna@example.org");

        var result = await ChangeAsync(account, Current, Next);

        Assert.Equal(PasswordChangeOutcome.Changed, result.Outcome);
        Assert.True(PasswordHasher.Verify(Next, account.PasswordHash));
    }

    [Fact]
    public async Task WrongCurrentPassword_IsRefused_KeepsTheHash_AndCountsAsFailure()
    {
        var account = AddTeam("ben");
        var before = account.PasswordHash;

        var result = await ChangeAsync(account, "a wrong password", Next);

        Assert.Equal((PasswordChangeOutcome.CurrentInvalid, "Dein aktuelles Passwort stimmt nicht."), (result.Outcome, result.Detail));
        Assert.Equal(before, account.PasswordHash);
        Assert.Equal(["ben"], attempts.Failures);
    }

    // No password is no guess: a client sending the wrong field must not lock the login.
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task MissingCurrentPassword_IsRefused_KeepsTheHash_AndDoesNotCount(string? current)
    {
        var account = AddTeam("ben");
        var before = account.PasswordHash;

        var result = await ChangeAsync(account, current, Next);

        Assert.Equal((PasswordChangeOutcome.CurrentInvalid, "Gib dein aktuelles Passwort ein."), (result.Outcome, result.Detail));
        Assert.Equal(before, account.PasswordHash);
        Assert.Empty(attempts.Failures);
    }

    [Fact]
    public async Task CurrentPasswordBeyondTheLimit_IsAWrongOne()
    {
        var account = AddTeam("ben");

        var result = await ChangeAsync(account, new string('x', PasswordHasher.MaximumLength + 1), Next);

        Assert.Equal(PasswordChangeOutcome.CurrentInvalid, result.Outcome);
        Assert.Equal(["ben"], attempts.Failures);
    }

    // A user account counts under its normalized address, the key the login uses.
    [Fact]
    public async Task WrongPassword_OfAUserAccount_CountsUnderItsAddress()
    {
        var account = AddUser("anna@example.org");

        await ChangeAsync(account, "a wrong password", Next);

        Assert.Equal(["anna@example.org"], attempts.Failures);
    }

    [Theory]
    [InlineData("too short", "Dein Passwort braucht mindestens 12 Zeichen.")]
    [InlineData("", "Dein Passwort braucht mindestens 12 Zeichen.")]
    [InlineData(null, "Dein Passwort braucht mindestens 12 Zeichen.")]
    public async Task NewPasswordTooShort_IsRefused_AndKeepsTheHash(string? next, string sentence)
    {
        var account = AddTeam("ben");
        var before = account.PasswordHash;

        var result = await ChangeAsync(account, Current, next);

        Assert.Equal((PasswordChangeOutcome.RulesViolated, sentence), (result.Outcome, result.Detail));
        Assert.Equal(before, account.PasswordHash);
        Assert.Empty(attempts.Failures);
    }

    [Fact]
    public async Task NewPasswordTooLong_IsRefused_AndKeepsTheHash()
    {
        var account = AddTeam("ben");
        var before = account.PasswordHash;

        var result = await ChangeAsync(account, Current, new string('x', PasswordHasher.MaximumLength + 1));

        Assert.Equal((PasswordChangeOutcome.RulesViolated, "Dein Passwort darf höchstens 256 Zeichen haben."), (result.Outcome, result.Detail));
        Assert.Equal(before, account.PasswordHash);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(256)]
    public async Task NewPasswordAtTheLimits_IsAccepted(int length)
    {
        var account = AddTeam("ben");

        var result = await ChangeAsync(account, Current, new string('x', length));

        Assert.Equal(PasswordChangeOutcome.Changed, result.Outcome);
    }

    // Locked means no look at the password at all: not the right one, and no new failure for a wrong one.
    [Theory]
    [InlineData(Current)]
    [InlineData("a wrong password")]
    public async Task LockedLogin_IsRefusedWithTheWait_WithoutCheckingThePassword(string current)
    {
        var account = AddTeam("ben");
        var before = account.PasswordHash;
        attempts.LockedFor = TimeSpan.FromMinutes(7);

        var result = await ChangeAsync(account, current, Next);

        Assert.Equal((PasswordChangeOutcome.Locked, TimeSpan.FromMinutes(7)), (result.Outcome, result.RetryAfter));
        Assert.Equal(before, account.PasswordHash);
        Assert.Empty(attempts.Failures);
    }

    [Fact]
    public async Task UnknownAccount_IsNotFound()
    {
        var result = await change.ChangeAsync(Guid.NewGuid(), Current, Next, TestContext.Current.CancellationToken);

        Assert.Equal(PasswordChangeOutcome.AccountNotFound, result.Outcome);
        Assert.Empty(attempts.Failures);
    }

    private Task<PasswordChangeResult> ChangeAsync(Account account, string? current, string? next) =>
        change.ChangeAsync(account.Id, current, next, TestContext.Current.CancellationToken);

    private Account AddTeam(string name)
    {
        var account = Account.Create(name, PasswordHasher.Hash(Current), DateTime.UnixEpoch);
        accounts.Add(account);
        return account;
    }

    private Account AddUser(string email)
    {
        var account = Account.CreateUser(email, "Anna", 2001, PasswordHasher.Hash(Current), DateTime.UnixEpoch);
        accounts.Add(account);
        return account;
    }

    private sealed class CountingAttempts : ILoginAttempts
    {
        public List<string?> Failures { get; } = [];

        public TimeSpan? LockedFor { get; set; }

        public bool IsLocked(string? login, out TimeSpan retryAfter)
        {
            retryAfter = LockedFor ?? TimeSpan.Zero;
            return LockedFor is not null;
        }

        public void Failed(string? login) => Failures.Add(login);
    }
}
