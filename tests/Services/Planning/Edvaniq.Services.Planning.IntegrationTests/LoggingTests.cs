using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;

using Edvaniq.Testing;

namespace Edvaniq.Services.Planning.IntegrationTests;

// The service's log on the server never shows the password of its database or the token of a caller, not even of a
// rejected one. A ping checks the token and writes into the database.
public sealed class LoggingTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    [Fact]
    public async Task Ping_LogsNeitherTokenNorDatabasePassword()
    {
        var token = TestTokens.Create("pinger");
        var foreignToken = TestTokens.Create("pinger", signingKey: TestTokens.NewKey());
        var expiredToken = TestTokens.Create("pinger", expires: DateTime.UtcNow.AddMinutes(-6));

        Assert.Equal(HttpStatusCode.OK, await PingAsync(token));
        Assert.Equal(HttpStatusCode.Unauthorized, await PingAsync(foreignToken));
        Assert.Equal(HttpStatusCode.Unauthorized, await PingAsync(expiredToken));

        var password = new DbConnectionStringBuilder { ConnectionString = service.ConnectionString }["Pwd"].ToString()!;
        // The log saw the write, so it had the chance to show how the service got into the database.
        Assert.NotEmpty(service.Logs.Containing("INSERT INTO"));
        Assert.Empty(service.Logs.Containing(password, token, foreignToken, expiredToken));
    }

    private async Task<HttpStatusCode> PingAsync(string token)
    {
        using var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.PostAsync("/ping", content: null, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }
}