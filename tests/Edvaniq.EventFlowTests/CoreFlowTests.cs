using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;

using static Edvaniq.EventFlowTests.FlowSteps;

using PlanningDomain = Edvaniq.Services.Planning.Domain;

namespace Edvaniq.EventFlowTests;

// #428: the core flow end to end, as a user goes through it: a learning goal, a pasted text analysed by the simulated
// AI, today's plan, a session on its first learn block with every check question right, the session's result, the
// risen mastery and today's plan worked out again from it. Twice with a fresh account each, and both runs have to come
// out the same, ids aside: the simulated AI answers the same text the same way and nothing else is left to chance.
[Collection(FlowCollection.Name)]
public sealed class CoreFlowTests(FlowServices services) : IDisposable
{
    private readonly FlowSteps steps = new(services);

    [Fact(Timeout = 600_000)]
    public async Task CoreFlow_GoalAnalysisPlanSessionMasteryAndReplan()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (first, second) = (await RunAsync(cancellationToken), await RunAsync(cancellationToken));

        // Both runs have to plan the same study day. Only a run across the day's boundary at 04:00 can miss that, so
        // it's done again once instead of failing for the clock.
        if (first.StudyDays.Distinct().Count() > 1 || second.StudyDays.Distinct().Count() > 1 || first.StudyDays[0] != second.StudyDays[0])
        {
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"The runs crossed the start of a study day ({string.Join(", ", first.StudyDays.Concat(second.StudyDays))}), running both again.");
            (first, second) = (await RunAsync(cancellationToken), await RunAsync(cancellationToken));
        }

        Assert.Equal(first.Outcome, second.Outcome);
    }

    private async Task<Run> RunAsync(CancellationToken cancellationToken)
    {
        var user = $"core-{Guid.NewGuid():N}";
        var token = TestTokens.Create(user);
        using var planning = Client(services.Planning.CreateClient(), token);
        using var learningEngine = Client(services.LearningEngine.CreateClient(), token);

        // The first goal of an account is the one its plan follows.
        using var created = await planning.PostAsJsonAsync("/learning-goals", new { kind = "FreeLearning", name = "Biologie" }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var goal = (await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("id").GetGuid();
        Assert.Equal(PlanningDomain.LearningGoal.DefaultIdFor(user), goal);

        var topics = await steps.TopicsWithQuestionsAsync(token, user, goal);
        var names = services.QueryKnowledge(db => db.Topics.IgnoreQueryFilters()
            .Where(topic => topics.Contains(topic.Id)).ToDictionary(topic => topic.Id, topic => topic.Name));
        Assert.All(names.Keys, topic => Assert.Equal(goal, services.QueryKnowledge(db => db.Topics.IgnoreQueryFilters().Single(entry => entry.Id == topic).LearningGoalId)));

        var before = await PlanWithIdsAsync(planning, cancellationToken);
        Assert.Equal(goal, before.LearningGoalId);
        var block = before.Blocks.First(entry => entry.Kind == "learn");
        var standingsBefore = await StandingsAsync(learningEngine, goal, cancellationToken);

        var session = await SendAsync(planning, "/sessions", new { blockId = block.Id }, HttpStatusCode.Created, cancellationToken);
        var sessionId = session.GetProperty("id").GetGuid();
        await SendAsync(planning, $"/sessions/{sessionId}/advance", new { step = session.GetProperty("steps").GetArrayLength() - 1 }, HttpStatusCode.OK, cancellationToken);
        var last = await steps.AnswerCheckQuestionsRightAsync(token, block.TopicId, sessionId, cancellationToken);
        await SendAsync(planning, $"/sessions/{sessionId}/complete", new { }, HttpStatusCode.OK, cancellationToken);

        // The answers reach LearningEngine through the broker, the result waits for them until it's complete.
        await WaitForAsync(async () => await LastAnswerAsync(learningEngine, block.TopicId, cancellationToken) == last, "LearningEngine never got the answers.");
        JsonElement result = default;
        await WaitForAsync(
            async () =>
            {
                result = await planning.GetFromJsonAsync<JsonElement>($"/sessions/{sessionId}/result", cancellationToken);
                return result.GetProperty("complete").GetBoolean();
            },
            "The session's result never got complete.");
        Assert.Equal("completed", result.GetProperty("outcome").GetString());
        Assert.True(result.GetProperty("answered").GetInt32() > 0);
        Assert.Equal((result.GetProperty("answered").GetInt32(), 0), (result.GetProperty("correct").GetInt32(), result.GetProperty("wrong").GetInt32()));

        var standingsAfter = await StandingsAsync(learningEngine, goal, cancellationToken);
        Assert.True(
            standingsAfter[block.TopicId].Mastery > standingsBefore[block.TopicId].Mastery,
            $"The mastery of {names[block.TopicId]} went from {standingsBefore[block.TopicId].Mastery} to {standingsAfter[block.TopicId].Mastery}.");

        var after = await PlanWithIdsAsync(planning, cancellationToken);
        Assert.True(after.Blocks.Single(entry => entry.Id == block.Id).Done);
        var replan = await steps.ReplanAsync(planning, user, goal, before, after, block.TopicId, standingsBefore, standingsAfter, cancellationToken);
        Assert.True(replan.Follows(after), "Today's plan doesn't follow the new mastery.");
        Assert.True(replan.LearnWithNew <= replan.LearnWithOld, "The topic got more time after a session that raised its mastery.");

        string Blocks(Plan plan) => string.Join("; ", plan.Blocks.Select(entry => $"{names.GetValueOrDefault(entry.TopicId, "?")} {entry.Kind} {entry.Minutes}{(entry.Done ? " done" : "")}"));
        string Standings(IReadOnlyDictionary<Guid, PlanningDomain.TopicStanding> standings) =>
            string.Join("; ", standings.Where(entry => names.ContainsKey(entry.Key)).OrderBy(entry => names[entry.Key], StringComparer.Ordinal)
                .Select(entry => $"{names[entry.Key]} {entry.Value.Mastery} {entry.Value.State}"));
        var outcome = string.Join(
            Environment.NewLine,
            $"session on {names[block.TopicId]}: {result.GetProperty("outcome").GetString()}, {result.GetProperty("correct").GetInt32()} of {result.GetProperty("answered").GetInt32()} right",
            $"mastery before: {Standings(standingsBefore)}",
            $"mastery after: {Standings(standingsAfter)}",
            $"plan before: {Blocks(before)}",
            $"plan after: {Blocks(after)}");
        TestContext.Current.TestOutputHelper?.WriteLine(outcome);
        return new Run(outcome, [before.StudyDay, after.StudyDay]);
    }

    public void Dispose() => steps.Dispose();

    private sealed record Run(string Outcome, DateOnly[] StudyDays);
}