using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Contracts;
using Edvaniq.Services.Assessment.Application;
using Edvaniq.Services.Assessment.Contracts;
using Edvaniq.Services.Assessment.Domain;


namespace Edvaniq.Services.Assessment.UnitTests;

public sealed class QuizTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);

    private readonly FakeAttempts attempts = new();
    private readonly FakeOutbox outbox = new();
    private readonly Clock time = new();

    public QuizTests() => attempts.Outbox = outbox;

    private Quiz Quiz => new(attempts, outbox, time);

    [Fact]
    public async Task Ask_PicksAQuestionOfTheTopic()
    {
        var topicId = Guid.NewGuid();
        var question = AddTopicWithQuestion(topicId);

        var asked = await Quiz.AskAsync(topicId, TestContext.Current.CancellationToken);

        Assert.Equal(AskOutcome.Asked, asked.Outcome);
        Assert.Equal((question.Id, topicId, Now), (asked.Attempt!.QuestionId, asked.Attempt.TopicId, asked.Attempt.AskedAt));
        Assert.Same(asked.Attempt, Assert.Single(attempts.Stored));
        Assert.Empty(outbox.Events);
    }

    [Fact]
    public async Task Ask_ForATopicTheUserDoesNotHave_IsUnknown()
    {
        var asked = await Quiz.AskAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(AskOutcome.UnknownTopic, asked.Outcome);
        Assert.Empty(attempts.Stored);
    }

    [Fact]
    public async Task Ask_ForATopicWithoutQuestionsYet_SaysSo()
    {
        var topicId = Guid.NewGuid();
        attempts.KnownTopics.Add(topicId);

        Assert.Equal(AskOutcome.NoQuestions, (await Quiz.AskAsync(topicId, TestContext.Current.CancellationToken)).Outcome);
    }

    [Fact]
    public async Task Answer_IsGradedAndRecordedOnce()
    {
        var topicId = Guid.NewGuid();
        var question = AddTopicWithQuestion(topicId);
        var attempt = (await Quiz.AskAsync(topicId, TestContext.Current.CancellationToken)).Attempt!;
        time.Advance(TimeSpan.FromSeconds(4));

        var graded = await Quiz.AnswerAsync(attempt.Id, 1, 3_000, TestContext.Current.CancellationToken);
        var again = await Quiz.AnswerAsync(attempt.Id, 0, 9_000, TestContext.Current.CancellationToken);

        Assert.Equal(GradeOutcome.Graded, graded.Outcome);
        Assert.True(graded.Attempt!.Correct);
        Assert.Equal((GradeOutcome.Graded, 1, true, 3_000), (again.Outcome, again.Attempt!.Option, again.Attempt.Correct, again.Attempt.ResponseTimeMs));
        var recorded = Assert.IsType<AnswerRecorded>(Assert.Single(outbox.Events));
        Assert.Equal(
            new AnswerRecorded(1, "alice", topicId, question.Id, null, attempt.Id, true, 2, 3_000, null, Mode.Practice, Now.AddSeconds(4)),
            recorded);
    }

    [Fact]
    public async Task Answer_WithAnOptionTheQuestionDoesNotHave_IsRejected()
    {
        var topicId = Guid.NewGuid();
        AddTopicWithQuestion(topicId);
        var attempt = (await Quiz.AskAsync(topicId, TestContext.Current.CancellationToken)).Attempt!;

        var graded = await Quiz.AnswerAsync(attempt.Id, 5, 1_000, TestContext.Current.CancellationToken);

        Assert.Equal(GradeOutcome.InvalidOption, graded.Outcome);
        Assert.False(attempt.Answered);
        Assert.Empty(outbox.Events);
    }

    [Fact]
    public async Task Answer_ToAnUnknownAttempt_IsNotFound() =>
        Assert.Equal(GradeOutcome.NotFound, (await Quiz.AnswerAsync(Guid.NewGuid(), 0, 1_000, TestContext.Current.CancellationToken)).Outcome);

    // The other request saved first: its answer is what this one gets back.
    [Fact]
    public async Task Answer_ThatLosesTheRace_ReturnsTheStoredAnswer()
    {
        var topicId = Guid.NewGuid();
        var question = AddTopicWithQuestion(topicId);
        var attempt = (await Quiz.AskAsync(topicId, TestContext.Current.CancellationToken)).Attempt!;
        var winner = Attempt.Ask(question, topicId, Now);
        typeof(Attempt).GetProperty(nameof(Attempt.Id))!.SetValue(winner, attempt.Id);
        winner.Answer(question, 0, 2_000, Now.AddSeconds(3));
        attempts.LoseNextSaveTo(winner);
        time.Advance(TimeSpan.FromSeconds(4));

        var graded = await Quiz.AnswerAsync(attempt.Id, 1, 3_000, TestContext.Current.CancellationToken);

        Assert.Equal((GradeOutcome.Graded, 0, false), (graded.Outcome, graded.Attempt!.Option, graded.Attempt.Correct));
    }

    // Showing the solution is recorded wrong with the measured time, once, and an answer after it changes nothing.
    [Fact]
    public async Task Reveal_IsRecordedWrongOnce_AndAnAnswerAfterItGetsItBack()
    {
        var topicId = Guid.NewGuid();
        var question = AddTopicWithQuestion(topicId);
        var attempt = (await Quiz.AskAsync(topicId, TestContext.Current.CancellationToken)).Attempt!;
        time.Advance(TimeSpan.FromSeconds(6));

        var revealed = await Quiz.RevealAsync(attempt.Id, 5_000, TestContext.Current.CancellationToken);
        var answered = await Quiz.AnswerAsync(attempt.Id, 1, 1_000, TestContext.Current.CancellationToken);

        Assert.Equal((GradeOutcome.Graded, true, false), (revealed.Outcome, revealed.Attempt!.Revealed, revealed.Attempt.Correct));
        Assert.Equal((GradeOutcome.Graded, true, false, (int?)null), (answered.Outcome, answered.Attempt!.Revealed, answered.Attempt.Correct, answered.Attempt.Option));
        Assert.Equal(
            new AnswerRecorded(1, "alice", topicId, question.Id, null, attempt.Id, false, 2, 5_000, null, Mode.Practice, Now.AddSeconds(6)),
            Assert.IsType<AnswerRecorded>(Assert.Single(outbox.Events)));
    }

    [Fact]
    public async Task Reveal_AfterAnAnswer_GetsTheAnswerBack()
    {
        var topicId = Guid.NewGuid();
        AddTopicWithQuestion(topicId);
        var attempt = (await Quiz.AskAsync(topicId, TestContext.Current.CancellationToken)).Attempt!;

        await Quiz.AnswerAsync(attempt.Id, 1, 1_000, TestContext.Current.CancellationToken);
        var revealed = await Quiz.RevealAsync(attempt.Id, 2_000, TestContext.Current.CancellationToken);

        Assert.Equal((false, true, 1), (revealed.Attempt!.Revealed, revealed.Attempt.Correct, revealed.Attempt.Option));
        Assert.Single(outbox.Events);
    }

    [Fact]
    public async Task MultipleChoice_IsGradedBySet()
    {
        var topicId = Guid.NewGuid();
        attempts.KnownTopics.Add(topicId);
        attempts.Questions.Add(AttemptTests.MultipleChoice("alice", topicId));
        var right = (await Quiz.AskAsync(topicId, TestContext.Current.CancellationToken)).Attempt!;
        var half = (await Quiz.AskAsync(topicId, TestContext.Current.CancellationToken)).Attempt!;

        var rightGrade = await Quiz.AnswerAsync(right.Id, [2, 0], 1_000, TestContext.Current.CancellationToken);
        var halfGrade = await Quiz.AnswerAsync(half.Id, [0], 1_000, TestContext.Current.CancellationToken);
        var empty = await Quiz.AnswerAsync(Guid.NewGuid(), [], 1_000, TestContext.Current.CancellationToken);

        Assert.True(rightGrade.Attempt!.Correct);
        Assert.False(halfGrade.Attempt!.Correct);
        Assert.Equal(GradeOutcome.NotFound, empty.Outcome);
        Assert.Equal([true, false], outbox.Events.Cast<AnswerRecorded>().Select(record => record.Correct));
    }

    [Fact]
    public async Task MultipleChoice_WithoutAnyOption_IsRejected()
    {
        var topicId = Guid.NewGuid();
        attempts.KnownTopics.Add(topicId);
        attempts.Questions.Add(AttemptTests.MultipleChoice("alice", topicId));
        var attempt = (await Quiz.AskAsync(topicId, TestContext.Current.CancellationToken)).Attempt!;

        Assert.Equal(GradeOutcome.InvalidOption, (await Quiz.AnswerAsync(attempt.Id, [], 1_000, TestContext.Current.CancellationToken)).Outcome);
        Assert.False(attempt.Answered);
    }

    private Question AddTopicWithQuestion(Guid topicId)
    {
        attempts.KnownTopics.Add(topicId);
        var question = AttemptTests.Question("alice", topicId);
        attempts.Questions.Add(question);
        attempts.Questions.Add(AttemptTests.Question("alice"));
        return question;
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = Now;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }

    private sealed class FakeOutbox : IEventOutbox
    {
        private readonly List<IIntegrationEvent> pending = [];

        public List<IIntegrationEvent> Events { get; } = [];

        public void Add(IIntegrationEvent integrationEvent) => pending.Add(integrationEvent);

        public void Commit()
        {
            Events.AddRange(pending);
            pending.Clear();
        }

        public void Discard() => pending.Clear();
    }

    private sealed class FakeAttempts : IAttempts
    {
        private Attempt? winner;

        public FakeOutbox? Outbox { get; set; }

        public List<Question> Questions { get; } = [];

        public List<Attempt> Stored { get; } = [];

        public void LoseNextSaveTo(Attempt attempt) => winner = attempt;

        public HashSet<Guid> KnownTopics { get; } = [];

        public Task<bool> KnowsTopicAsync(Guid topicId, CancellationToken cancellationToken) => Task.FromResult(KnownTopics.Contains(topicId));

        public Task<IReadOnlyList<Question>> QuestionsOfAsync(Guid topicId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Question>>([.. Questions.Where(question => question.TopicId == topicId)]);

        public Task<Question?> FindQuestionAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Questions.FirstOrDefault(question => question.Id == id));

        public Task<Attempt?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Stored.FirstOrDefault(attempt => attempt.Id == id));

        public void Add(Attempt attempt) => Stored.Add(attempt);

        public Task<bool> SaveAsync(CancellationToken cancellationToken)
        {
            if (winner is { } stored)
            {
                Stored.RemoveAll(attempt => attempt.Id == stored.Id);
                Stored.Add(stored);
                winner = null;
                Outbox?.Discard();
                return Task.FromResult(false);
            }

            Outbox?.Commit();
            return Task.FromResult(true);
        }
    }
}