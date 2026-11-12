using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Assessment.Domain;
using Edvaniq.Services.Assessment.Infrastructure;
using Edvaniq.Testing;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Assessment.IntegrationTests;

// #387: the quiz of the weaknesses takes them from LearningEngine with the caller's own token and asks only their
// questions, the severest first. A block's quiz and a single question say when the pool was too small.
public sealed class WeaknessQuizTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    [Fact]
    public async Task WeaknessQuiz_AsksOnlyTheWeaknesses_WithTheCallersToken()
    {
        var alice = User.New("alice");
        var severest = await AddTopicAsync(alice, questions: 2);
        var lighter = await AddTopicAsync(alice, questions: 2);
        var building = await AddTopicAsync(alice, questions: 2);
        service.LearningEngine.Weaknesses(alice.Token, severest, lighter);
        var goal = Guid.NewGuid();

        var (status, quiz) = await WeaknessQuizAsync(alice, goal, size: 4);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal([severest, lighter, severest, lighter], quiz!.Questions.Select(question => question.TopicId));
        Assert.DoesNotContain(quiz.Questions, question => question.TopicId == building);
        Assert.False(quiz.PoolTooSmall);
        Assert.Contains(service.LearningEngine.Requests, request =>
            request.Token == alice.Token && request.PathAndQuery == $"/mastery/weak-and-secure?learningGoalId={goal}");
    }

    // The questions are attempts like any other: answered at /attempts/{id}/answer.
    [Fact]
    public async Task WeaknessQuiz_QuestionsAreAnsweredLikeAnyOther()
    {
        var alice = User.New("alice");
        var weakness = await AddTopicAsync(alice, questions: 1);
        service.LearningEngine.Weaknesses(alice.Token, weakness);

        var (_, quiz) = await WeaknessQuizAsync(alice, Guid.NewGuid(), size: 3);
        using var client = ClientFor(alice);
        using var answered = await client.PostAsJsonAsync(
            $"/attempts/{quiz!.Questions[0].Id}/answer", new { option = 1, responseTimeMs = 0 }, TestContext.Current.CancellationToken);

        Assert.True(quiz.PoolTooSmall);
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
    }

    // A topic LearningEngine names that isn't the caller's in Assessment brings no questions of anyone else.
    [Fact]
    public async Task ForeignWeaknessTopics_GiveNoQuestions()
    {
        var alice = User.New("alice");
        var bob = User.New("bob");
        var alicesTopic = await AddTopicAsync(alice, questions: 3);
        service.LearningEngine.Weaknesses(bob.Token, alicesTopic);

        var (status, _) = await WeaknessQuizAsync(bob, Guid.NewGuid(), size: 3);

        Assert.Equal(HttpStatusCode.Conflict, status);
    }

    [Fact]
    public async Task WithoutWeaknesses_Conflict_LearningEngineDown_Unavailable()
    {
        var alice = User.New("alice");
        var bob = User.New("bob");
        service.LearningEngine.Down(bob.Token);

        Assert.Equal(HttpStatusCode.Conflict, (await WeaknessQuizAsync(alice, Guid.NewGuid(), size: 3)).Status);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await WeaknessQuizAsync(bob, Guid.NewGuid(), size: 3)).Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task SizeOutOfRange_IsRejected(int size)
    {
        var alice = User.New("alice");

        Assert.Equal(HttpStatusCode.BadRequest, (await WeaknessQuizAsync(alice, Guid.NewGuid(), size)).Status);
    }

    // AC1 and AC3 for a block's quiz: an answered question stays out of the next quiz while the topic has others, and a
    // quiz that has to repeat one says so, on every call.
    [Fact]
    public async Task BlockQuiz_LeavesOutAnswered_AndSaysWhenThePoolIsTooSmall()
    {
        var alice = User.New("alice");
        var topic = await AddTopicAsync(alice, questions: 4);
        var first = await OpenAsync(alice, topic, Guid.NewGuid(), size: 2);
        foreach (var question in first.Questions)
        {
            await AnswerAsync(alice, question.Id);
        }

        var second = await OpenAsync(alice, topic, Guid.NewGuid(), size: 2);
        var thirdBlock = Guid.NewGuid();
        var third = await OpenAsync(alice, topic, thirdBlock, size: 3);
        var thirdAgain = await OpenAsync(alice, topic, thirdBlock, size: 3);

        Assert.Empty(first.Questions.Select(question => question.QuestionId).Intersect(second.Questions.Select(question => question.QuestionId)));
        Assert.False(first.PoolTooSmall || second.PoolTooSmall);
        Assert.True(third.PoolTooSmall);
        Assert.True(thirdAgain.PoolTooSmall);
    }

    // AC1 and AC3 for a single question.
    [Fact]
    public async Task Attempt_SaysWhenItRepeatsARecentQuestion()
    {
        var alice = User.New("alice");
        var topic = await AddTopicAsync(alice, questions: 1);

        var first = await AskAsync(alice, topic);
        await AnswerAsync(alice, first.GetProperty("id").GetGuid());
        var second = await AskAsync(alice, topic);

        Assert.False(first.GetProperty("repeated").GetBoolean());
        Assert.True(second.GetProperty("repeated").GetBoolean());
    }

    private sealed record User(string Id, string Token)
    {
        public static User New(string name)
        {
            var id = $"{name}-{Guid.NewGuid():N}";
            return new(id, TestTokens.Create(id));
        }
    }

    private sealed record QuizDto(List<QuestionDto> Questions, bool PoolTooSmall);

    private sealed record QuestionDto(Guid Id, Guid QuestionId, Guid TopicId);

    // A known topic of the user with this many single choice questions, the right option is 1.
    private async Task<Guid> AddTopicAsync(User user, int questions)
    {
        var topicId = Guid.NewGuid();
        await service.AddKnownTopicAsync(user.Id, topicId);
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssessmentDbContext>();
        for (var i = 0; i < questions; i++)
        {
            db.Questions.Add(Question.Create(
                Guid.NewGuid(), user.Id, topicId, Guid.NewGuid(), 0, Guid.NewGuid(), $"Frage {i}", ["3", "4"], 1, "Zwei und zwei ist vier.", 1,
                "simulated", "questions-1", Seed.Now));
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return topicId;
    }

    private HttpClient ClientFor(User user)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        return client;
    }

    private async Task<(HttpStatusCode Status, QuizDto? Quiz)> WeaknessQuizAsync(User user, Guid learningGoalId, int size)
    {
        using var client = ClientFor(user);
        using var response = await client.PostAsJsonAsync("/quizzes/weaknesses", new { learningGoalId, size }, TestContext.Current.CancellationToken);
        return response.IsSuccessStatusCode
            ? (response.StatusCode, await response.Content.ReadFromJsonAsync<QuizDto>(TestContext.Current.CancellationToken))
            : (response.StatusCode, null);
    }

    private async Task<QuizDto> OpenAsync(User user, Guid topicId, Guid blockId, int size)
    {
        using var client = ClientFor(user);
        using var response = await client.PostAsJsonAsync("/quizzes", new { topicId, blockId, size }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<QuizDto>(TestContext.Current.CancellationToken))!;
    }

    private async Task<JsonElement> AskAsync(User user, Guid topicId)
    {
        using var client = ClientFor(user);
        using var response = await client.PostAsJsonAsync("/attempts", new { topicId }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private async Task AnswerAsync(User user, Guid attemptId)
    {
        using var client = ClientFor(user);
        using var response = await client.PostAsJsonAsync(
            $"/attempts/{attemptId}/answer", new { option = 1, responseTimeMs = 0 }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}