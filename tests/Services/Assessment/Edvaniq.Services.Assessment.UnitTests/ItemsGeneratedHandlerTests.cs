using Edvaniq.BuildingBlocks.Application;
using Edvaniq.Services.Assessment.Application;
using Edvaniq.Services.Assessment.Domain;
using Edvaniq.Services.Content.Contracts;

using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.Assessment.UnitTests;

// The handler takes over each new question of the event, leaves known ones, cards and broken items alone.
public class ItemsGeneratedHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid TopicId = Guid.NewGuid();

    private readonly FakeQuestions questions = new();
    private readonly TopicsChangedHandlerTests.FakeKnownTopics topics = new();
    private readonly FakeLogger logger = new();

    public ItemsGeneratedHandlerTests() => topics.Add(KnownTopic.Create("alice", TopicId, Guid.Empty, hidden: false));

    [Fact]
    public async Task NewQuestion_IsAddedWithUserTopicAndEveryField()
    {
        var item = Item();
        var generated = Event(item);

        await HandleAsync(generated);

        var question = Assert.Single(questions.Added);
        var source = item.Question!;
        Assert.Equal(
            (item.ItemId, "alice", TopicId, generated.DocumentId, 3, item.SectionId),
            (question.Id, question.OwnerId, question.TopicId, question.DocumentId, question.TopicPosition, question.SectionId));
        Assert.Equal(
            (source.Text, source.Answer, source.Explanation, source.Difficulty, item.ModelId, item.PromptVersion),
            (question.Text, question.Answer, question.Explanation, question.Difficulty, question.ModelId, question.PromptVersion));
        Assert.Equal(source.Options, question.Options);
        Assert.Equal(Now.UtcDateTime, question.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, question.CreatedAt.Kind);
    }

    // TopicsChanged is still on its way: the broker brings the message again later.
    [Fact]
    public async Task TopicNotKnownYet_WaitsForTheTopic_AndAddsNothing()
    {
        var generated = Event(Item()) with { TopicId = Guid.NewGuid() };

        await Assert.ThrowsAsync<MessageNotReadyException>(() => HandleAsync(generated));

        Assert.Empty(questions.Added);
    }

    // Another user's topic is as unknown as none.
    [Fact]
    public async Task TopicOfAnotherUser_IsNotKnown()
    {
        var generated = Event(Item()) with { UserId = "bob" };

        await Assert.ThrowsAsync<MessageNotReadyException>(() => HandleAsync(generated));

        Assert.Empty(questions.Added);
    }

    // Sent before Content knew the topicId: there is no topic to put the questions to.
    [Fact]
    public async Task EventWithoutTopic_IsDroppedWithALogLine()
    {
        var generated = Event(Item()) with { TopicId = null };

        await HandleAsync(generated);

        Assert.Empty(questions.Added);
        Assert.Contains(generated.DocumentId.ToString(), Assert.Single(logger.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task KnownQuestion_IsNotAddedAgain_TheNewOneIs()
    {
        var known = Item();
        var fresh = Item();
        questions.Known.Add(known.ItemId);

        await HandleAsync(Event(known, fresh));

        Assert.Equal([fresh.ItemId], questions.Added.Select(question => question.Id));
    }

    [Fact]
    public async Task SameItemTwiceInOneEvent_IsAddedOnce()
    {
        var item = Item();

        await HandleAsync(Event(item, item));

        Assert.Single(questions.Added);
    }

    [Fact]
    public async Task Card_IsLeftToFlashcards()
    {
        await HandleAsync(Event(Item() with { Kind = (ItemKind)1, Question = null }));

        Assert.Empty(questions.Added);
        Assert.Empty(logger.Lines);
    }

    [Fact]
    public async Task HigherPromptVersion_IsTakenAsItIs()
    {
        await HandleAsync(Event(Item() with { PromptVersion = "questions-7" }));

        Assert.Equal("questions-7", Assert.Single(questions.Added).PromptVersion);
    }

    public static TheoryData<string> BrokenItems =>
    [
        "no question",
        "answer past the options",
        "negative answer",
        "no options",
        "one option",
        "blank option",
        "empty text",
        "no explanation",
        "difficulty 0",
        "difficulty 4",
        "text too long",
    ];

    // A broken item is left out with a line that names it but not its content, the rest of the topic still arrives.
    [Theory]
    [MemberData(nameof(BrokenItems))]
    public async Task BrokenItem_IsLeftOutWithALogLine_TheOthersArrive(string broken)
    {
        var good = Item();
        var bad = Broken(Item(), broken);

        await HandleAsync(Event(bad, good));

        Assert.Equal([good.ItemId], questions.Added.Select(question => question.Id));
        var line = Assert.Single(logger.Lines);
        Assert.Contains(bad.ItemId.ToString(), line, StringComparison.Ordinal);
        Assert.DoesNotContain(good.Question!.Text, line, StringComparison.Ordinal);
        Assert.DoesNotContain("Option", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Erklärung", line, StringComparison.Ordinal);
    }

    private static GeneratedItem Broken(GeneratedItem item, string broken)
    {
        var question = item.Question!;
        return broken switch
        {
            "no question" => item with { Question = null },
            "answer past the options" => item with { Question = question with { Answer = question.Options.Count } },
            "negative answer" => item with { Question = question with { Answer = -1 } },
            "no options" => item with { Question = question with { Options = [], Answer = 0 } },
            "one option" => item with { Question = question with { Options = ["Option A"], Answer = 0 } },
            "blank option" => item with { Question = question with { Options = ["Option A", " "] } },
            "empty text" => item with { Question = question with { Text = "" } },
            "no explanation" => item with { Question = question with { Explanation = " " } },
            "difficulty 0" => item with { Question = question with { Difficulty = 0 } },
            "difficulty 4" => item with { Question = question with { Difficulty = 4 } },
            "text too long" => item with { Question = question with { Text = new string('x', Question.TextMaxLength + 1) } },
            _ => throw new ArgumentOutOfRangeException(nameof(broken)),
        };
    }

    private Task HandleAsync(ItemsGenerated generated) =>
        new ItemsGeneratedHandler(questions, topics, new FixedClock(), logger)
            .HandleAsync(generated, TestContext.Current.CancellationToken);

    private static ItemsGenerated Event(params GeneratedItem[] items) => new("alice", Guid.NewGuid(), 3, items, TopicId);

    private static GeneratedItem Item() =>
        new(Guid.NewGuid(), ItemKind.Question, Guid.NewGuid(), Guid.NewGuid(), "simulated", "questions-1",
            new GeneratedQuestion($"Frage {Guid.NewGuid():N}", ["Option A", "Option B", "Option C"], 1, "Erklärung", 2));

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeQuestions : IQuestions
    {
        public HashSet<Guid> Known { get; } = [];

        public List<Question> Added { get; } = [];

        public Task<IReadOnlySet<Guid>> KnownAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(ids.Where(Known.Contains).ToHashSet());

        public void Add(Question question) => Added.Add(question);
    }

    private sealed class FakeLogger : ILogger<ItemsGeneratedHandler>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }
}