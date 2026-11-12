using System.Collections.Concurrent;

using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Application;

namespace Edvaniq.Services.Content.IntegrationTests;

// Answers the Api's question for the sections of a topic in place of Knowledge (#344). Like Knowledge it answers only
// the owner of the topic, anyone else gets nothing as for a topic that doesn't exist.
public sealed class TopicMaterialStub
{
    private readonly ConcurrentDictionary<Guid, (string Owner, IReadOnlyList<(Guid DocumentId, Guid SectionId)> Sections)> topics = new();
    private readonly ConcurrentDictionary<Guid, bool> down = new();

    public ConcurrentQueue<Guid> Asked { get; } = new();

    public void Give(string owner, Guid topicId, IReadOnlyList<(Guid DocumentId, Guid SectionId)> sections) => topics[topicId] = (owner, sections);

    public void Down(Guid topicId) => down[topicId] = true;

    internal ITopicMaterial For(ICurrentUser user) => new Caller(this, user);

    private sealed class Caller(TopicMaterialStub stub, ICurrentUser user) : ITopicMaterial
    {
        public Task<IReadOnlyList<(Guid DocumentId, Guid SectionId)>?> SectionsAsync(Guid topicId, CancellationToken cancellationToken)
        {
            stub.Asked.Enqueue(topicId);
            if (stub.down.ContainsKey(topicId))
            {
                throw new TopicMaterialUnavailableException(new HttpRequestException("Knowledge is down."));
            }

            return Task.FromResult(stub.topics.TryGetValue(topicId, out var topic) && topic.Owner == user.Id ? topic.Sections : null);
        }
    }
}