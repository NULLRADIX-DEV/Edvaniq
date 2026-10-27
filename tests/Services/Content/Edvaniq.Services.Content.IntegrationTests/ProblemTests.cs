using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Edvaniq.Testing;

namespace Edvaniq.Services.Content.IntegrationTests;

// Errors answer as problems that name the caller's trace, the id the gateway and the web host log for the same request.
// Nothing about the inside gets out.
public sealed class ProblemTests(ServiceFactory factory) : IClassFixture<ServiceFactory>
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    [Fact]
    public async Task WithoutToken_IsAProblemNamingTheCallersTrace()
    {
        using var response = await GetAsync("/me", token: null);

        Assert.Equal(TraceId, await AssertProblemAsync(response, HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task UnknownDocument_IsAProblemNamingTheCallersTrace()
    {
        using var response = await GetAsync($"/documents/{Guid.NewGuid()}", TestTokens.Create("alice"));

        Assert.Equal(TraceId, await AssertProblemAsync(response, HttpStatusCode.NotFound));
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string? token)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("traceparent", $"00-{TraceId}-00f067aa0ba902b7-01");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    // Returns the trace id the problem names.
    private static async Task<string?> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        return problem.GetProperty("traceId").GetString();
    }
}