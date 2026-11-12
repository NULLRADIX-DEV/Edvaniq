using Edvaniq.Services.Assessment.Domain;

namespace Edvaniq.Services.Assessment.Application;

// The one way the quiz picks questions (#387), for a single question (POST /attempts), a block's quiz (POST /quizzes)
// and the quiz of the weaknesses. Questions the user never answered come first, then those not answered recently, and
// one answered recently only when nothing else is left, the oldest answer first. The difficulty is mixed: the first
// two groups take turns over the difficulties. A quiz that had to repeat a recent question or has fewer questions than
// asked for says so.
public static class QuestionSelection
{
    public static readonly TimeSpan Recent = TimeSpan.FromDays(7);

    // Up to count questions of the pool, none twice. lastAnswered: when the user last answered a question, if ever.
    public static Selection Pick(
        IReadOnlyList<Question> pool, IReadOnlyDictionary<Guid, DateTime> lastAnswered, int count, DateTime now, Random random)
    {
        ArgumentNullException.ThrowIfNull(pool);
        return PickAcross([pool], lastAnswered, count, now, random);
    }

    // The questions of several topics, the first topic first: the topics take turns, each in the order of Pick. For the
    // weaknesses, severest first.
    public static Selection PickAcross(
        IReadOnlyList<IReadOnlyList<Question>> topics, IReadOnlyDictionary<Guid, DateTime> lastAnswered, int count, DateTime now, Random random)
    {
        ArgumentNullException.ThrowIfNull(topics);
        ArgumentNullException.ThrowIfNull(lastAnswered);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var queues = topics.Select(pool => new Queue<Question>(Ordered(pool, lastAnswered, now, random))).ToList();
        var picked = new List<Question>();
        var seen = new HashSet<Guid>();

        // Fresh questions of all topics before a recent one of any topic.
        foreach (var fresh in new[] { true, false })
        {
            while (picked.Count < count && queues.Any(queue => queue.TryPeek(out var next) && IsRecent(next, lastAnswered, now) != fresh))
            {
                foreach (var queue in queues)
                {
                    if (picked.Count < count && queue.TryPeek(out var next) && IsRecent(next, lastAnswered, now) != fresh)
                    {
                        queue.Dequeue();
                        if (seen.Add(next.Id))
                        {
                            picked.Add(next);
                        }
                    }
                }
            }
        }

        return new(picked, picked.Count < count || picked.Any(question => IsRecent(question, lastAnswered, now)));
    }

    public static bool IsRecent(Question question, IReadOnlyDictionary<Guid, DateTime> lastAnswered, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(lastAnswered);
        return lastAnswered.TryGetValue(question.Id, out var answeredAt) && answeredAt > now - Recent;
    }

    private static IEnumerable<Question> Ordered(IReadOnlyList<Question> pool, IReadOnlyDictionary<Guid, DateTime> lastAnswered, DateTime now, Random random)
    {
        var never = pool.Where(question => !lastAnswered.ContainsKey(question.Id)).ToList();
        var earlier = pool.Where(question => lastAnswered.ContainsKey(question.Id) && !IsRecent(question, lastAnswered, now)).ToList();
        var recent = pool.Where(question => IsRecent(question, lastAnswered, now))
            .OrderBy(question => lastAnswered[question.Id])
            .ThenBy(question => question.Id);
        return Mixed(never, random).Concat(Mixed(earlier, random)).Concat(recent);
    }

    // Shuffled within each difficulty, then the difficulties take turns, the easiest first.
    private static IEnumerable<Question> Mixed(List<Question> questions, Random random)
    {
        var buckets = questions.GroupBy(question => question.Difficulty)
            .OrderBy(bucket => bucket.Key)
            .Select(bucket =>
            {
                var shuffled = bucket.ToArray();
                random.Shuffle(shuffled);
                return new Queue<Question>(shuffled);
            })
            .ToList();
        while (buckets.Any(bucket => bucket.Count > 0))
        {
            foreach (var bucket in buckets.Where(bucket => bucket.Count > 0))
            {
                yield return bucket.Dequeue();
            }
        }
    }
}

// PoolTooSmall: fewer questions than asked for, or one the user answered recently.
public sealed record Selection(IReadOnlyList<Question> Questions, bool PoolTooSmall);