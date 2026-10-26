using System.Net;
using System.Security.Cryptography;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Identity.IntegrationTests;

// Identity's log on the server never shows a client secret, the signing key or a token it hands out, not even the
// wrong secret of a rejected client.
public sealed class LoggingTests
{
    [Fact]
    public async Task TokenRequests_LogNoSecret()
    {
        var signingKey = IdentityFactory.NewSigningKey();
        var wrongSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var identity = new IdentityFactory(new Dictionary<string, string?>
        {
            [WebHostDefaults.EnvironmentKey] = Environments.Production,
            ["Tokens:SigningKey"] = signingKey,
        });

        var token = await identity.GetTokenAsync();
        using var rejected = await identity.RequestTokenAsync(clientSecret: wrongSecret);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Empty(identity.Logs.Containing(identity.ClientSecret, wrongSecret, signingKey, token));
    }
}