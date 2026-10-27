using System.Net;
using System.Text.Json;

using Microsoft.Extensions.AI;

namespace Edvaniq.BuildingBlocks.AI.UnitTests;

// The simulated provider answers the same prompt the same way, in every process, and fails on purpose when asked to.
public class SimulatedChatClientTests
{
    private static readonly ChatMessage[] Prompt =
    [
        new(ChatRole.System, "Antworte kurz."),
        new(ChatRole.User, "Photosynthese"),
    ];

    // Pinned, so a seed from string.GetHashCode, which changes with every process, can't pass.
    [Fact]
    public void Seed_IsTheSameInEveryProcess() =>
        Assert.Equal(-1058907921, new SimulatedPrompt(Prompt).Seed);

    [Fact]
    public void Seed_KeepsTheMessagesApart() =>
        Assert.NotEqual(
            new SimulatedPrompt([new(ChatRole.User, "ab"), new(ChatRole.User, "c")]).Seed,
            new SimulatedPrompt([new(ChatRole.User, "a"), new(ChatRole.User, "bc")]).Seed);

    [Fact]
    public void Prompt_SplitsSystemAndUser()
    {
        var prompt = new SimulatedPrompt(Prompt);

        Assert.Equal(("Antworte kurz.", "Photosynthese"), (prompt.System, prompt.User));
    }

    [Fact]
    public async Task SamePrompt_GetsTheSameAnswer_FromAnotherClient()
    {
        var first = await new SimulatedChatClient([new Echo()]).GetResponseAsync(Prompt, cancellationToken: TestContext.Current.CancellationToken);
        var second = await new SimulatedChatClient([new Echo()]).GetResponseAsync(Prompt, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(first.Text, second.Text);
        Assert.Equal("""{"echo":"Photosynthese","number":""" + new Random(-1058907921).Next(100) + "}", first.Text);
        Assert.Equal(SimulatedChatClient.Provider, first.ModelId);
    }

    [Fact]
    public async Task OtherPrompt_GetsItsOwnAnswer()
    {
        var answer = await new SimulatedChatClient([new Echo()])
            .GetResponseAsync([new ChatMessage(ChatRole.User, "Zellatmung")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.StartsWith("""{"echo":"Zellatmung",""", answer.Text);
    }

    [Fact]
    public async Task Outage_FailsLikeAProviderAnswering503()
    {
        var client = new SimulatedChatClient([new Echo()], SimulatedFault.Outage);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetResponseAsync(Prompt, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
    }

    [Fact]
    public async Task Timeout_WaitsUntilTheCallerGivesUp()
    {
        var client = new SimulatedChatClient([new Echo()], SimulatedFault.Timeout);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetResponseAsync(Prompt, cancellationToken: timeout.Token));
    }

    [Fact]
    public async Task InvalidOutput_IsNoJson()
    {
        var client = new SimulatedChatClient([new Echo()], SimulatedFault.InvalidOutput);

        var answer = await client.GetResponseAsync(Prompt, cancellationToken: TestContext.Current.CancellationToken);

        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(answer.Text));
    }

    [Fact]
    public async Task Fault_CanBeSwitchedWhileRunning()
    {
        var client = new SimulatedChatClient([new Echo()], SimulatedFault.Outage);
        client.Fault = SimulatedFault.None;

        var answer = await client.GetResponseAsync(Prompt, cancellationToken: TestContext.Current.CancellationToken);

        Assert.StartsWith("""{"echo":""", answer.Text);
    }

    [Fact]
    public async Task PromptWithoutAnswer_IsATestSetupError()
    {
        var client = new SimulatedChatClient([]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync(Prompt, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("ISimulatedAnswer", error.Message);
    }

    [Fact]
    public async Task Streaming_GivesTheWholeAnswerAtOnce()
    {
        var updates = await new SimulatedChatClient([new Echo()])
            .GetStreamingResponseAsync(Prompt, cancellationToken: TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith("""{"echo":"Photosynthese",""", Assert.Single(updates).Text);
    }

    // Echoes the user message with a number from the prompt's randomness.
    private sealed class Echo : ISimulatedAnswer
    {
        public bool Answers(SimulatedPrompt prompt) => true;

        public string Answer(SimulatedPrompt prompt) =>
            JsonSerializer.Serialize(new { echo = prompt.User, number = prompt.Random().Next(100) });
    }
}
