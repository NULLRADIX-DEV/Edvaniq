using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Edvaniq.BuildingBlocks.AI;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Services.Content.Infrastructure.Ai;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Content.IntegrationTests;

// Nothing that points at a person reaches the AI provider (#312). The worker talks to the provider over HTTP, and the
// test records every request at that one exit: address, headers and body. So a new prompt is checked as soon as it goes
// out, and every prompt template of Content (a type with a PromptVersion) has to show up at least once.
public sealed class PromptPrivacyTests : IClassFixture<ServiceFactory>, IDisposable
{
    private const string Text = """
        # Photosynthese
        ## Lichtreaktion
        Licht spaltet Wasser, dabei entsteht Sauerstoff.

        ## Calvin-Zyklus
        Aus CO2 wird Zucker.

        # Zellatmung
        Zucker wird in den Mitochondrien zu Energie.
        """;

    private const string Name = "Maxima Mustermann";
    private const string Email = "maxima.mustermann@example.org";
    private const string Pseudonym = "MaxiStern42";
    private const string GoalName = "Abitur von Maxima Mustermann";
    private const string Title = "Notizen von Maxima Mustermann";

    private readonly ServiceFactory service;

    private readonly ProviderEndpoint provider = new();

    private readonly IHost ai;

    public PromptPrivacyTests(ServiceFactory service)
    {
        this.service = service;
        ai = ProviderAi(provider);
    }

    [Fact]
    public async Task EveryRequestToTheProvider_CarriesNothingOfTheUser()
    {
        var marker = Guid.NewGuid().ToString("N");
        var user = $"user-{marker}";
        service.Planning.Name(user, GoalName);
        var goal = service.Planning.GoalOf(user);
        using var client = ClientFor(user);
        var id = await PasteAsync(client);

        await AnalyzeWithQuestionsAsync(client, user, id);

        var requests = provider.Requests.ToList();
        Assert.NotEmpty(requests);
        string[] personal = [user, marker, Name, "Mustermann", Email, Pseudonym, GoalName, Title, goal.ToString(), goal.ToString("N")];
        Assert.All(requests, request => Assert.All(personal, value =>
            Assert.DoesNotContain(value, request.Everything, StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(requests, request => request.Body.Contains("Licht spaltet Wasser", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EveryPromptTemplate_GoesOutAndIsChecked()
    {
        var user = $"user-{Guid.NewGuid():N}";
        using var client = ClientFor(user);
        var id = await PasteAsync(client);

        await AnalyzeWithQuestionsAsync(client, user, id);

        var systems = provider.Requests.SelectMany(request => request.SystemPrompts).ToList();
        var templates = Templates();
        Assert.NotEmpty(templates);
        Assert.All(templates, template => Assert.Contains(systems, system => system.StartsWith(template.Task, StringComparison.Ordinal)));
    }

    public void Dispose() => ai.Dispose();

    // Every prompt template of Content: a type with a PromptVersion, named by the Task its system prompt starts with.
    private static List<(Type Type, string Task)> Templates() =>
        [.. typeof(TopicAnalysis).Assembly.GetTypes()
            .Where(type => type.GetField("PromptVersion", BindingFlags.Public | BindingFlags.Static) is { IsLiteral: true })
            .Select(type => (type, type.GetField("Task", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string
                ?? throw new InvalidOperationException($"{type.Name} has a PromptVersion but no Task its system prompt starts with.")))];

    // Splits the document as the worker does, analyzes it, lets Knowledge take the topics over and fills the pools. Then
    // asks for the summary of the first topic (#344) and lets the worker write it.
    private async Task AnalyzeWithQuestionsAsync(HttpClient client, string user, Guid id)
    {
        var chat = ai.Services.GetRequiredService<IChatClient>();
        await ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services).SplitAsync(id, TestContext.Current.CancellationToken);
        await ActivatorUtilities.CreateInstance<DocumentAnalyzer>(service.Services, chat).AnalyzeAsync(id, TestContext.Current.CancellationToken);
        var topics = service.Knowledge.TakeOver(Assert.Single(await OutboxAsync<DocumentAnalyzed>(id)));
        await ActivatorUtilities.CreateInstance<QuestionPools>(service.Services, chat).FinishAsync(id, TestContext.Current.CancellationToken);
        Assert.NotEmpty(await OutboxAsync<ItemsGenerated>(id));

        var topic = topics.First(item => item.SectionIds.Count > 0);
        service.Material.Give(user, topic.Id, [.. topic.SectionIds.Select(section => (id, section))]);
        using var requested = await client.PostAsync($"/topics/{topic.Id}/summaries/short", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        await ActivatorUtilities.CreateInstance<SummaryWriter>(service.Services, chat).WriteWaitingAsync(TestContext.Current.CancellationToken);
        var summary = await client.GetFromJsonAsync<JsonElement>($"/topics/{topic.Id}/summaries/short", TestContext.Current.CancellationToken);
        Assert.Equal("ready", summary.GetProperty("status").GetString());
    }

    private async Task<List<T>> OutboxAsync<T>(Guid documentId)
    {
        using var scope = service.Services.CreateScope();
        var subject = EventRoute.For(typeof(T)).Subject;
        var payloads = await scope.ServiceProvider.GetRequiredService<ContentDbContext>().Set<OutboxMessage>()
            .Where(message => message.Subject == subject && message.Payload.Contains(documentId.ToString()))
            .Select(message => message.Payload)
            .ToListAsync(TestContext.Current.CancellationToken);
        return [.. payloads.Select(payload => JsonSerializer.Deserialize<T>(payload, JsonSerializerOptions.Web)!)];
    }

    private static async Task<Guid> PasteAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/documents", new { text = Text, title = Title, confirmation = "material-1" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
    }

    // A token as Identity could issue it, with everything about the person a token might carry.
    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        Claim[] claims =
        [
            new("name", Name),
            new("email", Email),
            new("preferred_username", Pseudonym),
        ];
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId, claims: claims));
        return client;
    }

    // Content's AI on the real HTTP client of the provider (named "ai"), which only ever reaches the recording endpoint.
    // Should the name change, the requests go to an address that doesn't exist and the test fails.
    private static IHost ProviderAi(ProviderEndpoint endpoint)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(
        [
            KeyValuePair.Create("Ai:Provider", (string?)"OpenAiCompatible"),
            KeyValuePair.Create("Ai:OpenAiCompatible:Endpoint", (string?)"https://ai.invalid/v1"),
            KeyValuePair.Create("Ai:OpenAiCompatible:Model", (string?)"test-model"),
            KeyValuePair.Create("Ai:OpenAiCompatible:ApiKey", (string?)"test-key"),
        ]);
        builder.AddContentAi();
        builder.Services.AddHttpClient("ai").ConfigurePrimaryHttpMessageHandler(() => endpoint);
        return builder.Build();
    }

    // Stands in for the provider: keeps each request as it arrived and answers like the simulated AI would.
    private sealed class ProviderEndpoint : HttpMessageHandler
    {
        private readonly IHost simulated = SimulatedAi();

        public List<Recorded> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var headers = request.Headers.Concat(request.Content.Headers)
                .Select(header => $"{header.Key}: {string.Join(", ", header.Value)}");
            var messages = JsonNode.Parse(body)!["messages"]!.AsArray()
                .Select(message => new ChatMessage(new ChatRole(message!["role"]!.GetValue<string>()), message["content"]!.GetValue<string>()))
                .ToList();
            lock (Requests)
            {
                Requests.Add(new Recorded(
                    request.RequestUri!.ToString(),
                    string.Join('\n', headers),
                    body,
                    [.. messages.Where(message => message.Role == ChatRole.System).Select(message => message.Text)]));
            }

            var answer = await simulated.Services.GetRequiredService<SimulatedChatClient>().GetResponseAsync(messages, cancellationToken: cancellationToken);
            var json = new JsonObject
            {
                ["id"] = "answer",
                ["model"] = "test-model",
                ["choices"] = new JsonArray(new JsonObject
                {
                    ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = answer.Text },
                    ["finish_reason"] = "stop",
                }),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                simulated.Dispose();
            }

            base.Dispose(disposing);
        }

        private static IHost SimulatedAi()
        {
            var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
            builder.Configuration.AddInMemoryCollection([KeyValuePair.Create("Ai:Provider", (string?)SimulatedChatClient.Provider)]);
            builder.AddContentAi();
            return builder.Build();
        }
    }

    private sealed record Recorded(string Uri, string Headers, string Body, IReadOnlyList<string> SystemPrompts)
    {
        public string Everything => $"{Uri}\n{Headers}\n{Body}";
    }
}