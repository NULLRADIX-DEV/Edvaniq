using System.Collections.Concurrent;

using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Contracts;

namespace Edvaniq.Services.Content.IntegrationTests;

// Answers in place of Knowledge. TakeOver does what Knowledge does with DocumentAnalyzed: one topic with a new id per
// suggestion, the parent by its index. Until then a document has no topics. The tests share it, each with its own
// documents.
public sealed class KnowledgeStub : IKnowledgeTopics
{
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<KnowledgeTopic>> topics = new();
    private readonly ConcurrentDictionary<Guid, bool> down = new();

    public IReadOnlyList<KnowledgeTopic> TakeOver(DocumentAnalyzed analyzed)
    {
        var made = new List<KnowledgeTopic>();
        foreach (var (position, suggestion) in analyzed.Topics.Index())
        {
            made.Add(new(Guid.NewGuid(), suggestion.Parent is { } parent ? made[parent].Id : null, position, suggestion.SectionIds));
        }

        topics[analyzed.DocumentId] = made;
        return made;
    }

    // What Knowledge answers for the document from now on, e.g. with a topic merged from another document.
    public void Give(Guid documentId, IReadOnlyList<KnowledgeTopic> given) => topics[documentId] = given;

    // Knowledge can't be reached for the document.
    public void Down(Guid documentId) => down[documentId] = true;

    public Task<IReadOnlyList<KnowledgeTopic>> OfDocumentAsync(Guid documentId, CancellationToken cancellationToken) =>
        down.ContainsKey(documentId)
            ? throw new HttpRequestException("Knowledge is down.")
            : Task.FromResult(topics.GetValueOrDefault(documentId) ?? []);
}