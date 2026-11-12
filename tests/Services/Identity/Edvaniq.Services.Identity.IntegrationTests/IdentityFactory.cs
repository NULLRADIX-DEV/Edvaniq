using System.Net.Http.Json;
using System.Security.Cryptography;

using Edvaniq.Services.Identity.Api.Accounts;
using Edvaniq.Services.Identity.Infrastructure;
using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Identity.IntegrationTests;

// Identity as it runs, with a signing key and a client secret that exist only for this test run. Settings replaces
// single keys, null removes one, to see how Identity reacts to a configuration that is missing something. Without a
// connection string there is no database: a client's own token works, a login does not (IdentityDatabase).
public sealed class IdentityFactory(
    IReadOnlyDictionary<string, string?>? settings = null, TimeProvider? time = null, string? connectionString = null)
    : WebApplicationFactory<Program>
{
    public const string Issuer = "edvaniq-identity-tests";
    public const string ClientId = "web";

    // Content's worker, a client whose tokens are only for other services' internal endpoints.
    public const string InternalClientId = "content";

    public string ClientSecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public string InternalClientSecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    // What Identity logs.
    public TestLogs Logs { get; } = new();

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

    // POST /connect/token as the web host sends it for a login: its own credentials plus the account's.
    public Task<HttpResponseMessage> LoginAsync(
        string username, string password, string clientId = ClientId, string? clientSecret = null)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret ?? ClientSecret,
            ["username"] = username,
            ["password"] = password,
        };
        return CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(form), TestContext.Current.CancellationToken);
    }

    // Sets up an account the way the operator does on the server, through the account command.
    public async Task<(int ExitCode, string Output, string Error)> SetAccountAsync(string name, string password, bool testDouble = false)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        string[] args = testDouble ? ["account", "set", name, AccountCommand.TestDoubleOption] : ["account", "set", name];
        var exitCode = await AccountCommand.RunAsync(Services, args, new StringReader(password + "\n"), output, error);
        return (exitCode, output.ToString(), error.ToString());
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
            [$"Tokens:Clients:{InternalClientId}:Secret"] = InternalClientSecret,
        };
        if (connectionString is not null)
        {
            defaults[$"ConnectionStrings:{InfrastructureExtensions.DatabaseName}"] = connectionString;
        }

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            defaults[key] = value;
        }

        // UseSetting with null does not remove a value, so an empty string stands for a missing one.
        foreach (var (key, value) in defaults)
        {
            builder.UseSetting(key, value ?? "");
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(Logs);
            if (time is not null)
            {
                services.AddSingleton(time);
            }
        });
    }
}