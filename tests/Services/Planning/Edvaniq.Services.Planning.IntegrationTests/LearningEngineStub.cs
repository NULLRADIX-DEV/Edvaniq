using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Edvaniq.Services.Planning.IntegrationTests;

// LearningEngine's GET /mastery?learningGoalId= as the service sees it over HTTP. Mastery belongs to the token it was
// set with, so it only comes back when the service hands on the caller's own token. Every request is recorded with
// the token it came with.
public sealed class LearningEngineStub
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, int>> masteries = new();
    private readonly ConcurrentDictionary<string, Failure> failures = new();

    public ConcurrentQueue<(string? Token, string PathAndQuery)> Requests { get; } = new();

    public void Set(string token, Guid topicId, int mastery) =>
        masteries.GetOrAdd(token, _ => new())[topicId] = mastery;

    // Requests with this token can't connect at all.
    public void Unreachable(string token) => failures[token] = new Failure(null, "");

    // Requests with this token get this status and body.
    public void Fail(string token, HttpStatusCode status, string body) => failures[token] = new Failure(status, body);

    public IReadOnlyList<(string? Token, string PathAndQuery)> RequestsWith(string token) =>
        [.. Requests.Where(request => request.Token == token)];

    public HttpMessageHandler Handler() => new StubHandler(this);

    private sealed record Failure(HttpStatusCode? Status, string Body);

    private sealed class StubHandler(LearningEngineStub learningEngine) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = request.Headers.Authorization is { Scheme: "Bearer", Parameter: { } parameter } ? parameter : null;
            learningEngine.Requests.Enqueue((token, request.RequestUri!.PathAndQuery));

            if (token is not null && learningEngine.failures.TryGetValue(token, out var failure))
            {
                if (failure.Status is not { } status)
                {
                    throw new HttpRequestException("Connection refused (learningengine-api:8080)");
                }

                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(failure.Body, Encoding.UTF8, "text/plain"),
                });
            }

            if (request.Method != HttpMethod.Get || request.RequestUri.AbsolutePath != "/mastery" || token is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var topics = learningEngine.masteries.TryGetValue(token, out var known)
                ? known.Select(topic => new { topicId = topic.Key, mastery = topic.Value }).ToArray()
                : [];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { ruleVersion = 1, topics }, options: JsonSerializerOptions.Web),
            });
        }
    }
}