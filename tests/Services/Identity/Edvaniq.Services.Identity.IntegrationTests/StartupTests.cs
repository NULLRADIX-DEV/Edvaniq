using System.Security.Cryptography;

using Edvaniq.Services.Identity.Api.Tokens;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Identity.IntegrationTests;

// Identity refuses to start with an incomplete configuration instead of failing at the first token request. The
// message names the key, never its value.
//
// The tests start AddTokenIssuer in a host of their own, not through IdentityFactory. When a start fails there, the
// factory races the host's disposal and now and then reports an ObjectDisposedException instead of the actual error.
public sealed class StartupTests
{
    [Fact]
    public async Task CompleteConfiguration_Starts() => await StartAsync(new Dictionary<string, string?>());

    [Theory]
    [InlineData("Tokens:Issuer", null, "Tokens:Issuer")]
    [InlineData("Tokens:Audience", null, "Tokens:Audience")]
    [InlineData("Tokens:Lifetime", "00:00:00", "Tokens:Lifetime")]
    [InlineData("Tokens:SigningKey", null, "Tokens:SigningKey")]
    [InlineData("Tokens:SigningKey", "bm90IGEga2V5", "Tokens:SigningKey")]
    [InlineData("Tokens:Clients:web:Secret", null, "Tokens:Clients")]
    [InlineData("Tokens:Clients:web:Secret", "too-short", "Tokens:Clients:web:Secret")]
    public async Task IncompleteConfiguration_StopsTheStart(string key, string? value, string message)
    {
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => StartAsync(new Dictionary<string, string?> { [key] = value }));

        Assert.Contains(message, exception.Message);
        if (value is not null)
        {
            Assert.DoesNotContain(value, exception.Message);
        }
    }

    // A key from another curve or algorithm would sign tokens the services cannot check with ES256.
    [Fact]
    public async Task SigningKeyOfAnotherCurve_StopsTheStart()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => StartAsync(new Dictionary<string, string?>
        {
            ["Tokens:SigningKey"] = Convert.ToBase64String(other.ExportPkcs8PrivateKey()),
        }));

        Assert.Contains("Tokens:SigningKey", exception.Message);
    }

    // A complete configuration as Identity gets it from appsettings.json and the environment. Changes replace single
    // keys, null stands for a missing one.
    private static async Task StartAsync(IReadOnlyDictionary<string, string?> changes)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Tokens:Issuer"] = IdentityFactory.Issuer,
            ["Tokens:Audience"] = "edvaniq-api",
            ["Tokens:Lifetime"] = "00:05:00",
            ["Tokens:SigningKey"] = IdentityFactory.NewSigningKey(),
            [$"Tokens:Clients:{IdentityFactory.ClientId}:Secret"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
        };
        foreach (var (key, value) in changes)
        {
            settings[key] = value ?? "";
        }

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddTokenIssuer();

        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }
}