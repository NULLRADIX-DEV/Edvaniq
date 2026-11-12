using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.Services.Planning.Domain;
using Edvaniq.Services.Planning.Infrastructure;
using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Planning.IntegrationTests;

// #377: a session comes with its fixed steps. Review first (empty in M3), the introduction unit only for a new topic,
// then the quiz. Fixed at the start, so they stay when the topic's state changes later.
public sealed class SessionStepTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static readonly Step EmptyReview = new("review", true);
    private static readonly Step Unit = new("introductionUnit", false);
    private static readonly Step Quiz = new("quiz", false, 5);

    // AC1 and AC3.
    [Fact]
    public async Task NewTopic_HasReviewThenIntroductionUnitThenQuiz()
    {
        var token = Seed(Guid.NewGuid(), "new");

        var session = await StartLearnBlockAsync(token);

        Assert.Equal(new[] { EmptyReview, Unit, Quiz }, session.Steps);
        Assert.Equal(session.Steps, (await GetAsync(token, $"/sessions/{session.Id}")).Steps);
        Assert.Equal(session.Steps, (await GetAsync(token, "/sessions/active")).Steps);
    }

    // AC2 and AC3.
    [Fact]
    public async Task KnownTopic_HasReviewThenQuizOnly()
    {
        var token = Seed(Guid.NewGuid(), "building");

        var session = await StartLearnBlockAsync(token);

        Assert.Equal(new[] { EmptyReview, Quiz }, session.Steps);
    }

    // LearningEngine down: the topic can't be vouched for, so it gets the unit. The steps are stored, LearningEngine
    // answering again later doesn't change them.
    [Fact]
    public async Task LearningEngineDown_GivesTheUnit_AndTheStepsStayWhenItIsBack()
    {
        var topic = Guid.NewGuid();
        var token = Seed(topic, "building");
        service.LearningEngine.Unreachable(token);

        var session = await StartLearnBlockAsync(token);
        service.LearningEngine.Reachable(token);
        service.LearningEngine.Set(token, topic, 90, "secure");

        Assert.Equal(new[] { EmptyReview, Unit, Quiz }, session.Steps);
        Assert.Equal(session.Steps, (await GetAsync(token, $"/sessions/{session.Id}")).Steps);
    }

    private string Seed(Guid topic, string state)
    {
        var user = $"alice-{Guid.NewGuid():N}";
        var token = TestTokens.Create(user);
        using (var scope = service.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanningDbContext>();
            db.KnownTopics.Add(KnownTopic.Create(user, topic, LearningGoal.DefaultIdFor(user), hidden: false));
            db.SaveChanges();
        }

        service.Assessment.Set(token, topic);
        service.LearningEngine.Set(token, topic, 20, state);
        return token;
    }

    private async Task<Session> StartLearnBlockAsync(string token)
    {
        var plan = await GetAsync<Plan>(token, "/plan/today");
        var block = plan.Blocks.First(candidate => candidate.Kind == "learn");
        using var client = Client(token);
        using var response = await client.PostAsJsonAsync("/sessions", new { blockId = block.Id }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Session>(TestContext.Current.CancellationToken))!;
    }

    private Task<Session> GetAsync(string token, string path) => GetAsync<Session>(token, path);

    private async Task<T> GetAsync<T>(string token, string path)
    {
        using var client = Client(token);
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken))!;
    }

    private HttpClient Client(string token)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed record Plan(List<PlannedBlock> Blocks);

    private sealed record PlannedBlock(Guid Id, string Kind);

    private sealed record Session(Guid Id, List<Step> Steps);

    private sealed record Step(string Kind, bool Empty, int? Questions = null);
}