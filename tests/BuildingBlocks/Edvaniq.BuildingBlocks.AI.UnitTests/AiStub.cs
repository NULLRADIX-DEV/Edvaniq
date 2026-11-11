using System.Net;
using System.Security.Cryptography;
using System.Text;

using Edvaniq.Testing;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edvaniq.BuildingBlocks.AI.UnitTests;

// Stands in for a provider with an OpenAI-compatible API, so no test ever reaches a real one. Answers every request
// with the next answer of the test and remembers what it got.
internal sealed class AiStub : HttpMessageHandler
{
    // New for every run, a fixed one in the repo would look like a leaked key.
    public static readonly string Key = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    public const string Endpoint = "https://ai.test/api/v1";

    private readonly Queue<Func<HttpResponseMessage>> answers = new();

    public List<(string? Authorization, Uri? Uri, string Body)> Requests { get; } = [];

    public static string Completion(string text, string model = "vendor/model-1") =>
        $$$"""
        {"id":"gen-1","model":"{{{model}}}","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"{{{text}}}"}}],
         "usage":{"prompt_tokens":12,"completion_tokens":3,"total_tokens":15}}
        """;

    public AiStub Answer(HttpStatusCode status, string body, TimeSpan? retryAfter = null)
    {
        answers.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (retryAfter is { } wait)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(wait);
            }

            return response;
        });
        return this;
    }

    public AiStub Hang()
    {
        answers.Enqueue(() => throw new InvalidOperationException("The stub hangs, the caller must give up first."));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Headers.Authorization?.ToString(), request.RequestUri, body));
        var answer = answers.Count > 0 ? answers.Dequeue() : () => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        try
        {
            return answer();
        }
        catch (InvalidOperationException)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw;
        }
    }

    // A host as on the server: standard resilience on every HttpClient, the log at Information.
    public IHost Host(TestLogs? logs = null, params (string Key, string? Value)[] overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Ai:Provider"] = "OpenAiCompatible",
            ["Ai:OpenAiCompatible:Endpoint"] = Endpoint,
            ["Ai:OpenAiCompatible:Model"] = "vendor/model-1",
            ["Ai:OpenAiCompatible:ApiKey"] = Key,
            ["Ai:OpenAiCompatible:ExtraBody"] = """{"provider":{"only":["one"],"zdr":true,"data_collection":"deny"}}""",
        };
        foreach (var (key, value) in overrides)
        {
            settings[key] = value;
        }

        var builder = Microsoft.Extensions.Hosting.Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        if (logs is not null)
        {
            builder.Logging.AddProvider(logs);
        }

        builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        builder.AddAi();
        builder.Services.AddHttpClient("ai").ConfigurePrimaryHttpMessageHandler(() => this);
        return builder.Build();
    }

    public static IChatClient Client(IHost host) => host.Services.GetRequiredService<IChatClient>();
}