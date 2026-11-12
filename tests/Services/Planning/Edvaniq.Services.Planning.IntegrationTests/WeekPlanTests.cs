using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.Services.Planning.Domain;
using Edvaniq.Services.Planning.Infrastructure;
using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Planning.IntegrationTests;

// GET /plan/week (#379): the days from today on, day 0 exactly as /plan/today, cut before the exam day of the goal.
public sealed class WeekPlanTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Week_WithoutToken_IsRejected()
    {
        using var client = service.CreateClient();
        using var response = await client.GetAsync("/plan/week", Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // One way to plan: day 0 has the blocks of /plan/today with the same ids, and without an exam there are 7 days in a row.
    [Fact]
    public async Task DayZero_IsTodaysPlan_SevenDaysWithoutAnExam()
    {
        var (alice, token) = NewUser("alice");
        var (strong, weak) = (Guid.NewGuid(), Guid.NewGuid());
        Seed(alice, token, strong);
        Seed(alice, token, weak);
        service.LearningEngine.Set(token, strong, 70);
        service.LearningEngine.Set(token, weak, 10);
        using var client = ClientFor(token);

        var today = await GetAsync<Today>(client, "/plan/today");
        var week = await GetAsync<Week>(client, "/plan/week");

        Assert.Equal(7, week.Days.Count);
        Assert.Equal(Enumerable.Range(0, 7).Select(today.StudyDay.AddDays), week.Days.Select(day => day.StudyDay));
        Assert.Equal(today.Blocks, week.Days[0].Blocks);
        Assert.Equal(today.LearningGoalId, week.LearningGoalId);
        Assert.Equal(today.ShortenedTopicIds, week.ShortenedTopicIds);
        Assert.Null(week.EmptyReason);
        Assert.True(week.MasteryAvailable);
        Assert.Contains(week.Days.Skip(1), day => day.Blocks.Count > 0);
    }

    // An exam in 3 days: only the 3 days before it.
    [Fact]
    public async Task WithAnExamInThreeDays_ShowsTheDaysBeforeIt()
    {
        var (alice, token) = NewUser("alice");
        Seed(alice, token, Guid.NewGuid());
        using var client = ClientFor(token);
        var exam = SharedKernel.StudyDay.Of(DateTime.UtcNow).AddDays(3);
        var goal = new
        {
            Kind = "ExamPreparation",
            Name = "Mathe",
            ExamType = "Schulaufgabe",
            ExamDate = exam.ToString("yyyy-MM-dd"),
            Level = "MiddleSecondary",
            OutputLanguage = "de",
        };
        using (var created = await client.PostAsJsonAsync("/learning-goals", goal, Cancel))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var week = await GetAsync<Week>(client, "/plan/week");

        Assert.Equal(3, week.Days.Count);
        Assert.All(week.Days, day => Assert.True(day.StudyDay < exam));
    }

    // No topics: every day an empty list, and the reason the client turns into "create a learning goal".
    [Fact]
    public async Task WithoutTopics_AllDaysAreEmpty_WithTheNoTopicsHint()
    {
        var (_, token) = NewUser("alice");
        using var client = ClientFor(token);

        var week = await GetAsync<Week>(client, "/plan/week");

        Assert.Equal(7, week.Days.Count);
        Assert.All(week.Days, day => Assert.Empty(day.Blocks));
        Assert.Equal("noTopics", week.EmptyReason);
    }

    private (string User, string Token) NewUser(string name)
    {
        var user = $"{name}-{Guid.NewGuid():N}";
        return (user, TestTokens.Create(user));
    }

    private HttpClient ClientFor(string token)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Cancel))!;
    }

    private void Seed(string owner, string token, Guid topic)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanningDbContext>();
        db.KnownTopics.Add(KnownTopic.Create(owner, topic, LearningGoal.DefaultIdFor(owner), hidden: false));
        db.SaveChanges();
        service.Assessment.Set(token, topic);
    }

    private sealed record Today(DateOnly StudyDay, Guid LearningGoalId, List<Block> Blocks, List<Guid> ShortenedTopicIds);

    private sealed record Week(
        Guid LearningGoalId,
        List<Day> Days,
        string? EmptyReason,
        bool MasteryAvailable,
        List<Guid> ShortenedTopicIds,
        bool QuestionPoolsAvailable,
        List<Guid> NotReadyTopicIds);

    private sealed record Day(DateOnly StudyDay, List<Block> Blocks);

    private sealed record Block(Guid TopicId, int Minutes, string Kind, Guid Id, bool Done);
}