using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

internal sealed class FakeAccountConfirmations(ResendOutcome? outcome = null) : IAccountConfirmations
{
    public List<string> Resent { get; } = [];

    public Task<ResendOutcome> ResendAsync(string email, CancellationToken cancellationToken = default)
    {
        Resent.Add(email);
        return Task.FromResult(outcome ?? new ResendOutcome(ResendResult.Sent));
    }
}