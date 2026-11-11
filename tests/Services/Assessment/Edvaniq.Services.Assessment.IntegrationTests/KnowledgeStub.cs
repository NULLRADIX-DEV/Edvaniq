using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Edvaniq.Services.Assessment.IntegrationTests;

// Knowledge's GET /topics/{id} as the service sees it over HTTP. A topic belongs to the token it was added with, so a
// topic only comes back when the service hands on the caller's own token.
public sealed class KnowledgeStub
{
    private readonly ConcurrentDictionary<(string Token, Guid TopicId), (Guid? DocumentId, int Position)> topics = new();

    public bool Down { get; set; }

    public ConcurrentQueue<string?> Authorizations { get; } = new();

    public void Add(string token, Guid topicId, Guid? documentId, int position) => topics[(token, topicId)] = (documentId, position);

    public HttpMessageHandler Handler() => new StubHandler(this);

    private sealed class StubHandler(KnowledgeStub knowledge) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var authorization = request.Headers.Authorization;
            knowledge.Authorizations.Enqueue(authorization?.ToString());
            if (knowledge.Down)
            {
                throw new HttpRequestException("Connection refused (knowledge-api:8080)");
            }

            var segments = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
            if (request.Method == HttpMethod.Get
                && segments is ["topics", var id]
                && Guid.TryParse(id, out var topicId)
                && authorization is { Scheme: "Bearer", Parameter: { } token }
                && knowledge.topics.TryGetValue((token, topicId), out var source))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(
                        new { id = topicId, learningGoalId = Guid.Empty, parentId = (Guid?)null, name = "Thema", source.DocumentId, source.Position },
                        options: JsonSerializerOptions.Web),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}