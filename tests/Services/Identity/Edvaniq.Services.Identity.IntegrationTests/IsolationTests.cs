using Edvaniq.Testing;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Identity.IntegrationTests;

// #271: Discovery, key set, token endpoint and registration are open to anyone by design and hold no user's data, so the
// isolation check leaves them out. The one endpoint with a user's token (#254) changes only the account of its subject.
// Every endpoint added later fails the check until it is listed, and with a seed of its own it gets the same test as
// every service.
public sealed class IsolationTests
{
    internal static IsolationCheck Check() => new IsolationCheck().Exempt(
        "POST",
        "/accounts/password",
        "Changes the password of the account the token names, the request names no account.",
        nameof(PasswordChangeTests.Subject_DecidesTheAccount_NotTheBody));

    [Fact]
    public async Task EveryEndpoint_IsInTheCheck()
    {
        await using var identity = new IdentityFactory();

        Check().AssertCovers(identity.Services);
    }

    // The proof for the endpoints to come: one for a user's token that the list doesn't name fails it.
    [Fact]
    public async Task NewEndpoint_FailsTheCheckUntilItIsListed()
    {
        await using var identity = new IdentityFactory();
        await using var withMore = identity.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, ProfileEndpoint>()));

        var error = Assert.Throws<IsolationCheckException>(() => Check().AssertCovers(withMore.Services));

        Assert.Contains("GET /profile", error.Message, StringComparison.Ordinal);
    }

    // What #244 might add: the profile of the user from the token.
    private sealed class ProfileEndpoint : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.UseRouting();
            app.UseEndpoints(endpoints => endpoints.MapGet("/profile", () => Results.Ok()));
        };
    }
}