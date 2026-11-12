namespace Edvaniq.Services.Planning.Domain;

// One step of a session (docs/spec/glossary.md#session). Empty: nothing to do in it, the client skips it (A60).
public sealed record SessionStep(SessionStepKind Kind, bool Empty = false);

public enum SessionStepKind
{
    Review,
    IntroductionUnit,
    Quiz,
}

// The fixed order of a session's steps (#377): review first, the introduction unit only for a learn block of a topic
// not introduced yet, then the quiz. A pure function.
public static class SessionSteps
{
    public static IReadOnlyList<SessionStep> For(BlockKind kind, bool topicIsNew)
    {
        // The review has no content before cards and wrong answers come in (M4).
        var steps = new List<SessionStep> { new(SessionStepKind.Review, Empty: true) };
        if (kind == BlockKind.Learn && topicIsNew)
        {
            steps.Add(new SessionStep(SessionStepKind.IntroductionUnit));
        }

        steps.Add(new SessionStep(SessionStepKind.Quiz));
        return steps;
    }
}