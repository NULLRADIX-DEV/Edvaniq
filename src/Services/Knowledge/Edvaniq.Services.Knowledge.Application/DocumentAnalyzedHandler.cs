using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Knowledge.Domain;

namespace Edvaniq.Services.Knowledge.Application;

// Takes over the topics of an analysis (docs/spec/events.md) and announces the new state of the learning goal with
// TopicsChanged in the same save. Topics the goal has already keep their id and name and only get the document's
// sections (TopicImport), so analysing a document again, or another one on the same topics, adds no topic twice. The
// inbox drops a redelivered message. Only new or moved topics change the state, sections alone announce nothing.
public sealed class DocumentAnalyzedHandler(IAnalyzedTopics topics, TopicsChangedEvents changes, TimeProvider time)
    : IIntegrationEventHandler<DocumentAnalyzed>
{
    // Topics from before the default goal (#215) belong to none. A message without a goal still lands there.
    public static readonly Guid NoLearningGoal = Guid.Empty;

    public async Task HandleAsync(DocumentAnalyzed integrationEvent, CancellationToken cancellationToken)
    {
        var owner = integrationEvent.UserId;
        var goal = integrationEvent.LearningGoalId ?? NoLearningGoal;

        // The user's topics without a goal join the first one there is. They keep their ids, so questions and records
        // stay with them. Once moved there are none left, a later analysis moves nothing.
        IReadOnlyList<Topic> moved = goal == NoLearningGoal ? [] : await topics.OfGoalAsync(owner, NoLearningGoal, cancellationToken);
        foreach (var topic in moved)
        {
            topic.MoveTo(goal);
        }

        // The query sees the stored goal, the moved topics join by hand.
        var known = (await topics.OfGoalAsync(owner, goal, cancellationToken)).Concat(moved).Distinct().ToList();
        var import = TopicImport.From(integrationEvent, goal, time.GetUtcNow().UtcDateTime, known);
        await topics.ImportAsync(import, cancellationToken);
        if (import.Topics.Count > 0 || moved.Count > 0)
        {
            await changes.AnnounceAsync(owner, goal, cancellationToken);
        }

        if (moved.Count > 0)
        {
            await changes.AnnounceAsync(owner, NoLearningGoal, cancellationToken);
        }
    }
}