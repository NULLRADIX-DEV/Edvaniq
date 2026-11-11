using Microsoft.Extensions.AI;

namespace Edvaniq.BuildingBlocks.AI.UnitTests;

// A real provider without its settings stops the start, and the message names the setting, never its value.
public class OpenAiCompatibleSettingsTests
{
    [Theory]
    [InlineData("Ai:OpenAiCompatible:Endpoint", "Ai:OpenAiCompatible:Endpoint is missing.")]
    [InlineData("Ai:OpenAiCompatible:Model", "Ai:OpenAiCompatible:Model is missing.")]
    [InlineData("Ai:OpenAiCompatible:ApiKey", "Ai:OpenAiCompatible:ApiKey is missing.")]
    public void MissingSetting_StopsTheStart(string key, string message) =>
        Assert.Equal(message, Assert.Throws<InvalidOperationException>(() => new AiStub().Host(overrides: (key, " "))).Message);

    [Theory]
    [InlineData("http://ai.test/api/v1")]
    [InlineData("ai.test/api/v1")]
    public void EndpointWithoutHttps_StopsTheStart(string endpoint) =>
        Assert.Equal(
            "Ai:OpenAiCompatible:Endpoint must be an absolute https address.",
            Assert.Throws<InvalidOperationException>(() => new AiStub().Host(overrides: ("Ai:OpenAiCompatible:Endpoint", endpoint))).Message);

    [Theory]
    [InlineData("""["provider"]""")]
    [InlineData("""{"provider":""")]
    [InlineData("""sk-not-json""")]
    public void ExtraBodyNotAnObject_StopsTheStart(string extraBody)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new AiStub().Host(overrides: ("Ai:OpenAiCompatible:ExtraBody", extraBody)));

        Assert.Equal("Ai:OpenAiCompatible:ExtraBody must be a JSON object.", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData("""{"model":"x"}""", "model")]
    [InlineData("""{"messages":[]}""", "messages")]
    [InlineData("""{"stream":true}""", "stream")]
    [InlineData("""{"provider":{},"User":"u-1"}""", "User")]
    public void ExtraBodyWithReservedField_StopsTheStart(string extraBody, string field) =>
        Assert.Equal(
            $"Ai:OpenAiCompatible:ExtraBody must not set {field}.",
            Assert.Throws<InvalidOperationException>(() => new AiStub().Host(overrides: ("Ai:OpenAiCompatible:ExtraBody", extraBody))).Message);

    [Fact]
    public async Task NoExtraBody_SendsOnlyModelAndMessages()
    {
        var stub = new AiStub().Answer(System.Net.HttpStatusCode.OK, AiStub.Completion("ok"));
        using var host = stub.Host(overrides: ("Ai:OpenAiCompatible:ExtraBody", null));

        await AiStub.Client(host).GetResponseAsync([new ChatMessage(ChatRole.User, "Hallo")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ["messages", "model"],
            System.Text.Json.Nodes.JsonNode.Parse(Assert.Single(stub.Requests).Body)!.AsObject().Select(field => field.Key).Order());
    }
}