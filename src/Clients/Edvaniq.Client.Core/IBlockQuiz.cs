namespace Edvaniq.Client.Core;

// The quiz of a block of the plan from Assessment, through the web host. Each question is an attempt answered through
// IQuiz like any other.
public interface IBlockQuiz
{
    // Makes the quiz on the first call and brings the same questions in the same order on every further one. Size only
    // counts when the quiz is made. NotReady while the topic has no questions yet (or the block already has a quiz of
    // another topic), NotFound for a topic the user doesn't have.
    Task<CallOutcome<BlockQuiz>> OpenAsync(Guid topicId, Guid blockId, int size, CancellationToken cancellationToken = default);
}

// PoolTooSmall: the topic had too few questions, so the quiz repeats one or has fewer than asked for.
public sealed record BlockQuiz(Guid Id, Guid BlockId, Guid TopicId, IReadOnlyList<BlockQuizQuestion> Questions, bool PoolTooSmall);

// A question as POST /attempts gives it. Answered: the user gave an answer already, Correct is how it was graded.
public sealed record BlockQuizQuestion(
    Guid Id,
    Guid TopicId,
    Guid QuestionId,
    string Text,
    IReadOnlyList<string> Options,
    int Difficulty,
    bool Answered,
    bool? Correct,
    string? Type = null,
    QuestionSource? Source = null)
{
    public AskedQuestion Asked => new(Id, TopicId, QuestionId, Text, Options, Difficulty, Type, Source);
}