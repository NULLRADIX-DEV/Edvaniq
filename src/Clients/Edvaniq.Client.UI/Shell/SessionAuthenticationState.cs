using System.Security.Claims;

using Edvaniq.Client.Core;

using Microsoft.AspNetCore.Components.Authorization;

namespace Edvaniq.Client.UI.Shell;

// Whether someone is signed in, as the web host sees it. Asked once, again after every login and logout. The client
// knows nothing about the user beyond that, the token stays with the host.
public sealed class SessionAuthenticationState(IWebSession session) : AuthenticationStateProvider
{
    private static readonly AuthenticationState SignedOut = new(new ClaimsPrincipal(new ClaimsIdentity()));
    private static readonly AuthenticationState SignedIn = new(new ClaimsPrincipal(new ClaimsIdentity("session")));

    private Task<AuthenticationState>? state;

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => state ??= LoadAsync();

    public void Refresh()
    {
        state = LoadAsync();
        NotifyAuthenticationStateChanged(state);
    }

    private async Task<AuthenticationState> LoadAsync() => await session.IsSignedInAsync() ? SignedIn : SignedOut;
}