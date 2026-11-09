using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Answers logins with the given outcome and remembers what it was asked. While a gate is set, every login waits until
// the test opens it.
internal sealed class FakeWebSession(SignInOutcome? outcome = null) : IWebSession
{
    public bool SignedIn { get; set; }

    public List<(string Name, string Password)> Logins { get; } = [];

    public int Logouts { get; private set; }

    public TaskCompletionSource? Gate { get; set; }

    public Task<bool> IsSignedInAsync(CancellationToken cancellationToken = default) => Task.FromResult(SignedIn);

    public async Task<SignInOutcome> SignInAsync(string name, string password, CancellationToken cancellationToken = default)
    {
        Logins.Add((name, password));
        if (Gate is not null)
        {
            await Gate.Task;
        }

        var result = outcome ?? new SignInOutcome(SignInResult.SignedIn);
        SignedIn = result.Result == SignInResult.SignedIn;
        return result;
    }

    public Task<bool> SignOutAsync(CancellationToken cancellationToken = default)
    {
        Logouts++;
        SignedIn = false;
        return Task.FromResult(true);
    }
}