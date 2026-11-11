using Edvaniq.BuildingBlocks.Web;
using Edvaniq.Services.Knowledge.Application;

namespace Edvaniq.Services.Knowledge.Api;

// Which ids Knowledge gave the topics of an analysis, for Content's worker (rule 6: Content asks, it doesn't listen).
// Only Content's own internal token gets in, no user and no other client. A document belongs to one user, so the answer
// holds only that user's topics, and only what Content knows anyway plus the ids: no names. No gateway route leads here.
internal static class DocumentTopicEndpoints
{
    public const string ContentClient = "content";

    public static IEndpointRouteBuilder MapDocumentTopicEndpoints(this IEndpointRouteBuilder app)
    {
        // An empty list means Knowledge has no topics of the document (yet).
        app.MapGet("/documents/{documentId:guid}/topics", async (Guid documentId, IAnalyzedTopics topics, CancellationToken cancellationToken) =>
                (await topics.OfDocumentAsync(documentId, cancellationToken))
                .Select(topic => new DocumentTopicResponse(topic.Id, topic.ParentId, topic.Position, topic.SectionIds)))
            .RequireInternalClient(ContentClient);

        return app;
    }
}

internal sealed record DocumentTopicResponse(Guid Id, Guid? ParentId, int Position, IReadOnlyList<Guid> SectionIds);