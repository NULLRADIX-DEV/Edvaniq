using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Content.Application;

namespace Edvaniq.Services.Content.Infrastructure;

// GET /documents/{id}/topics of Knowledge. The worker registers the client with its own token for Knowledge's internal
// endpoint. The response type is ours, nothing of Knowledge is referenced (rule 6).
public sealed class KnowledgeTopics(HttpClient http) : IKnowledgeTopics
{
    public static readonly Uri Address = new("https+http://knowledge-api");

    public async Task<IReadOnlyList<KnowledgeTopic>> OfDocumentAsync(Guid documentId, CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<List<KnowledgeTopic>>(
            new Uri($"documents/{documentId}/topics", UriKind.Relative), JsonSerializerOptions.Web, cancellationToken)
        ?? throw new InvalidDataException("Knowledge answered without topics.");
}