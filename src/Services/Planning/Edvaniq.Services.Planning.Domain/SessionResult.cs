namespace Edvaniq.Services.Planning.Domain;

// One answer given on a session's block, as Assessment stored it (#403). Correct false also for a shown solution.
public sealed record SessionAnswer(Guid AnswerId, Guid TopicId, bool Correct, DateTime AnsweredAt);

// Where a topic stood at the start and at the end, as LearningEngine answered. Before null: LearningEngine can't tell any
// more. Introduced: LearningEngine has the topic's introduction.
public sealed record TopicChange(Guid TopicId, TopicStanding? Before, TopicStanding? After, bool Introduced);

// MissingAnswers: how many of the answers asked about have no learning record at LearningEngine yet.
public sealed record MasteryChanges(IReadOnlyList<TopicChange> Topics, int MissingAnswers);

// From and To, UTC: the moments the mastery is compared at.
public sealed record SessionWindow(DateTime From, DateTime To);

// What a session brought (#403). It counts for the session's block up to the session: a block broken off and started
// again is one quiz (BlockQuiz in Assessment), so the answers of the sessions broken off before count too, the partial
// result (Teilwertung, docs/spec/modes.md). Counts come from Assessment, where the answers are stored, and are always
// right. The mastery comes from LearningEngine, which hears of the answers later: until it has all of them, and the
// introduction a session made, Complete is false and there is no mastery, never an old one (#406).
public sealed record SessionResult(int Answered, int Correct, bool Complete, IReadOnlyList<TopicChange>? Topics)
{
    public int Wrong => Answered - Correct;

    // The ended sessions of the block up to and including this one, the first first.
    public static IReadOnlyList<Session> ChainOf(Session session, IEnumerable<Session> sessionsOnBlock)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(sessionsOnBlock);

        return
        [
            .. sessionsOnBlock
                .Where(other => other.BlockId == session.BlockId && !other.IsActive && other.StartedAt <= session.StartedAt && other.Id != session.Id)
                .Append(session)
                .OrderBy(other => other.StartedAt)
                .ThenBy(other => other.Id),
        ];
    }

    // From the start of the first session to the end of the last, or to the last answer if one came later (a client
    // that sent it again after the end).
    public static SessionWindow WindowOf(IReadOnlyList<Session> chain, IReadOnlyList<SessionAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(answers);
        if (chain.Count == 0 || chain.Any(session => session.EndedAt is null))
        {
            throw new ArgumentException("Only ended sessions have a result.", nameof(chain));
        }

        var to = chain.Max(session => session.EndedAt!.Value);
        if (answers.Count > 0 && answers.Max(answer => answer.AnsweredAt) is var last && last > to)
        {
            to = last;
        }

        return new SessionWindow(chain.Min(session => session.StartedAt), to);
    }

    // The block's topic first, then any other an answer was on.
    public static IReadOnlyList<Guid> TopicsOf(IReadOnlyList<Session> chain, IReadOnlyList<SessionAnswer> answers) =>
        [.. chain.Select(session => session.TopicId).Concat(answers.Select(answer => answer.TopicId)).Distinct()];

    // Changes null: LearningEngine couldn't be asked.
    public static SessionResult Of(IReadOnlyList<Session> chain, IReadOnlyList<SessionAnswer> answers, MasteryChanges? changes)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(answers);

        var correct = answers.Count(answer => answer.Correct);
        if (changes is null || changes.MissingAnswers > 0)
        {
            return new SessionResult(answers.Count, correct, false, null);
        }

        var byTopic = changes.Topics.ToDictionary(topic => topic.TopicId);
        var topics = TopicsOf(chain, answers).Select(id => byTopic.GetValueOrDefault(id)).ToList();
        var introduced = chain.SelectMany(session => session.IntroducedTopicIds()).ToHashSet();
        var complete = topics.All(topic => topic?.After is not null && (topic.Introduced || !introduced.Contains(topic.TopicId)));
        return complete
            ? new SessionResult(answers.Count, correct, true, [.. topics.Select(topic => topic!)])
            : new SessionResult(answers.Count, correct, false, null);
    }
}