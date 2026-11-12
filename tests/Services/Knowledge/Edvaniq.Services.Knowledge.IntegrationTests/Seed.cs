using Edvaniq.Services.Knowledge.Domain;
using Edvaniq.Services.Knowledge.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Knowledge.IntegrationTests;

// Topics come from the analysis of the material, the API has no way to add one. The tests put them into the database.
internal static class Seed
{
    public static readonly DateTime Now = new(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);

    public static async Task AddAsync(this ServiceFactory service, params Topic[] topics)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        db.Topics.AddRange(topics);
        db.TopicSets.AddRange(topics.Select(topic => (topic.OwnerId, topic.LearningGoalId)).Distinct()
            .Select(goal => TopicSet.Of(goal.OwnerId, goal.LearningGoalId, Now)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public static async Task<Topic> AddTopicAsync(this ServiceFactory service, string owner, string name)
    {
        var topic = Topic.Create(owner, Guid.NewGuid(), name, Now);
        await service.AddAsync(topic);
        return topic;
    }
}