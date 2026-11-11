using System.Text.Json;

using Edvaniq.BuildingBlocks.Contracts;
using Edvaniq.Services.Knowledge.Contracts;

namespace Edvaniq.Services.Knowledge.UnitTests;

// TopicsChanged as it lies on the broker. Parents came with #909, messages from before must stay readable.
public class TopicsChangedContractTests
{
    [Fact]
    public void MessageFromBeforeParents_IsReadWithoutParents()
    {
        const string json = """
            {"userId":"alice","learningGoalId":"6f1c2a1e-0000-4000-8000-000000000001","changedAt":"2026-11-11T09:00:00Z",
             "topicIds":["6f1c2a1e-0000-4000-8000-000000000002"],"hiddenTopicIds":[]}
            """;

        var changed = JsonSerializer.Deserialize<TopicsChanged>(json, EventJson.Options)!;

        Assert.Null(changed.Parents);
        Assert.Equal([new Guid("6f1c2a1e-0000-4000-8000-000000000002")], changed.TopicIds);
    }

    [Fact]
    public void Parents_AreWrittenAndReadBack()
    {
        var (parent, child) = (Guid.NewGuid(), Guid.NewGuid());
        var changed = new TopicsChanged("alice", Guid.NewGuid(), DateTime.UnixEpoch, [parent, child], [], [new(child, parent)]);

        var json = JsonSerializer.Serialize(changed, EventJson.Options);
        var read = JsonSerializer.Deserialize<TopicsChanged>(json, EventJson.Options)!;

        Assert.Contains("\"parents\":[{\"topicId\":", json, StringComparison.Ordinal);
        Assert.Equal([new TopicParent(child, parent)], read.Parents);
    }
}