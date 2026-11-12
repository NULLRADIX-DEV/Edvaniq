namespace Edvaniq.Services.Planning.Domain;

// Spreads the topics of one learning goal over the study days up to the exam (docs/spec/acceptance.md A22). A pure
// function: no clock, no database, the same input always gives the same plan.
//
// - Weight of a topic = size × (100 - mastery). Size 0 or mastery 100 means no time.
// - Study days run from today up to the day before the exam. The exam day itself is no study day, so the last block
//   lies on the day before it. An exam today or earlier gives an empty plan.
// - Without an exam (#371) the plan is rolling: RollingDays study days from today and no final phase, as there is nothing
//   to finish towards. A day later the same input gives the same number of days, one day later.
// - Review (#369): every study day starts with one review block for a topic that is already introduced, either from the
//   input (IsNew false) or because it had a learn block on an earlier day of the plan. Topics take turns: fewest review
//   blocks so far, then the heaviest, then by id. Its size is ReviewShareInPercent of the day, see ReviewMinutes. A day
//   with nothing introduced yet has no review block, which can only be the first day when every topic is new.
// - Final phase (#369, only with an exam): the last FinalPhaseDays study days only give learn time to introduced topics, never to a new one.
// - The learn minutes before the final phase are shared out by weight among all topics, those of the final phase among
//   the introduced ones, each in steps of 5 minutes (largest remainder). Whatever is left below 5 goes to the heaviest
//   topic. So every day is full, and within new or introduced topics more weight never means fewer minutes. Review
//   minutes don't count towards these shares.
// - Day by day, each learn block goes to the topic with the largest share of its minutes still open, so topics take
//   turns. A block is at most BlockMinutes long.
// - Ties always go by weight, then by the place in the material (Rank, #380), then by topic id.
// - Short of time (#370): every topic that can get learn time needs at least MinLearnMinutesPerTopic of it in the plan.
//   If the learn minutes of the plan don't cover that, every weakness keeps its place, then the heaviest foundations
//   (#909) and then the heaviest other topics as long as there is room, at least one. The rest is shortened: no learn
//   time in this plan, but an introduced one still takes its turn for review. The kept topics share the learn time by
//   weight as above, so a weakness never gets less than without shortening. New topics only count when the plan has
//   learn minutes before the final phase.
//
// Known limit: on a first day without review a budget that isn't a multiple of 5 ends with a block of 1 to 4 minutes
// for the heaviest topic. On every other day the review block takes up those minutes.
public static class StudyPlanner
{
    public const int BlockMinutes = 15;

    // Less learn time than one full block per topic means the time is too short, see #370.
    public const int MinLearnMinutesPerTopic = BlockMinutes;

    public const int Step = 5;

    public const int ReviewShareInPercent = 20;

    public const int MaxReviewMinutes = 10;

    public const int FinalPhaseShareInPercent = 20;

    public const int MinFinalPhaseDays = 2;

    // A learning goal without an exam is planned for this many study days from today, see #371.
    public const int RollingDays = 7;

    // examDay null: no exam, so a rolling plan over RollingDays.
    public static StudyPlan Plan(IReadOnlyCollection<TopicToPlan> topics, DailyBudget budget, DateOnly today, DateOnly? examDay)
    {
        ArgumentNullException.ThrowIfNull(topics);
        ArgumentNullException.ThrowIfNull(budget);
        Validate(topics);

        var weighted = topics
            .Select(topic => new Weighted(topic.TopicId, (long)topic.Size * (100 - topic.Mastery), topic.IsNew, topic.IsWeakness, topic.IsFoundation, topic.Rank))
            .Where(topic => topic.Weight > 0)
            .OrderByDescending(topic => topic.Weight)
            .ThenBy(topic => topic.Rank)
            .ThenBy(topic => topic.TopicId)
            .ToList();

        var lastDay = examDay ?? today.AddDays(RollingDays);
        var days = new List<DateOnly>();
        for (var day = today; day < lastDay; day = day.AddDays(1))
        {
            days.Add(day);
        }

        var finalPhase = examDay is null ? 0 : FinalPhaseDays(days.Count);
        var anyIntroduced = weighted.Any(topic => !topic.IsNew);
        if (weighted.Count == 0 || days.Count == 0 || (!anyIntroduced && finalPhase == days.Count))
        {
            return StudyPlan.Empty;
        }

        // Day 0 has learn time whenever it has no review, so from day 1 on something is introduced.
        var hasReview = days.Select((_, i) => anyIntroduced || i > 0).ToList();
        var learnMinutes = days
            .Select((day, i) => budget.MinutesOn(day) - (hasReview[i] ? ReviewMinutes(budget.MinutesOn(day)) : 0))
            .ToList();

        // Without a review block to take them, the minutes of day 0 that don't fill a step go to the heaviest topic.
        var leftOnDayZero = hasReview[0] ? 0 : learnMinutes[0] % Step;
        learnMinutes[0] -= leftOnDayZero;

        var firstFinal = days.Count - finalPhase;
        var beforeMinutes = learnMinutes.Take(firstFinal).Sum(minutes => (long)minutes);
        var shortened = Shorten(
            weighted,
            learnMinutes.Sum(minutes => (long)minutes) + leftOnDayZero,
            newCanLearn: beforeMinutes > 0 || leftOnDayZero > 0);
        var learnWeights = weighted.Select((topic, i) => shortened[i] ? 0 : topic.Weight).ToList();
        var heaviest = learnWeights.FindIndex(weight => weight > 0);

        var before = Share(beforeMinutes, learnWeights);
        var inFinal = weighted.Select((topic, i) => !topic.IsNew || before[i] > 0 || (i == heaviest && leftOnDayZero > 0)).ToList();
        var final = Share(
            learnMinutes.Skip(firstFinal).Sum(minutes => (long)minutes),
            learnWeights.Select((weight, i) => inFinal[i] ? weight : 0).ToList());

        var plan = Fill(days, budget, weighted, hasReview, learnMinutes, leftOnDayZero, heaviest, firstFinal, before, final);
        return plan with
        {
            Shortened = [.. weighted.Where((_, i) => shortened[i]).Select(topic => topic.TopicId).Order()],
        };
    }

    // Which topics lose their learn time because there isn't one block for each (#370). Weaknesses never, foundations
    // only when the room is used up by weaknesses and heavier foundations, then the lightest first (topics come sorted,
    // heaviest first). New topics only count when there are learn minutes before the
    // final phase: otherwise they get nothing anyway, and keeping one would leave the final phase without a topic.
    private static bool[] Shorten(List<Weighted> topics, long learnMinutes, bool newCanLearn)
    {
        var shortened = new bool[topics.Count];
        var candidates = Enumerable.Range(0, topics.Count).Where(i => newCanLearn || !topics[i].IsNew).ToList();
        var room = Math.Max(1, learnMinutes / MinLearnMinutesPerTopic);
        if (candidates.Count <= room)
        {
            return shortened;
        }

        // Weaknesses first, then foundations (#909), then the rest, each heaviest first.
        var kept = (long)candidates.Count(i => topics[i].IsWeakness);
        var others = candidates.Where(i => !topics[i].IsWeakness).OrderBy(i => topics[i].IsFoundation ? 0 : 1).ThenBy(i => i);
        foreach (var i in others)
        {
            if (kept < room)
            {
                kept++;
            }
            else
            {
                shortened[i] = true;
            }
        }

        return shortened;
    }

    // The last rounded 20 % of the study days, at least 2, and never more days than there are. 10 days give 2, 13 give 3.
    public static int FinalPhaseDays(int studyDays) =>
        Math.Min(studyDays, Math.Max(MinFinalPhaseDays, (int)Math.Round(studyDays * FinalPhaseShareInPercent / 100.0, MidpointRounding.AwayFromZero)));

    // 20 % of the day's minutes in steps of 5, at least 5 and at most 10, plus the minutes of the day that don't fill a
    // step, so every learn block can stay a multiple of 5. A budget below 5 minutes is all review.
    public static int ReviewMinutes(int dayMinutes)
    {
        var rest = dayMinutes % Step;
        var share = (dayMinutes - rest) * ReviewShareInPercent / 100 / Step * Step;
        return Math.Min(dayMinutes, Math.Clamp(share, Step, MaxReviewMinutes) + rest);
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

    // Weights come sorted, heaviest first. A weight of 0 gets nothing.
    private static long[] Share(long total, List<long> weights)
    {
        var quotas = new long[weights.Count];
        var sum = weights.Aggregate(Int128.Zero, (acc, weight) => acc + weight);
        if (sum == 0)
        {
            return quotas;
        }

        var steps = total / Step;
        var remainders = new Int128[weights.Count];
        for (var i = 0; i < weights.Count; i++)
        {
            var exact = (Int128)steps * weights[i];
            quotas[i] = (long)(exact / sum);
            remainders[i] = exact % sum;
        }

        var left = steps - quotas.Sum();
        var byRemainder = Enumerable.Range(0, weights.Count)
            .Where(i => weights[i] > 0)
            .OrderByDescending(i => remainders[i])
            .ThenBy(i => i);
        foreach (var i in byRemainder.Take((int)left))
        {
            quotas[i]++;
        }

        for (var i = 0; i < quotas.Length; i++)
        {
            quotas[i] *= Step;
        }

        quotas[weights.FindIndex(weight => weight > 0)] += total % Step;
        return quotas;
    }

    private static StudyPlan Fill(
        List<DateOnly> days,
        DailyBudget budget,
        List<Weighted> topics,
        List<bool> hasReview,
        List<int> learnMinutes,
        int leftOnDayZero,
        int heaviest,
        int firstFinal,
        long[] before,
        long[] final)
    {
        var introduced = topics.Select(topic => !topic.IsNew).ToArray();
        var reviews = new int[topics.Count];
        var open = before.ToArray();
        var plan = new List<DayPlan>();
        for (var d = 0; d < days.Count; d++)
        {
            var quotas = d < firstFinal ? before : final;
            if (d == firstFinal)
            {
                open = final.ToArray();
            }

            var blocks = new List<Block>();
            if (hasReview[d])
            {
                var next = NextReview(introduced, reviews);
                reviews[next]++;
                blocks.Add(new Block(topics[next].TopicId, budget.MinutesOn(days[d]) - learnMinutes[d], BlockKind.Review));
            }

            var dayLeft = (long)learnMinutes[d];
            var learned = new List<int>();
            while (dayLeft > 0)
            {
                var next = MostOpen(open, quotas);
                if (next < 0)
                {
                    break;
                }

                var minutes = Math.Min(BlockMinutes, Math.Min(open[next], dayLeft));
                blocks.Add(new Block(topics[next].TopicId, (int)minutes));
                learned.Add(next);
                open[next] -= minutes;
                dayLeft -= minutes;
            }

            if (d == 0 && leftOnDayZero > 0)
            {
                blocks.Add(new Block(topics[heaviest].TopicId, leftOnDayZero));
                learned.Add(heaviest);
            }

            // Introduced once its first learn block is done, so it can come up for review from the next day on.
            learned.ForEach(i => introduced[i] = true);

            if (blocks.Count > 0)
            {
                plan.Add(new DayPlan(days[d], blocks));
            }
        }

        return new StudyPlan(plan);
    }

    // The introduced topic with the fewest review blocks so far, the heavier one on a tie (topics come sorted).
    private static int NextReview(bool[] introduced, int[] reviews)
    {
        var best = -1;
        for (var i = 0; i < introduced.Length; i++)
        {
            if (introduced[i] && (best < 0 || reviews[i] < reviews[best]))
            {
                best = i;
            }
        }

        return best;
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

    private sealed record Weighted(Guid TopicId, long Weight, bool IsNew, bool IsWeakness, bool IsFoundation, int Rank);
}