using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Edvaniq.Testing;

namespace Edvaniq.Services.ServiceName.IntegrationTests;

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
    public async Task UnknownItem_IsAProblemNamingTheCallersTrace()
    {
        using var response = await GetAsync($"/examples/{Guid.NewGuid()}", TestTokens.Create("alice"));

        Assert.Equal(TraceId, await AssertProblemAsync(response, HttpStatusCode.NotFound));
    }

    // A body the endpoint can't bind is the caller's mistake, a 400, not a 500.
    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    public async Task UnreadableBody_IsABadRequestProblem(string body)
    {
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var response = await SendAsync(HttpMethod.Post, "/examples", TestTokens.Create("alice"), content);

        Assert.Equal(TraceId, await AssertProblemAsync(response, HttpStatusCode.BadRequest));
    }

    private Task<HttpResponseMessage> GetAsync(string path, string? token) => SendAsync(HttpMethod.Get, path, token, content: null);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, HttpContent? content)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path) { Content = content };
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