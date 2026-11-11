using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Assessment.Contracts;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Knowledge.Contracts;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;

using Mode = Edvaniq.Services.Assessment.Contracts.Mode;

namespace Edvaniq.EventFlowTests;

// #205: the broker doesn't promise an order across senders. The same events reach Assessment and LearningEngine once in
// the order they were sent and once the other way round, questions and answers before TopicsChanged names their topic.
// Nothing gets lost, and both users end up with the same questions and the same mastery.
[Collection(FlowCollection.Name)]
public sealed class OrderFlowTests(FlowServices services)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private static readonly DateTime AnsweredAt = DateTime.UtcNow.AddMinutes(-10);

    [Fact(Timeout = 180_000)]
    public async Task EventsBeforeTheirTopic_EndLikeEventsInOrder()
    {
        await EnsureStreamsAsync(TestContext.Current.CancellationToken);
        var inOrder = Events.For($"alice-{Guid.NewGuid():N}");
        var reversed = Events.For($"bob-{Guid.NewGuid():N}");

        await PublishAsync(inOrder.Topics);
        await WaitForAsync(() => KnownInLearningEngine(inOrder.User) == 2, "LearningEngine did not take the topics of the first user over.");
        await PublishAsync(inOrder.Questions);
        foreach (var answer in inOrder.Answers)
        {
            await PublishAsync(answer);
        }

        // The other way round: answers and questions first. LearningEngine keeps the answers without a topic to show
        // them on, Assessment has the questions delivered again until the topic is known.
        foreach (var answer in reversed.Answers)
        {
            await PublishAsync(answer);
        }

        await PublishAsync(reversed.Questions);
        await WaitForAsync(() => Records(reversed.User) == reversed.Answers.Count, "LearningEngine did not keep the early answers.");
        Assert.Empty(await MasteryAsync(reversed.User));
        Assert.Empty(Questions(reversed.User));
        await PublishAsync(reversed.Topics);

        foreach (var events in new[] { inOrder, reversed })
        {
            await WaitForAsync(() => Questions(events.User).Count == 1, $"Assessment did not take the questions of {events.User} over.");
            await WaitForAsync(async () => (await MasteryAsync(events.User)).Count == 2, $"LearningEngine shows no mastery for {events.User}.");
        }

        Assert.Equal(Questions(inOrder.User), Questions(reversed.User));
        Assert.Equal(
            await ByPositionAsync(inOrder),
            await ByPositionAsync(reversed));
        var (practised, untouched) = ((await ByPositionAsync(reversed))[0], (await ByPositionAsync(reversed))[1]);
        Assert.NotEqual(50, practised.Mastery);
        Assert.Equal(50, untouched.Mastery);
    }

    private async Task EnsureStreamsAsync(CancellationToken cancellationToken)
    {
        foreach (var service in new[] { "knowledge", "content", "assessment" })
        {
            await services.Broker.EnsureStreamAsync(
                new StreamSpec(EventRoute.StreamOf(service), EventRoute.SubjectsOf(service)), cancellationToken);
        }
    }

    private async Task PublishAsync<TEvent>(TEvent integrationEvent)
        where TEvent : notnull =>
        await services.Broker.PublishAsync(
            new OutgoingMessage(
                EventRoute.For(typeof(TEvent)).Subject, Guid.NewGuid(), JsonSerializer.Serialize(integrationEvent, JsonSerializerOptions.Web)),
            TestContext.Current.CancellationToken);

    private int KnownInLearningEngine(string user) =>
        services.QueryLearningEngine(db => db.KnownTopics.IgnoreQueryFilters().Count(topic => topic.OwnerId == user));

    private int Records(string user) =>
        services.QueryLearningEngine(db => db.LearningRecords.IgnoreQueryFilters().Count(record => record.OwnerId == user));

    // Text and topic position of the user's questions, the same for both users.
    private List<(string Text, int Position)> Questions(string user) =>
        [.. services.QueryAssessment(db => db.Questions.IgnoreQueryFilters()
            .Where(question => question.OwnerId == user)
            .Select(question => new { question.Text, question.TopicPosition })
            .ToList())
            .Select(question => (question.Text, question.TopicPosition))
            .OrderBy(question => question.TopicPosition)];

    private async Task<List<MasteryEntry>> MasteryAsync(string user)
    {
        using var client = services.LearningEngine.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        var list = await client.GetFromJsonAsync<MasteryList>("/mastery", TestContext.Current.CancellationToken);
        return [.. list?.Topics ?? []];
    }

    // Mastery and state in the order of the user's topics, their ids differ between the users.
    private async Task<List<(int Mastery, string State)>> ByPositionAsync(Events events)
    {
        var entries = (await MasteryAsync(events.User)).ToDictionary(entry => entry.TopicId);
        return [.. events.TopicIds.Select(id => (entries[id].Mastery, entries[id].State))];
    }

    private static async Task WaitForAsync(Func<bool> condition, string because) =>
        await WaitForAsync(() => Task.FromResult(condition()), because);

    private static async Task WaitForAsync(Func<Task<bool>> condition, string because)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, because);
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
    }

    private sealed record MasteryList(int RuleVersion, IReadOnlyList<MasteryEntry> Topics);

    private sealed record MasteryEntry(Guid TopicId, int Mastery, string State);

    // One user's events: two topics, the questions of the first, and three answers on it.
    private sealed record Events(
        string User, IReadOnlyList<Guid> TopicIds, TopicsChanged Topics, ItemsGenerated Questions, IReadOnlyList<AnswerRecorded> Answers)
    {
        public static Events For(string user)
        {
            var (first, second) = (Guid.NewGuid(), Guid.NewGuid());
            var document = Guid.NewGuid();
            var item = Guid.NewGuid();
            var question = new GeneratedQuestion("Was spaltet Licht?", ["Wasser", "Zucker", "Sauerstoff"], 0, "Licht spaltet Wasser.", 2);
            return new(
                user,
                [first, second],
                new TopicsChanged(user, Guid.NewGuid(), DateTime.UtcNow, [first, second], []),
                new ItemsGenerated(
                    user, document, 0, [new GeneratedItem(item, ItemKind.Question, document, Guid.NewGuid(), "sim", "questions-1", question)], first),
                [
                    Answer(user, first, item, correct: true, minutes: 0),
                    Answer(user, first, item, correct: false, minutes: 1),
                    Answer(user, first, item, correct: true, minutes: 2),
                ]);
        }

        private static AnswerRecorded Answer(string user, Guid topic, Guid item, bool correct, int minutes) =>
            new(AnswerRecorded.CurrentVersion, user, topic, item, null, Guid.NewGuid(), correct, 2, 4000, null, Mode.Practice,
                AnsweredAt.AddMinutes(minutes));
    }
}