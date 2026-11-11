using System.Net;

using Microsoft.Extensions.AI;

namespace Edvaniq.BuildingBlocks.AI.UnitTests;

// An unusable answer is asked again up to the limit, then the result is empty, never half of it. A provider that fails
// is not asked again. Every attempt counts towards the usage.
public class StructuredOutputTests
{
    private static readonly ChatMessage[] Prompt = [new(ChatRole.User, "Photosynthese")];

    private static readonly ChatResponseFormat Format = ChatResponseFormat.ForJsonSchema(
        System.Text.Json.JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone(), "answer");

    [Fact]
    public async Task UnusableThenUsable_TakesTwoAttempts_AndAddsTheirUsage()
    {
        var ai = new Scripted(Answer("kaputt", 10, 2), Answer("ok", 10, 3));

        var answer = await AskAsync(ai);

        Assert.Equal("ok", answer.Value);
        Assert.Null(answer.Failure);
        Assert.Equal(2, answer.Attempts);
        Assert.Equal(2, ai.Calls);
        Assert.Equal((20L, 5L, 25L), (answer.Usage.InputTokenCount, answer.Usage.OutputTokenCount, answer.Usage.TotalTokenCount));
    }

    [Fact]
    public async Task AlwaysUnusable_StopsAtTheLimit_WithoutAValue()
    {
        var ai = new Scripted(Answer("kaputt", 10, 2), Answer("kaputt", 10, 2), Answer("ok", 10, 2));

        var answer = await AskAsync(ai);

        Assert.Null(answer.Value);
        Assert.Null(answer.Failure);
        Assert.Equal(StructuredOutput.DefaultAttempts, answer.Attempts);
        Assert.Equal(StructuredOutput.DefaultAttempts, ai.Calls);
        Assert.Equal(24L, answer.Usage.TotalTokenCount);
    }

    [Fact]
    public async Task MaxAttempts_IsTheLimit()
    {
        var ai = new Scripted(Answer("kaputt", 1, 1), Answer("kaputt", 1, 1), Answer("ok", 1, 1));

        var answer = await AskAsync(ai, maxAttempts: 3);

        Assert.Equal(("ok", 3, 3), (answer.Value, answer.Attempts, ai.Calls));
    }

    // A retry after an outage would be another paid request, that is #300. The usage before it stays.
    [Fact]
    public async Task Outage_IsNotAskedAgain_AndKeepsTheUsageBefore()
    {
        var ai = new Scripted(
            Answer("kaputt", 10, 2),
            () => throw new HttpRequestException("down", null, HttpStatusCode.ServiceUnavailable));

        var answer = await AskAsync(ai, maxAttempts: 3);

        Assert.Null(answer.Value);
        Assert.IsType<HttpRequestException>(answer.Failure);
        Assert.Equal((2, 2), (answer.Attempts, ai.Calls));
        Assert.Equal(12L, answer.Usage.TotalTokenCount);
    }

    // The cost log keeps every attempt on its own: its tokens, cost, model and how it ended.
    [Fact]
    public async Task EveryAttempt_IsATry_WithItsOwnUsageCostAndOutcome()
    {
        var ai = new Scripted(
            Answer("kaputt", 10, 2, 0.5m),
            Answer("ok", 7, 3),
            () => throw new HttpRequestException("down", null, HttpStatusCode.ServiceUnavailable));

        var usable = await AskAsync(ai);
        var failed = await AskAsync(ai);

        Assert.Equal(
            [(1, "test", 10L, 2L, (decimal?)0.5m, AiTryOutcome.Unusable), (2, "test", 7L, 3L, null, AiTryOutcome.Answered)],
            usable.Tries.Select(attempt => (attempt.Number, attempt.ModelId, attempt.InputTokens, attempt.OutputTokens, attempt.Cost, attempt.Outcome)));
        Assert.Equal((1, null, 0L, 0L, (decimal?)null, AiTryOutcome.Failed), Tuple(Assert.Single(failed.Tries)));
        Assert.All(usable.Tries.Concat(failed.Tries), attempt =>
        {
            Assert.Equal(DateTimeKind.Utc, attempt.StartedAt.Kind);
            Assert.True(attempt.Duration >= TimeSpan.Zero);
        });
    }

    private static (int, string?, long, long, decimal?, AiTryOutcome) Tuple(AiTry attempt) =>
        (attempt.Number, attempt.ModelId, attempt.InputTokens, attempt.OutputTokens, attempt.Cost, attempt.Outcome);

    [Fact]
    public async Task SlowAnswer_FailsAfterTheAttemptTimeout()
    {
        var ai = new Scripted(() => throw new InvalidOperationException("hang"));

        var answer = await AskAsync(ai, timeout: TimeSpan.FromMilliseconds(100));

        Assert.IsAssignableFrom<OperationCanceledException>(answer.Failure);
        Assert.Equal(1, ai.Calls);
    }

    [Fact]
    public async Task StoppingHost_Throws()
    {
        var ai = new Scripted(() => throw new InvalidOperationException("hang"));
        using var stopping = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ai.AskAsync(Prompt, Read, new StructuredRequest(TimeSpan.FromMinutes(1)), TimeProvider.System, stopping.Token));
    }

    [Fact]
    public async Task ResponseFormat_GoesWithEveryAttempt()
    {
        var ai = new Scripted(Answer("kaputt", 1, 1), Answer("ok", 1, 1));

        await AskAsync(ai);

        Assert.All(ai.Options, options => Assert.Same(Format, options?.ResponseFormat));
    }

    private static Task<StructuredAnswer<string>> AskAsync(Scripted ai, int maxAttempts = StructuredOutput.DefaultAttempts, TimeSpan? timeout = null) =>
        ai.AskAsync(
            Prompt, Read, new StructuredRequest(timeout ?? TimeSpan.FromMinutes(1), Format, maxAttempts), TimeProvider.System,
            TestContext.Current.CancellationToken);

    private static string? Read(string text) => text == "ok" ? text : null;

    private static Func<ChatResponse> Answer(string text, long input, long output, decimal? cost = null) => () =>
        new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            ModelId = "test",
            Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output, TotalTokenCount = input + output },
            AdditionalProperties = cost is { } known ? new() { [AiCost.Property] = known } : null,
        };

    // Answers each call with the next answer. One that throws InvalidOperationException hangs until the caller gives up.
    private sealed class Scripted(params Func<ChatResponse>[] answers) : IChatClient
    {
        public int Calls { get; private set; }

        public List<ChatOptions?> Options { get; } = [];

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Options.Add(options);
            var answer = answers[Calls++];
            try
            {
                return answer();
            }
            catch (InvalidOperationException)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw;
            }
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}