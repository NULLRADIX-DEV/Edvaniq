using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Assessment.Domain;

// The fixed questions of the quiz of one block of Planning's plan (#393). Made on the first call and the same on every
// later one, so a session broken off and started again on the block only gets the questions not answered yet. Its
// questions are attempts with this QuizId. BlockId is only a reference, Assessment never checks it: with the owner it
// is the key, so someone else's block id only ever gives the caller their own quiz.
public sealed class BlockQuiz : IOwnedByUser
{
    private BlockQuiz(Guid id, string ownerId, Guid blockId, Guid topicId, DateTime createdAt)
    {
        Id = id;
        OwnerId = ownerId;
        BlockId = blockId;
        TopicId = topicId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string OwnerId { get; private set; }

    public Guid BlockId { get; private set; }

    public Guid TopicId { get; private set; }

    // UTC.
    public DateTime CreatedAt { get; private set; }

    // Whether the pool was too small when the quiz was made (#387): it repeats a question answered recently or has fewer
    // questions than asked for. Null for quizzes from before.
    public bool? PoolTooSmall { get; private set; }

    public static BlockQuiz Create(string ownerId, Guid blockId, Guid topicId, DateTime createdAt, bool poolTooSmall = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);
        return new BlockQuiz(Guid.CreateVersion7(createdAt), ownerId, blockId, topicId, createdAt) { PoolTooSmall = poolTooSmall };
    }
}