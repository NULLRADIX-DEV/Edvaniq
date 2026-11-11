using System.Security.Claims;

using Edvaniq.Client.Core;

using Microsoft.AspNetCore.Components.Authorization;

namespace Edvaniq.Client.UI.Shell;

// Whether someone is signed in, as the web host sees it. Asked once, again after every login and logout. The client
// knows nothing about the user beyond that and an opaque key of the account, the token stays with the host. Each answer
// tells the drafts whose they are now and ends the hint about a login that ran out.
public sealed class SessionAuthenticationState(IWebSession session, Drafts? drafts = null, SessionWatch? watch = null)
    : AuthenticationStateProvider
{
    public const string AccountClaim = "account";

    private static readonly AuthenticationState SignedOut = new(new ClaimsPrincipal(new ClaimsIdentity()));

    private Task<AuthenticationState>? state;

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => state ??= LoadAsync();

    public Task<AuthenticationState> Refresh()
    {
        state = LoadAsync();
        NotifyAuthenticationStateChanged(state);
        return state;
    }

    private async Task<AuthenticationState> LoadAsync()
    {
        var account = await session.AccountAsync();
        drafts?.Claim(account);
        watch?.Reset();
        return account is null
            ? SignedOut
            : new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new Claim(AccountClaim, account)], "session")));
    }
}