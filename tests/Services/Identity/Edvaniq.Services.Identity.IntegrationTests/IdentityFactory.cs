using System.Net.Http.Json;
using System.Security.Cryptography;

using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Identity.IntegrationTests;

// Identity as it runs, with a signing key and a client secret that exist only for this test run. Settings replaces
// single keys, null removes one, to see how Identity reacts to a configuration that is missing something.
public sealed class IdentityFactory(IReadOnlyDictionary<string, string?>? settings = null, TimeProvider? time = null)
    : WebApplicationFactory<Program>
{
    public const string Issuer = "edvaniq-identity-tests";
    public const string ClientId = "web";

    public string ClientSecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static string NewSigningKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Convert.ToBase64String(key.ExportPkcs8PrivateKey());
    }

    // POST /connect/token as the web host sends it: client credentials in the form.
    public Task<HttpResponseMessage> RequestTokenAsync(
        string grantType = "client_credentials", string clientId = ClientId, string? clientSecret = null)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = grantType,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret ?? ClientSecret,
        };
        return CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(form), TestContext.Current.CancellationToken);
    }

    public async Task<string> GetTokenAsync()
    {
        using var response = await RequestTokenAsync();
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(TestContext.Current.CancellationToken);
        return body!["access_token"].ToString()!;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var defaults = new Dictionary<string, string?>
        {
            ["Tokens:Issuer"] = Issuer,
            ["Tokens:SigningKey"] = NewSigningKey(),
            [$"Tokens:Clients:{ClientId}:Secret"] = ClientSecret,
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            defaults[key] = value;
        }

        // UseSetting with null does not remove a value, so an empty string stands for a missing one.
        foreach (var (key, value) in defaults)
        {
            builder.UseSetting(key, value ?? "");
        }

        if (time is not null)
        {
            builder.ConfigureTestServices(services => services.AddSingleton(time));
        }
    }
}