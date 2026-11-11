namespace Edvaniq.Client.Core;

// The login of a team member at the web host. The host keeps the token, the client only learns whether someone is
// signed in and an opaque key of the account.
public interface IWebSession
{
    // The key of the account signed in, null if nobody is. It only tells two logins apart.
    Task<string?> AccountAsync(CancellationToken cancellationToken = default);

    Task<SignInOutcome> SignInAsync(string name, string password, CancellationToken cancellationToken = default);

    // False if the host could not be reached, the session may then still be there.
    Task<bool> SignOutAsync(CancellationToken cancellationToken = default);
}

public enum SignInResult
{
    SignedIn,

    // Name or password wrong. Which of the two, nobody says.
    WrongCredentials,

    // Too many attempts, RetryAfter says how long to wait.
    TooManyAttempts,

    Unavailable,
}

// A login that did not get through names the trace of the failed request when the host answered with a problem.
public sealed record SignInOutcome(SignInResult Result, TimeSpan? RetryAfter = null, string? TraceId = null);