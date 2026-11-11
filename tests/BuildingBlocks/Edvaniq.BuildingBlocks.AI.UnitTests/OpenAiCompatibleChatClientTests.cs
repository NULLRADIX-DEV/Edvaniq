using System.Net;
using System.Text.Json.Nodes;

using Edvaniq.Testing;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Edvaniq.BuildingBlocks.AI.UnitTests;

// The real provider behind Ai:Provider=OpenAiCompatible, against a stub: endpoint, model, key and the extra fields
// come only from the configuration, and the key never gets into an exception or the log.
public class OpenAiCompatibleChatClientTests
{
    private static readonly ChatMessage[] Prompt =
    [
        new(ChatRole.System, "edvaniq:topics"),
        new(ChatRole.User, "Photosynthese"),
    ];

    [Fact]
    public async Task Request_CarriesModelMessagesExtraBodyAndKey()
    {
        var stub = new AiStub().Answer(HttpStatusCode.OK, AiStub.Completion("ok"));
        using var host = stub.Host();

        await AiStub.Client(host).GetResponseAsync(Prompt, cancellationToken: TestContext.Current.CancellationToken);

        var (authorization, uri, body) = Assert.Single(stub.Requests);
        Assert.Equal($"Bearer {AiStub.Key}", authorization);
        Assert.Equal(new Uri("https://ai.test/api/v1/chat/completions"), uri);
        var request = JsonNode.Parse(body)!.AsObject();
        Assert.Equal("vendor/model-1", (string?)request["model"]);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""[{"role":"system","content":"edvaniq:topics"},{"role":"user","content":"Photosynthese"}]"""),
            request["messages"]));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"only":["one"],"zdr":true,"data_collection":"deny"}"""), request["provider"]));
        Assert.False(request.ContainsKey("user"));
        Assert.False(request.ContainsKey("stream"));
    }

    [Fact]
    public async Task SchemaFormat_GoesOutAsStrictJsonSchema()
    {
        var stub = new AiStub().Answer(HttpStatusCode.OK, AiStub.Completion("ok"));
        using var host = stub.Host(overrides: ("Ai:OpenAiCompatible:ExtraBody", """{"response_format":{"type":"text"}}"""));

        await AiStub.Client(host).GetResponseAsync(Prompt, new ChatOptions { ResponseFormat = Schema }, TestContext.Current.CancellationToken);

        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""{"type":"json_schema","json_schema":{"name":"topics","strict":true,"schema":{"type":"object","required":["topics"]}}}"""),
            JsonNode.Parse(Assert.Single(stub.Requests).Body)!["response_format"]));
    }

    [Fact]
    public async Task JsonFormatWithoutSchema_GoesOutAsJsonObject()
    {
        var stub = new AiStub().Answer(HttpStatusCode.OK, AiStub.Completion("ok"));
        using var host = stub.Host();

        await AiStub.Client(host).GetResponseAsync(Prompt, new ChatOptions { ResponseFormat = ChatResponseFormat.Json }, TestContext.Current.CancellationToken);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"type":"json_object"}"""), JsonNode.Parse(Assert.Single(stub.Requests).Body)!["response_format"]));
    }

    // Off for an endpoint that can't keep to a schema: without the switch every request would fail there.
    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("False", false)]
    public async Task SchemaOutput_SwitchesTheFormat(string? value, bool sent)
    {
        var stub = new AiStub().Answer(HttpStatusCode.OK, AiStub.Completion("ok"));
        using var host = stub.Host(overrides: ("Ai:OpenAiCompatible:SchemaOutput", value));

        await AiStub.Client(host).GetResponseAsync(Prompt, new ChatOptions { ResponseFormat = Schema }, TestContext.Current.CancellationToken);

        Assert.Equal(sent, JsonNode.Parse(Assert.Single(stub.Requests).Body)!.AsObject().ContainsKey("response_format"));
    }

    [Fact]
    public void SchemaOutputNoBool_StopsTheStart() =>
        Assert.Equal(
            "Ai:OpenAiCompatible:SchemaOutput must be true or false.",
            Assert.Throws<InvalidOperationException>(() => new AiStub().Host(overrides: ("Ai:OpenAiCompatible:SchemaOutput", "ja"))).Message);

    private static readonly ChatResponseFormat Schema = ChatResponseFormat.ForJsonSchema(
        System.Text.Json.JsonDocument.Parse("""{"type":"object","required":["topics"]}""").RootElement.Clone(), "topics");

    // The model is only configuration: another one goes out without a new build.
    [Fact]
    public async Task Model_ComesFromTheConfiguration()
    {
        var stub = new AiStub().Answer(HttpStatusCode.OK, AiStub.Completion("ok", "other/model-2"));
        using var host = stub.Host(overrides: ("Ai:OpenAiCompatible:Model", "other/model-2"));
        var client = AiStub.Client(host);

        var response = await client.GetResponseAsync(Prompt, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("other/model-2", (string?)JsonNode.Parse(Assert.Single(stub.Requests).Body)!["model"]);
        Assert.Equal("other/model-2", client.GetService<ChatClientMetadata>()!.DefaultModelId);
        Assert.Equal("other/model-2", response.ModelId);
    }

    [Fact]
    public async Task Answer_ReadsTextModelAndUsage()
    {
        var stub = new AiStub().Answer(HttpStatusCode.OK, AiStub.Completion("Licht wird zu Zucker.", "vendor/model-1-0101"));
        using var host = stub.Host();

        var response = await AiStub.Client(host).GetResponseAsync(Prompt, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Licht wird zu Zucker.", response.Text);
        Assert.Equal("vendor/model-1-0101", response.ModelId);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
        Assert.Equal(12, response.Usage?.InputTokenCount);
        Assert.Equal(3, response.Usage?.OutputTokenCount);
        Assert.Equal(15, response.Usage?.TotalTokenCount);
    }

    // One request only: a retry would be a second paid one, the caller decides what happens after a failure.
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task ErrorStatus_ThrowsOnceWithTheStatus(HttpStatusCode status)
    {
        var stub = new AiStub().Answer(status, """{"error":{"message":"Photosynthese"}}""");
        using var host = stub.Host();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            AiStub.Client(host).GetResponseAsync(Prompt, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(status, exception.StatusCode);
        Assert.DoesNotContain("Photosynthese", exception.ToString(), StringComparison.Ordinal);
        Assert.Single(stub.Requests);
    }

    [Theory]
    [InlineData("""{"error":{"message":"Photosynthese","code":502}}""")]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"choices":[{"message":{"content":null}}]}""")]
    [InlineData("""Photosynthese <html>""")]
    public async Task AnswerWithoutText_Throws(string body)
    {
        var stub = new AiStub().Answer(HttpStatusCode.OK, body);
        using var host = stub.Host();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AiStub.Client(host).GetResponseAsync(Prompt, cancellationToken: TestContext.Current.CancellationToken));

        Assert.DoesNotContain("Photosynthese", exception.ToString(), StringComparison.Ordinal);
    }

    // No timeout of the HttpClient or of the resilience: the time limit is the caller's.
    [Fact]
    public async Task SlowAnswer_WaitsForTheCaller()
    {
        var stub = new AiStub().Hang();
        using var host = stub.Host();
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AiStub.Client(host).GetResponseAsync(Prompt, cancellationToken: caller.Token));
        Assert.Single(stub.Requests);
    }

    // Success, an echo of the key in a rejection, an outage and an answer that is no use, each logged as Content logs
    // a failed call. Nowhere the key, not in a message, a value, a scope or an exception.
    [Fact]
    public async Task Calls_LogNoKey()
    {
        var stub = new AiStub()
            .Answer(HttpStatusCode.OK, AiStub.Completion("ok"))
            .Answer(HttpStatusCode.Unauthorized, $$$"""{"error":{"message":"Invalid key {{{AiStub.Key}}}"}}""")
            .Answer(HttpStatusCode.ServiceUnavailable, $$$"""{"error":{"message":"down for {{{AiStub.Key}}}"}}""")
            .Answer(HttpStatusCode.OK, $$$"""{"error":{"message":"{{{AiStub.Key}}}"}}""");
        var logs = new TestLogs();
        using var host = stub.Host(logs);
        var client = AiStub.Client(host);
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Content");

        for (var call = 0; call < 4; call++)
        {
            try
            {
                await client.GetResponseAsync(Prompt, cancellationToken: TestContext.Current.CancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The AI failed for document {DocumentId}.", Guid.NewGuid());
            }
        }

        Assert.Equal(4, stub.Requests.Count);
        Assert.Contains(logs.Entries, entry => entry.Text.Contains("ai.test", StringComparison.Ordinal));
        Assert.Equal(3, logs.Entries.Count(entry => entry.Category == "Content"));
        Assert.Empty(logs.Containing(AiStub.Key));
    }
}