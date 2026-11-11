using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Edvaniq.Services.Planning.IntegrationTests;

// Assessment's GET /question-pools?learningGoalId= as the service sees it over HTTP. A pool belongs to the token it was
// set with, so it only comes back when the service hands on the caller's own token. Every request is recorded with
// the token it came with.
public sealed class AssessmentStub(OutgoingCalls calls)
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, string>> pools = new();
    private readonly ConcurrentDictionary<string, HttpStatusCode?> failures = new();

    public ConcurrentQueue<(string? Token, string PathAndQuery)> Requests { get; } = new();

    public void Set(string token, Guid topicId, string status = "ready") =>
        pools.GetOrAdd(token, _ => new())[topicId] = status;

    // Requests with this token can't connect at all.
    public void Unreachable(string token) => failures[token] = null;

    public void Fail(string token, HttpStatusCode status) => failures[token] = status;

    public IReadOnlyList<(string? Token, string PathAndQuery)> RequestsWith(string token) =>
        [.. Requests.Where(request => request.Token == token)];

    public HttpMessageHandler Handler() => new StubHandler(this, calls);

    private sealed class StubHandler(AssessmentStub assessment, OutgoingCalls calls) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = request.Headers.Authorization is { Scheme: "Bearer", Parameter: { } parameter } ? parameter : null;
            assessment.Requests.Enqueue((token, request.RequestUri!.PathAndQuery));
            using var open = await calls.EnterAsync(token, cancellationToken);

            if (token is not null && assessment.failures.TryGetValue(token, out var failure))
            {
                return failure is { } status
                    ? new HttpResponseMessage(status) { Content = new StringContent("boom", Encoding.UTF8, "text/plain") }
                    : throw new HttpRequestException("Connection refused (assessment-api:8080)");
            }

            if (request.Method != HttpMethod.Get || request.RequestUri.AbsolutePath != "/question-pools" || token is null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            // Shaped like Assessment's answer, the count only for show: Planning reads the status.
            var topics = assessment.pools.TryGetValue(token, out var known)
                ? known.Select(topic => new { topicId = topic.Key, questions = topic.Value == "ready" ? 15 : 0, status = topic.Value }).ToArray()
                : [];
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { minQuestions = 2, topics }, options: JsonSerializerOptions.Web),
            };
        }
    }
}