using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Answers logins with the given outcome and remembers what it was asked. While a gate is set, every login waits until
// the test opens it. A login signs in as NextAccount.
internal sealed class FakeWebSession(SignInOutcome? outcome = null) : IWebSession
{
    public const string DefaultAccount = "konto-a";

    public string? Account { get; set; }

    public string NextAccount { get; set; } = DefaultAccount;

    public bool SignedIn
    {
        get => Account is not null;
        set => Account = value ? Account ?? NextAccount : null;
    }

    public List<(string Name, string Password)> Logins { get; } = [];

    public int Logouts { get; private set; }

    public TaskCompletionSource? Gate { get; set; }

    public Task<string?> AccountAsync(CancellationToken cancellationToken = default) => Task.FromResult(Account);

    public async Task<SignInOutcome> SignInAsync(string name, string password, CancellationToken cancellationToken = default)
    {
        Logins.Add((name, password));
        if (Gate is not null)
        {
            await Gate.Task;
        }

        var result = outcome ?? new SignInOutcome(SignInResult.SignedIn);
        Account = result.Result == SignInResult.SignedIn ? NextAccount : null;
        return result;
    }

    public Task<bool> SignOutAsync(CancellationToken cancellationToken = default)
    {
        Logouts++;
        Account = null;
        return Task.FromResult(true);
    }
}