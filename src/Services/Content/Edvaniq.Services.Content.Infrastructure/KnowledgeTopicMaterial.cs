using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Content.Application;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Content.Infrastructure;

// GET /topics/{id}/sections of Knowledge (#294). The Api hands this client the caller's token, so Knowledge answers only
// for that user's topics. The response type is ours, nothing of Knowledge is referenced (rule 6).
public sealed partial class KnowledgeTopicMaterial(HttpClient http, ILogger<KnowledgeTopicMaterial> logger) : ITopicMaterial
{
    public async Task<IReadOnlyList<(Guid DocumentId, Guid SectionId)>?> SectionsAsync(Guid topicId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(new Uri($"topics/{topicId}/sections", UriKind.Relative), cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            var sections = await response.Content.ReadFromJsonAsync<List<Section>>(JsonSerializerOptions.Web, cancellationToken)
                ?? throw new InvalidDataException("Knowledge answered without sections.");
            return [.. sections.Select(section => (section.DocumentId, section.SectionId))];
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The type only: a message could name the address.
            LogUnavailable(exception.GetType().Name);
            throw new TopicMaterialUnavailableException(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Knowledge could not be asked for the sections of a topic ({Error})")]
    private partial void LogUnavailable(string error);

    private sealed record Section(Guid DocumentId, Guid SectionId);
}