using System.Net;

using Edvaniq.Testing;

using static Edvaniq.EventFlowTests.FlowSteps;

using PlanningDomain = Edvaniq.Services.Planning.Domain;

namespace Edvaniq.EventFlowTests;

// #211: an answer changes today's plan. Assessment grades it and sends AnswerRecorded, LearningEngine stores the
// learning record, and Planning, which works the plan out on every call with the mastery asked from LearningEngine,
// shows the new state. Nothing is stored or cached on the way, so the only wait is the broker.
[Collection(FlowCollection.Name)]
public sealed class PlanFlowTests(FlowServices services) : IDisposable
{
    private readonly FlowSteps steps = new(services);

    [Fact(Timeout = 180_000)]
    public async Task RightAnswer_MovesItsTopicBackInTodaysPlan()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        var token = TestTokens.Create(alice);
        var cancellationToken = TestContext.Current.CancellationToken;
        var topics = await steps.TopicsWithQuestionsAsync(token, alice);

        // Every topic stands at LearningEngine's start of 50, so they weigh the same and the id decides. The topic of
        // the first learn block is the one the plan puts first.
        var before = await steps.PlanAsync(token, cancellationToken);
        Assert.True(before.MasteryAvailable);
        var topic = before.Blocks.First(block => block.Kind == "learn").TopicId;
        Assert.Contains(topic, topics);

        await steps.AnswerRightAsync(token, topic, cancellationToken);

        // A right answer pulls the mastery part of the way to 100: 50 to 62 for a middle question (57 to 67 by
        // difficulty), so its weight drops from 50 to 38 while the others stay at 50. The topic has to lose minutes
        // today or come up later, whatever else the plan looks like.
        await WaitForAsync(
            async () =>
            {
                var after = await steps.PlanAsync(token, cancellationToken);
                return LearnMinutes(after, topic) < LearnMinutes(before, topic) || FirstLearnBlock(after, topic) > FirstLearnBlock(before, topic);
            },
            "Today's plan did not move the answered topic back.");
    }

    // #222, Q4: how long after an answer LearningEngine names it as the topic's newest, so mastery and today's plan
    // contain it. Measured from the grade to the first GET /mastery that has it, asked every 20 ms: Assessment's outbox,
    // NATS, LearningEngine's inbox and its database, all real. Not in it: the way between browser and server and the
    // client's own interval of 500 ms. Ten answers one after the other, the median has to stay under 5 s.
    [Fact(Timeout = 300_000)]
    public async Task Answer_ReachesMasteryAndPlan_TypicallyWithinFiveSeconds()
    {
        const int Answers = 10;
        var bob = $"bob-{Guid.NewGuid():N}";
        var token = TestTokens.Create(bob);
        var cancellationToken = TestContext.Current.CancellationToken;
        var topic = (await steps.TopicsWithQuestionsAsync(token, bob))[0];
        using var learningEngine = Client(services.LearningEngine.CreateClient(), token);

        var delays = new List<TimeSpan>();
        for (var i = 0; i < Answers; i++)
        {
            var answer = await steps.AnswerRightAsync(token, topic, cancellationToken);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (await LastAnswerAsync(learningEngine, topic, cancellationToken) != answer)
            {
                Assert.True(watch.Elapsed < FlowSteps.Timeout, $"Answer {i + 1} never reached LearningEngine.");
                await Task.Delay(20, cancellationToken);
            }

            delays.Add(watch.Elapsed);

            // The plan is worked out from that same mastery on every call, so it contains the answer now too.
            Assert.True((await steps.PlanAsync(token, cancellationToken)).MasteryAvailable);
        }

        var sorted = delays.Order().ToList();
        var median = (sorted[(Answers / 2) - 1] + sorted[Answers / 2]) / 2;
        var measured = $"Q4, answer to mastery and plan, {Answers} answers: median {median.TotalMilliseconds:F0} ms, "
            + $"max {sorted[^1].TotalMilliseconds:F0} ms ({string.Join(", ", delays.Select(delay => $"{delay.TotalMilliseconds:F0}"))})";
        TestContext.Current.TestOutputHelper?.WriteLine(measured);
        Assert.True(median < TimeSpan.FromSeconds(5), $"Median {median.TotalMilliseconds:F0} ms, Q4 allows 5000 ms.");
    }

    // #378, A46, Q3: the demo session. A session on the first learn block, every quiz answer right, then finished in
    // Planning. Once LearningEngine has the answers (they arrive late, through the broker) A's mastery has risen, the
    // block is done, and today's plan follows the new mastery: A has less learn time today, or the planner's own rule
    // gives A the same time with the old and the new mastery (then the plan confirms it). Each recomputation takes under
    // 2 s. The known flake point ("Assessment did not take the questions of every topic over", Manager 1's notes) sits
    // in TopicsWithQuestionsAsync, shared with the tests above.
    [Fact(Timeout = 180_000)]
    public async Task DemoSession_ReplansTheRest_InUnderTwoSeconds()
    {
        var carol = $"carol-{Guid.NewGuid():N}";
        var token = TestTokens.Create(carol);
        var cancellationToken = TestContext.Current.CancellationToken;
        await steps.TopicsWithQuestionsAsync(token, carol);
        var goal = PlanningDomain.LearningGoal.DefaultIdFor(carol);
        using var planning = Client(services.Planning.CreateClient(), token);
        using var learningEngine = Client(services.LearningEngine.CreateClient(), token);

        var before = await PlanWithIdsAsync(planning, cancellationToken);
        var block = before.Blocks.First(entry => entry.Kind == "learn");
        var standingsBefore = await StandingsAsync(learningEngine, goal, cancellationToken);

        var session = await SendAsync(planning, "/sessions", new { blockId = block.Id }, HttpStatusCode.Created, cancellationToken);
        var sessionId = session.GetProperty("id").GetGuid();
        await SendAsync(planning, $"/sessions/{sessionId}/advance", new { step = session.GetProperty("steps").GetArrayLength() - 1 }, HttpStatusCode.OK, cancellationToken);
        var last = await steps.AnswerCheckQuestionsRightAsync(token, block.TopicId, sessionId, cancellationToken);

        await SendAsync(planning, $"/sessions/{sessionId}/complete", new { }, HttpStatusCode.OK, cancellationToken);

        // Late on purpose: the plan is only checked once LearningEngine has the last answer, and A has to have risen.
        await WaitForAsync(async () => await LastAnswerAsync(learningEngine, block.TopicId, cancellationToken) == last, "LearningEngine never got the answers.");
        var standingsAfter = await StandingsAsync(learningEngine, goal, cancellationToken);
        Assert.True(
            standingsAfter[block.TopicId].Mastery > standingsBefore[block.TopicId].Mastery,
            $"A's mastery went from {standingsBefore[block.TopicId].Mastery} to {standingsAfter[block.TopicId].Mastery}.");

        var times = new List<TimeSpan>();
        Plan after = null!;
        for (var call = 0; call < 5; call++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            after = await PlanWithIdsAsync(planning, cancellationToken);
            times.Add(watch.Elapsed);
        }

        Assert.True(after.Blocks.Single(entry => entry.Id == block.Id).Done);
        var measured = string.Join(", ", times.Select(time => $"{time.TotalMilliseconds:F0}"));
        Assert.True(times.Max() < TimeSpan.FromSeconds(2), $"Q3 allows 2000 ms per recomputation, measured {measured} ms.");

        // AC1: the rest of today is what the planner makes of the new mastery, with the done block kept (#375). Whether A
        // got less time or kept it, the planner's own rule decides: the same inputs with A at its old mastery show which.
        var replan = await steps.ReplanAsync(planning, carol, goal, before, after, block.TopicId, standingsBefore, standingsAfter, cancellationToken);
        var verdict = replan.LearnWithNew < replan.LearnWithOld ? "changed" : "confirmed";
        Assert.True(
            replan.Follows(after),
            $"Today's plan doesn't follow the new mastery ({verdict}: A {replan.LearnWithOld} → {replan.LearnWithNew} learn minutes in a fresh plan).");
        Assert.True(replan.LearnWithNew <= replan.LearnWithOld, "A got more time after a session that raised its mastery.");
    }

    public void Dispose() => steps.Dispose();

    private static int LearnMinutes(Plan plan, Guid topic) =>
        plan.Blocks.Where(block => block.Kind == "learn" && block.TopicId == topic).Sum(block => block.Minutes);

    // Position among today's learn blocks, after all of them when the topic isn't on today.
    private static int FirstLearnBlock(Plan plan, Guid topic)
    {
        var learn = plan.Blocks.Where(block => block.Kind == "learn").ToList();
        var index = learn.FindIndex(block => block.TopicId == topic);
        return index < 0 ? int.MaxValue : index;
    }
}