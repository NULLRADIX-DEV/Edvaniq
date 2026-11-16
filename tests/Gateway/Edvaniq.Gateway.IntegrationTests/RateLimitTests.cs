using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace Edvaniq.Gateway.IntegrationTests;

// #420: calls that cost AI, answers and session steps are limited per account. Over the limit the caller gets a 429
// problem with Retry-After, a normal session and a quiz sent at once after being offline stay well below.
public sealed partial class RateLimitTests
{
    // Every call of Content, Assessment and Planning that changes something, with its limit or none. A new endpoint
    // there turns WriteEndpoints_AreAllListed red until it is listed here.
    public static readonly TheoryData<string, string, string, string?> WriteEndpoints = new()
    {
        { "content", "POST", "/content/documents", "ai" },
        { "content", "POST", "/content/documents/files", "ai" },
        { "content", "POST", $"/content/documents/{Guid.Empty}/retry", "ai" },
        { "content", "POST", $"/content/topics/{Guid.Empty}/summaries/short", "ai" },
        { "content", "DELETE", $"/content/documents/{Guid.Empty}", null },

        // The demo asks no AI, and a user has one (#411).
        { "content", "POST", "/content/demo", null },
        { "content", "DELETE", "/content/demo", null },
        { "assessment", "POST", "/assessment/attempts", "answers" },
        { "assessment", "POST", $"/assessment/attempts/{Guid.Empty}/answer", "answers" },
        { "assessment", "POST", "/assessment/check-questions", "answers" },
        { "assessment", "POST", "/assessment/quizzes", "answers" },
        { "assessment", "POST", "/assessment/quizzes/weaknesses", "answers" },
        { "planning", "POST", "/planning/sessions", "sessions" },
        { "planning", "POST", "/planning/sessions/bonus", "sessions" },
        { "planning", "POST", $"/planning/sessions/{Guid.Empty}/abort", "sessions" },
        { "planning", "POST", $"/planning/sessions/{Guid.Empty}/complete", "sessions" },
        { "planning", "POST", $"/planning/sessions/{Guid.Empty}/advance", "sessions" },
        { "planning", "POST", $"/planning/sessions/{Guid.Empty}/activity", "sessions" },

        // The guest ping of the smoke test, limited at the web host.
        { "planning", "POST", "/planning/ping", null },

        // Settings a person changes now and then, nothing that costs.
        { "planning", "POST", "/planning/learning-goals/default", null },
        { "planning", "POST", "/planning/learning-goals", null },
        { "planning", "PUT", $"/planning/learning-goals/{Guid.Empty}", null },
        { "planning", "PUT", "/planning/daily-budget", null },
        { "planning", "POST", "/planning/examples", null },
    };

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // With one call a minute the second gets turned away, unless the endpoint has no limit. Each case its own account,
    // and the call reaches its service without the prefix, so the specific route is the one that took it.
    [Theory]
    [MemberData(nameof(WriteEndpoints))]
    public async Task Endpoint_HasItsLimit_AndReachesItsService(string service, string method, string path, string? policy)
    {
        await using var stub = await StubService.StartAsync($"{service}-stub");
        await using var gateway = Gateway(stub, permits: 1);
        using var client = ClientFor(gateway, $"user-{Guid.NewGuid():N}");

        using var first = await SendAsync(client, method, path);
        using var second = await SendAsync(client, method, path);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var echo = await first.Content.ReadFromJsonAsync<Echo>(Cancel);
        Assert.Equal((method, path[(service.Length + 1)..]), (echo!.Method, echo.Path));
        Assert.Equal(policy is null ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task OverTheLimit_IsAProblemWithRetryAfter_OnlyForThatAccountAndKind()
    {
        await using var stub = await StubService.StartAsync("assessment-stub");
        await using var gateway = Gateway(stub, permits: 2);
        using var alice = ClientFor(gateway, $"alice-{Guid.NewGuid():N}");
        using var bob = ClientFor(gateway, $"bob-{Guid.NewGuid():N}");

        for (var call = 0; call < 2; call++)
        {
            using var allowed = await SendAsync(alice, "POST", "/assessment/attempts");
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var refused = await SendAsync(alice, "POST", $"/assessment/attempts/{Guid.Empty}/answer");
        Assert.Equal(2, stub.Requests);
        using var other = await SendAsync(bob, "POST", "/assessment/attempts");
        using var session = await SendAsync(alice, "POST", "/planning/sessions");
        using var reading = await SendAsync(alice, "GET", "/assessment/question-pools");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);
        var problem = await refused.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Cancel);
        Assert.Equal((429, AccountRateLimits.TooMany), (problem.GetProperty("status").GetInt32(), problem.GetProperty("title").GetString()));
        Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.InRange(refused.Headers.RetryAfter?.Delta ?? TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, session.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reading.StatusCode);
    }

    // With the limits of appsettings.json: a session with a 20 question quiz, the 20 answers sent again at once after the
    // connection was lost, three summaries, a new document and a retry, all within a minute.
    [Fact]
    public async Task NormalSession_AndAnOfflineQuizSentAtOnce_StayBelowTheLimits()
    {
        await using var stub = await StubService.StartAsync("stub");
        await using var gateway = Gateway(stub, permits: null);
        using var client = ClientFor(gateway, $"alice-{Guid.NewGuid():N}");
        var calls = new List<(string Method, string Path)> { ("POST", "/planning/sessions"), ("POST", "/assessment/check-questions") };
        calls.Add(("POST", "/assessment/quizzes"));
        for (var question = 0; question < 20; question++)
        {
            calls.Add(("POST", "/assessment/attempts"));
            calls.Add(("POST", $"/assessment/attempts/{Guid.NewGuid()}/answer"));
        }

        calls.AddRange(Enumerable.Repeat(("POST", $"/assessment/attempts/{Guid.Empty}/answer"), 20));
        calls.AddRange(Enumerable.Repeat(("POST", $"/planning/sessions/{Guid.Empty}/advance"), 5));
        calls.Add(("POST", $"/planning/sessions/{Guid.Empty}/complete"));
        calls.AddRange(new[] { "short", "standard", "detailed" }.Select(length => ("POST", $"/content/topics/{Guid.Empty}/summaries/{length}")));
        calls.Add(("POST", "/content/documents"));
        calls.Add(("POST", $"/content/documents/{Guid.Empty}/retry"));

        foreach (var (method, path) in calls)
        {
            using var response = await SendAsync(client, method, path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{method} {path}: {response.StatusCode}");
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void LimitBelowOne_StopsTheStart(string permits)
    {
        using var gateway = new GatewayFactory(
            "http://127.0.0.1:9", configure: builder => builder.UseSetting("RateLimits:answers:PermitsPerMinute", permits));

        var failure = StartFailure.Of<OptionsValidationException>(() => gateway.CreateClient());

        Assert.Contains("RateLimits", failure.Message, StringComparison.Ordinal);
    }

    // The table above has to know every endpoint that changes something in these services.
    [Fact]
    public void WriteEndpoints_AreAllListed()
    {
        var root = RepositoryRoot();
        foreach (var service in new[] { "content", "assessment", "planning" })
        {
            var name = char.ToUpperInvariant(service[0]) + service[1..];
            var api = Path.Combine(root, "src", "Services", name, $"Edvaniq.Services.{name}.Api");
            var mapped = Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories)
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Sum(file => WriteMapping().Count(File.ReadAllText(file)));
            var listed = WriteEndpoints.Count(row => row.Data.Item1 == service);

            Assert.True(mapped == listed, $"{name} maps {mapped} endpoints that change something, the table lists {listed}. Give the new one a limit or say why not.");
        }
    }

    private static GatewayFactory Gateway(StubService stub, int? permits) => new(
        stub.Address,
        services: new Dictionary<string, string> { ["content"] = stub.Address, ["assessment"] = stub.Address },
        configure: builder =>
        {
            if (permits is { } limit)
            {
                foreach (var policy in AccountRateLimits.Policies)
                {
                    builder.UseSetting($"RateLimits:{policy}:PermitsPerMinute", limit.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        });

    private static HttpClient ClientFor(GatewayFactory gateway, string user)
    {
        var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        return client;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path) =>
        client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Cancel);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "Edvaniq.slnx")))
        {
            directory = directory.Parent ?? throw new InvalidOperationException("Edvaniq.slnx not found.");
        }

        return directory.FullName;
    }

    [GeneratedRegex(@"\.Map(Post|Put|Patch|Delete)\(")]
    private static partial Regex WriteMapping();
}