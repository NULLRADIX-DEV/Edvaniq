using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Assessment.Domain;
using Edvaniq.Services.Assessment.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Assessment.IntegrationTests;

// #393: POST /quizzes gives the fixed questions of a block's quiz, made on the first call and the same on every later
// one, with the answered ones marked. A session started again on the block thus only has the rest left.
public sealed class BlockQuizTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    [Fact]
    public async Task Quiz_IsMadeOnce_AndTheSameOnEveryCall()
    {
        var alice = User.New("alice");
        var topic = await AddTopicAsync(alice, questions: 7);
        var block = Guid.NewGuid();

        var (status, first) = await OpenAsync(alice, topic, block, size: 5);
        var (_, second) = await OpenAsync(alice, topic, block, size: 3);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(5, first!.Questions.Count);
        Assert.Equal(5, first.Questions.Select(question => question.QuestionId).Distinct().Count());
        Assert.All(first.Questions, question => Assert.False(question.Answered));
        Assert.Equal(first.Questions.Select(question => question.Id), second!.Questions.Select(question => question.Id));
        Assert.Equal((block, topic), (first.BlockId, first.TopicId));
    }

    // Like POST /attempts: no question of the quiz carries its solution, answered or not.
    [Fact]
    public async Task Solution_IsNowhere_InTheQuiz()
    {
        var alice = User.New("alice");
        var topic = await AddTopicAsync(alice, questions: 3);
        var block = Guid.NewGuid();
        var (_, quiz) = await OpenAsync(alice, topic, block, size: 3);
        await AnswerAsync(alice, quiz!.Questions[0].Id, option: 1);

        using var client = ClientFor(alice);
        using var response = await client.PostAsJsonAsync("/quizzes", new { topicId = topic, blockId = block, size = 3 }, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(["blockId", "id", "poolTooSmall", "questions", "topicId"], body.EnumerateObject().Select(property => property.Name).Order());
        Assert.All(body.GetProperty("questions").EnumerateArray(), question => Assert.Equal(
            ["answered", "correct", "difficulty", "id", "options", "questionId", "text", "topicId", "type"],
            question.EnumerateObject().Select(property => property.Name).Order()));
    }

    // AC1 of #393 (A88): two of five answered, broken off, started again: the same five, two of them answered, so only
    // three are left to ask.
    [Fact]
    public async Task AnsweredQuestions_AreMarked_SoOnlyTheRestIsLeft()
    {
        var alice = User.New("alice");
        var topic = await AddTopicAsync(alice, questions: 7);
        var block = Guid.NewGuid();
        var (_, quiz) = await OpenAsync(alice, topic, block, size: 5);

        await AnswerAsync(alice, quiz!.Questions[0].Id, option: 1);
        await AnswerAsync(alice, quiz.Questions[3].Id, option: 0);
        var (_, again) = await OpenAsync(alice, topic, block, size: 5);

        Assert.Equal(quiz.Questions.Select(question => question.Id), again!.Questions.Select(question => question.Id));
        Assert.True(again.Questions[0] is { Answered: true, Correct: true });
        Assert.True(again.Questions[3] is { Answered: true, Correct: false });
        Assert.Equal(3, again.Questions.Count(question => question is { Answered: false, Correct: null }));
    }

    // Owner and block are the key: Bob with Alice's block id gets a quiz of his own and can't answer hers.
    [Fact]
    public async Task ForeignBlockId_GivesTheCallersOwnQuiz_AndForeignQuestionsCantBeAnswered()
    {
        var alice = User.New("alice");
        var bob = User.New("bob");
        var block = Guid.NewGuid();
        var alicesTopic = await AddTopicAsync(alice, questions: 3);
        var bobsTopic = await AddTopicAsync(bob, questions: 3);
        var (_, alices) = await OpenAsync(alice, alicesTopic, block, size: 3);

        var (status, bobs) = await OpenAsync(bob, bobsTopic, block, size: 3);
        using var client = ClientFor(bob);
        using var answer = await client.PostAsJsonAsync(
            $"/attempts/{alices!.Questions[0].Id}/answer", new { option = 1, responseTimeMs = 0 }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotEqual(alices.Id, bobs!.Id);
        Assert.Empty(bobs.Questions.Select(question => question.Id).Intersect(alices.Questions.Select(question => question.Id)));
        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.False(Query(db => db.Attempts.IgnoreQueryFilters().Single(attempt => attempt.Id == alices.Questions[0].Id).Answered));
    }

    // Like POST /attempts: a topic the caller doesn't have looks like none.
    [Fact]
    public async Task UnknownOrForeignTopic_IsNotFound()
    {
        var alice = User.New("alice");
        var bob = User.New("bob");
        var bobsTopic = await AddTopicAsync(bob, questions: 3);

        var (unknown, _) = await OpenAsync(alice, Guid.NewGuid(), Guid.NewGuid(), size: 3);
        var (foreign, _) = await OpenAsync(alice, bobsTopic, Guid.NewGuid(), size: 3);

        Assert.Equal(HttpStatusCode.NotFound, unknown);
        Assert.Equal(HttpStatusCode.NotFound, foreign);
        Assert.Equal(0, Query(db => db.BlockQuizzes.IgnoreQueryFilters().Count(quiz => quiz.OwnerId == alice.Id)));
    }

    [Fact]
    public async Task StartsAtTheSameTime_LeaveOneQuiz()
    {
        var alice = User.New("alice");
        var topic = await AddTopicAsync(alice, questions: 7);
        var block = Guid.NewGuid();

        var opened = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => OpenAsync(alice, topic, block, size: 5)));

        Assert.All(opened, entry => Assert.Equal(HttpStatusCode.OK, entry.Status));
        Assert.Single(opened.Select(entry => entry.Quiz!.Id).Distinct());
        Assert.Equal(1, Query(db => db.BlockQuizzes.IgnoreQueryFilters().Count(quiz => quiz.OwnerId == alice.Id)));
        Assert.Equal(5, Query(db => db.Attempts.IgnoreQueryFilters().Count(attempt => attempt.OwnerId == alice.Id)));
    }

    // A pool smaller than the size gives what there is, an empty one a conflict like POST /attempts, and another topic
    // on a block with a quiz is the caller's mistake.
    [Fact]
    public async Task SmallPool_EmptyPool_AndOtherTopic()
    {
        var alice = User.New("alice");
        var small = await AddTopicAsync(alice, questions: 2);
        var empty = await AddTopicAsync(alice, questions: 0);
        var block = Guid.NewGuid();

        var (_, quiz) = await OpenAsync(alice, small, block, size: 5);
        var (emptyStatus, _) = await OpenAsync(alice, empty, Guid.NewGuid(), size: 5);
        var (otherStatus, _) = await OpenAsync(alice, empty, block, size: 5);

        Assert.Equal(2, quiz!.Questions.Count);
        Assert.Equal(HttpStatusCode.Conflict, emptyStatus);
        Assert.Equal(HttpStatusCode.Conflict, otherStatus);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task SizeOutOfRange_IsRejected(int size)
    {
        var alice = User.New("alice");
        var topic = await AddTopicAsync(alice, questions: 3);

        var (status, _) = await OpenAsync(alice, topic, Guid.NewGuid(), size);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    private sealed record User(string Id, string Token)
    {
        public static User New(string name)
        {
            var id = $"{name}-{Guid.NewGuid():N}";
            return new(id, TestTokens.Create(id));
        }
    }

    private sealed record QuizDto(Guid Id, Guid BlockId, Guid TopicId, List<QuestionDto> Questions);

    private sealed record QuestionDto(Guid Id, Guid QuestionId, bool Answered, bool? Correct);

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

    private async Task<(HttpStatusCode Status, QuizDto? Quiz)> OpenAsync(User user, Guid topicId, Guid blockId, int size)
    {
        using var client = ClientFor(user);
        using var response = await client.PostAsJsonAsync("/quizzes", new { topicId, blockId, size }, TestContext.Current.CancellationToken);
        return response.IsSuccessStatusCode
            ? (response.StatusCode, await response.Content.ReadFromJsonAsync<QuizDto>(TestContext.Current.CancellationToken))
            : (response.StatusCode, null);
    }

    private async Task AnswerAsync(User user, Guid attemptId, int option)
    {
        using var client = ClientFor(user);
        using var response = await client.PostAsJsonAsync(
            $"/attempts/{attemptId}/answer", new { option, responseTimeMs = 0 }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private T Query<T>(Func<AssessmentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<AssessmentDbContext>());
    }
}