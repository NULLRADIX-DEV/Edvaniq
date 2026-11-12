using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Domain;
using Edvaniq.Services.Planning.Infrastructure;
using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Planning.IntegrationTests;

// #211 AC3: /plan/today switches to the next study day at 04:00 Europe/Berlin, in winter (UTC+1) and in summer (UTC+2).
public sealed class StudyDayBoundaryTests(StudyDayBoundaryTests.ClockedService service) : IClassFixture<StudyDayBoundaryTests.ClockedService>
{
    [Theory]
    [InlineData("2026-11-17T02:59:00Z", "2026-11-16")]
    [InlineData("2026-11-17T03:00:00Z", "2026-11-17")]
    [InlineData("2027-06-15T01:59:00Z", "2027-06-14")]
    [InlineData("2027-06-15T02:00:00Z", "2027-06-15")]
    public async Task Plan_TakesTheStudyDay_ThatStartsAtFourInBerlin(string utcNow, string studyDay)
    {
        service.Clock.Now = DateTimeOffset.Parse(utcNow, System.Globalization.CultureInfo.InvariantCulture);
        using var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create($"alice-{Guid.NewGuid():N}"));

        using var response = await client.GetAsync("/plan/today", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var plan = await response.Content.ReadFromJsonAsync<Plan>(TestContext.Current.CancellationToken);
        Assert.Equal(DateOnly.Parse(studyDay, System.Globalization.CultureInfo.InvariantCulture), plan!.StudyDay);
    }

    // #381 AC4 as in A51: a block finished just before 04:00 Europe/Berlin reaches the goal of the day that is ending,
    // and a minute after 04:00 the goal of the new day is open.
    [Theory]
    [InlineData("2026-11-17T02:59:00Z", "2026-11-17T03:01:00Z")]
    [InlineData("2027-06-15T01:59:00Z", "2027-06-15T02:01:00Z")]
    public async Task DailyGoal_FollowsTheStudyDay(string justBefore, string justAfter)
    {
        var user = $"alice-{Guid.NewGuid():N}";
        var token = TestTokens.Create(user);
        var topic = Guid.NewGuid();
        Execute(db =>
        {
            db.KnownTopics.Add(KnownTopic.Create(user, topic, LearningGoal.DefaultIdFor(user), hidden: false));
            db.LearningGoals.Add(LearningGoal.CreateDefault(user, DateTime.UtcNow));
            return db.SaveChanges();
        });
        service.Assessment.Set(token, topic);
        service.LearningEngine.Set(token, topic, 20);

        service.Clock.Now = DateTimeOffset.Parse(justBefore, System.Globalization.CultureInfo.InvariantCulture);
        var ending = await PlanAsync(token);
        Execute(db =>
        {
            foreach (var block in ending.Blocks)
            {
                var kind = block.Kind == "review" ? BlockKind.Review : BlockKind.Learn;
                db.DoneBlocks.Add(DoneBlock.Create(
                    block.Id, user, ending.LearningGoalId, ending.StudyDay, new Block(block.TopicId, block.Minutes, kind), service.Clock.Now.UtcDateTime));
            }

            return db.SaveChanges();
        });

        Assert.Equal("reached", (await PlanAsync(token)).DailyGoal);

        service.Clock.Now = DateTimeOffset.Parse(justAfter, System.Globalization.CultureInfo.InvariantCulture);
        var next = await PlanAsync(token);

        Assert.Equal(ending.StudyDay.AddDays(1), next.StudyDay);
        Assert.All(next.Blocks, block => Assert.False(block.Done));
        Assert.Equal("open", next.DailyGoal);
    }

    private async Task<GoalPlan> PlanAsync(string token)
    {
        using var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.GetAsync("/plan/today", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GoalPlan>(TestContext.Current.CancellationToken))!;
    }

    private T Execute<T>(Func<PlanningDbContext, T> action)
    {
        using var scope = service.Services.CreateScope();
        return action(scope.ServiceProvider.GetRequiredService<PlanningDbContext>());
    }

    // The service with a clock the test sets. Only the plan reads it, outbox and inbox keep the real time.
    public sealed class ClockedService(MySqlTestServer mysql) : ServiceFactory(mysql)
    {
        public TestClock Clock { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
                services.AddScoped(provider => ActivatorUtilities.CreateInstance<TodayPlans>(provider, (TimeProvider)Clock)));
        }
    }

    public sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record Plan(DateOnly StudyDay);

    private sealed record GoalPlan(DateOnly StudyDay, Guid LearningGoalId, List<PlannedBlock> Blocks, string DailyGoal);

    private sealed record PlannedBlock(Guid TopicId, int Minutes, string Kind, Guid Id, bool Done);
}