using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Edvaniq.Services.Assessment.IntegrationTests;

// LearningEngine's GET /mastery/weak-and-secure?learningGoalId= as the service sees it over HTTP. The weaknesses belong
// to the token they were set with, so they only come back when the service hands on the caller's own token.
public sealed class LearningEngineStub
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<Guid>> weaknesses = new();
    private readonly ConcurrentDictionary<string, bool> down = new();

    public ConcurrentQueue<(string? Token, string PathAndQuery)> Requests { get; } = new();

    // The user's weaknesses, the severest first.
    public void Weaknesses(string token, params Guid[] topicIds) => weaknesses[token] = topicIds;

    // Requests with this token get a 503.
    public void Down(string token) => down[token] = true;

    // A new handler for each client the factory makes, it disposes them on its own.
    public HttpMessageHandler Handler() => new StubHandler(this);

    private Task<HttpResponseMessage> AnswerAsync(HttpRequestMessage request)
    {
        var token = request.Headers.Authorization is { Scheme: "Bearer", Parameter: { } parameter } ? parameter : null;
        Requests.Enqueue((token, request.RequestUri!.PathAndQuery));
        if (token is not null && down.ContainsKey(token))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }

        if (request.Method != HttpMethod.Get || request.RequestUri.AbsolutePath != "/mastery/weak-and-secure" || token is null)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        // Shaped like LearningEngine's answer.
        var topics = weaknesses.TryGetValue(token, out var known) ? known : [];
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(
                new
                {
                    ruleVersion = 3,
                    weaknesses = topics.Select((topicId, index) => new { topicId, mastery = 10 + index, reason = "mastery" }).ToArray(),
                    secure = Array.Empty<object>(),
                },
                options: JsonSerializerOptions.Web),
        });
    }

    private sealed class StubHandler(LearningEngineStub learningEngine) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            learningEngine.AnswerAsync(request);
    }
}