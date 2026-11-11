using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Assessment.Application;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Assessment.Infrastructure;

// GET /topics/{id} of Knowledge, a query by rule 4. The Api hands this client the caller's token, so Knowledge's owner
// filter decides whose topic it is. The response type is ours, nothing of Knowledge is referenced (rule 6).
public sealed partial class KnowledgeTopics(HttpClient http, ILogger<KnowledgeTopics> logger) : ITopicSource
{
    public static readonly Uri Address = new("https+http://knowledge-api");

    public async Task<TopicSource?> FindAsync(Guid topicId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(new Uri($"topics/{topicId}", UriKind.Relative), cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            var topic = await response.Content.ReadFromJsonAsync<KnowledgeTopic>(JsonSerializerOptions.Web, cancellationToken)
                ?? throw new InvalidDataException("Knowledge answered without a topic.");
            return new(topic.DocumentId, topic.Position);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The type only: a message could name the address.
            LogUnavailable(topicId, exception.GetType().Name);
            throw new TopicSourceUnavailableException(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Knowledge could not be asked for topic {TopicId} ({Error})")]
    private partial void LogUnavailable(Guid topicId, string error);

    private sealed record KnowledgeTopic(Guid? DocumentId, int Position);
}