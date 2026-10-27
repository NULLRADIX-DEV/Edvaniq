using System.Net;
using System.Text.Json;

using Edvaniq.BuildingBlocks.AI;
using Edvaniq.Services.Content.Infrastructure.Ai;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Content.IntegrationTests;

// Content's AI runs without a key on the simulated provider, which answers topics and questions the same way for the
// same material. No database needed, so these run without Docker too.
public sealed class SimulatedAiTests
{
    private const string Material = """
        # Photosynthese
        ## Lichtreaktion
        Licht spaltet Wasser. Dabei entsteht Sauerstoff.
        ## Calvin-Zyklus
        CO2 wird zu Zucker.
        # Zellatmung
        Zucker wird zu Energie.
        """;

    [Fact]
    public async Task Topics_ComeFromTheHeadings()
    {
        using var host = Build();

        var answer = await AskAsync(host, ContentAi.TopicsTask, Material);

        Assert.Equal(
            """{"topics":[{"title":"Photosynthese","subtopics":["Lichtreaktion","Calvin-Zyklus"]},{"title":"Zellatmung","subtopics":[]}]}""",
            answer);
    }

    [Fact]
    public async Task Topics_WithoutHeadings_ComeFromTheLines()
    {
        using var host = Build();

        var answer = await AskAsync(host, ContentAi.TopicsTask, "Licht spaltet Wasser in Teile.\nZucker wird zu Energie.");

        Assert.Equal(
            """{"topics":[{"title":"Licht spaltet Wasser","subtopics":[]},{"title":"Zucker wird zu","subtopics":[]}]}""",
            answer);
    }

    [Fact]
    public async Task Questions_AreFifteenToTwenty_AndTheSameForTheSameMaterial()
    {
        using var host = Build();
        using var other = Build();

        var answer = await AskAsync(host, ContentAi.QuestionsTask, Material);

        Assert.Equal(answer, await AskAsync(other, ContentAi.QuestionsTask, Material));
        var questions = JsonDocument.Parse(answer).RootElement.GetProperty("questions").EnumerateArray().ToList();
        Assert.InRange(questions.Count, 15, 20);
        Assert.All(questions, question =>
        {
            Assert.StartsWith("Stimmt das: ", question.GetProperty("text").GetString());
            Assert.InRange(question.GetProperty("difficulty").GetInt32(), 1, 3);
            Assert.Equal(0, question.GetProperty("answer").GetInt32());
        });
    }

    [Fact]
    public async Task Faults_ComeFromTheConfiguration()
    {
        using var outage = Build(SimulatedFault.Outage);
        using var invalid = Build(SimulatedFault.InvalidOutput);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => AskAsync(outage, ContentAi.TopicsTask, Material));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        var cut = await AskAsync(invalid, ContentAi.TopicsTask, Material);
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(cut));
    }

    private static IHost Build(SimulatedFault fault = SimulatedFault.None)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(
        [
            KeyValuePair.Create("Ai:Provider", (string?)"Simulated"),
            KeyValuePair.Create("Ai:Simulated:Fault", (string?)fault.ToString()),
        ]);
        builder.AddContentAi();
        return builder.Build();
    }

    private static async Task<string> AskAsync(IHost host, string task, string material)
    {
        var response = await host.Services.GetRequiredService<IChatClient>().GetResponseAsync(
            [new ChatMessage(ChatRole.System, $"{task}\nAntworte als JSON."), new ChatMessage(ChatRole.User, material)],
            cancellationToken: TestContext.Current.CancellationToken);
        return response.Text;
    }
}
