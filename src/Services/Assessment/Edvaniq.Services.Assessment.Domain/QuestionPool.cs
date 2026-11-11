namespace Edvaniq.Services.Assessment.Domain;

// The questions Assessment holds for one of the user's topics (docs/spec/glossary.md#fragenpool), not Content's record of
// which topics got questions. The one rule for when a topic's pool is ready, for the check questions and the status query.
public static class QuestionPool
{
    // A check needs at least two, so a pool with fewer can't carry the learning unit yet.
    public const int MinQuestions = 2;

    public static bool IsReady(int questions) => questions >= MinQuestions;
}