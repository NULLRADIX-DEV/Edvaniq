namespace Edvaniq.Services.Planning.Domain;

// Spreads the topics of one learning goal over the study days up to the exam (docs/spec/acceptance.md A22). A pure
// function: no clock, no database, the same input always gives the same plan.
//
// - Weight of a topic = size × (100 - mastery). Size 0 or mastery 100 means no time.
// - Study days run from today up to the day before the exam. The exam day itself is no study day, so the last block
//   lies on the day before it. An exam today or earlier gives an empty plan.
// - All minutes of all study days are shared out by weight in steps of 5 minutes (largest remainder), whatever is left
//   below 5 goes to the heaviest topic. So every day is full, and more weight never means fewer minutes.
// - Day by day, each block goes to the topic with the largest share of its minutes still open, so topics take turns.
//   A block is at most BlockMinutes long.
// - Ties always go by weight, then by topic id.
//
// Known limit: a budget that isn't a multiple of 5 minutes can leave blocks of 1 to 4 minutes. A minimum block size
// belongs to #369.
public static class StudyPlanner
{
    public const int BlockMinutes = 15;

    public const int Step = 5;

    public static StudyPlan Plan(IReadOnlyCollection<TopicToPlan> topics, DailyBudget budget, DateOnly today, DateOnly examDay)
    {
        ArgumentNullException.ThrowIfNull(topics);
        ArgumentNullException.ThrowIfNull(budget);
        Validate(topics);

        var weighted = topics
            .Select(topic => (topic.TopicId, Weight: (long)topic.Size * (100 - topic.Mastery)))
            .Where(topic => topic.Weight > 0)
            .OrderByDescending(topic => topic.Weight)
            .ThenBy(topic => topic.TopicId)
            .ToList();

        var days = new List<DateOnly>();
        for (var day = today; day < examDay; day = day.AddDays(1))
        {
            days.Add(day);
        }

        if (weighted.Count == 0 || days.Count == 0)
        {
            return StudyPlan.Empty;
        }

        var total = days.Sum(day => (long)budget.MinutesOn(day));
        var quotas = Share(total, weighted.Select(topic => topic.Weight).ToList());
        return Fill(days, budget, weighted.Select(topic => topic.TopicId).ToList(), quotas);
    }

    private static void Validate(IReadOnlyCollection<TopicToPlan> topics)
    {
        var ids = new HashSet<Guid>();
        foreach (var topic in topics)
        {
            ArgumentNullException.ThrowIfNull(topic, nameof(topics));
            ArgumentOutOfRangeException.ThrowIfNegative(topic.Size, nameof(topics));
            ArgumentOutOfRangeException.ThrowIfNegative(topic.Mastery, nameof(topics));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(topic.Mastery, 100, nameof(topics));
            if (!ids.Add(topic.TopicId))
            {
                throw new ArgumentException($"Topic {topic.TopicId} is in the list twice.", nameof(topics));
            }
        }
    }

    // Weights come sorted, heaviest first.
    private static long[] Share(long total, List<long> weights)
    {
        var steps = total / Step;
        var sum = weights.Aggregate(Int128.Zero, (acc, weight) => acc + weight);
        var quotas = new long[weights.Count];
        var remainders = new Int128[weights.Count];
        for (var i = 0; i < weights.Count; i++)
        {
            var exact = (Int128)steps * weights[i];
            quotas[i] = (long)(exact / sum);
            remainders[i] = exact % sum;
        }

        var left = steps - quotas.Sum();
        var byRemainder = Enumerable.Range(0, weights.Count).OrderByDescending(i => remainders[i]).ThenBy(i => i);
        foreach (var i in byRemainder.Take((int)left))
        {
            quotas[i]++;
        }

        for (var i = 0; i < quotas.Length; i++)
        {
            quotas[i] *= Step;
        }

        quotas[0] += total % Step;
        return quotas;
    }

    private static StudyPlan Fill(List<DateOnly> days, DailyBudget budget, List<Guid> topicIds, long[] quotas)
    {
        var open = quotas.ToArray();
        var plan = new List<DayPlan>();
        foreach (var day in days)
        {
            var dayLeft = (long)budget.MinutesOn(day);
            var blocks = new List<Block>();
            while (dayLeft > 0)
            {
                var next = MostOpen(open, quotas);
                if (next < 0)
                {
                    break;
                }

                var minutes = Math.Min(BlockMinutes, Math.Min(open[next], dayLeft));
                blocks.Add(new Block(topicIds[next], (int)minutes));
                open[next] -= minutes;
                dayLeft -= minutes;
            }

            if (blocks.Count > 0)
            {
                plan.Add(new DayPlan(day, blocks));
            }
        }

        return new StudyPlan(plan);
    }

    // The topic with the largest open share open/quota, the earlier one on a tie. -1 when nothing is open.
    private static int MostOpen(long[] open, long[] quotas)
    {
        var best = -1;
        for (var i = 0; i < open.Length; i++)
        {
            if (open[i] == 0)
            {
                continue;
            }

            if (best < 0 || (Int128)open[i] * quotas[best] > (Int128)open[best] * quotas[i])
            {
                best = i;
            }
        }

        return best;
    }
}