namespace Edvaniq.Client.Core;

// The confirmation of a new account's address. Without a login, the host passes it on to Identity.
public interface IAccountConfirmations
{
    // Asks for a new link. The host answers the same whether the address has an account or not.
    Task<ResendOutcome> ResendAsync(string email, CancellationToken cancellationToken = default);
}

public enum ResendResult
{
    Sent,

    // Asked too often, RetryAfter says how long to wait if the host said so.
    Limited,

    Unavailable,
}

// MailDelayed: the mail may take a while, the host could not hand it over right away.
public sealed record ResendOutcome(ResendResult Result, bool MailDelayed = false, TimeSpan? RetryAfter = null);