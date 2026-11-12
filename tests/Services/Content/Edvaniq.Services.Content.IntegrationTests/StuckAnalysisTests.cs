using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.BuildingBlocks.AI;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Services.Content.Infrastructure.Ai;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Content.IntegrationTests;

// #324: no document stays in analysis for good. A run that takes longer than Content:Analysis:Timeout fails, the user can
// start it again. The tests run one after another on one database, a sweep with Timeout 0 takes every running document.
public sealed class StuckAnalysisTests(ServiceFactory service) : IClassFixture<ServiceFactory>, IDisposable
{
    private const string Text = """
        # Photosynthese
        Licht spaltet in den Blättern Wasser, dabei entsteht Sauerstoff für die Luft.

        # Zellatmung
        Zellen gewinnen aus Zucker und Sauerstoff die Energie, die sie zum Leben brauchen.
        """;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly IHost ai = SimulatedAi();

    // A worker that died: the document was split and nobody analyzes it. The sweep fails it with its reason, a retry
    // starts it again, and the analysis then goes through.
    [Fact]
    public async Task DocumentOfADeadWorker_FailsAfterTheLimit_AndRetryStartsItAgain()
    {
        using var client = ClientFor(NewUser());
        var id = await PasteAsync(client);
        await Splitter().SplitAsync(id, Cancel);

        var swept = await Sweep(TimeSpan.Zero).SweepAsync(Cancel);

        Assert.True(swept >= 1);
        Assert.Equal((DocumentStatus.Failed, Document.AnalysisTimedOut), StatusOf(id));
        using var retried = await client.PostAsync($"/documents/{id}/retry", null, Cancel);
        Assert.Equal(HttpStatusCode.Accepted, retried.StatusCode);
        await Splitter().SplitAsync(id, Cancel);
        await Analyzer(Chat).AnalyzeAsync(id, Cancel);
        Assert.Single(await ReportedAsync(id));
    }

    // A file whose text never gets read stays Uploaded, and fails the same way.
    [Fact]
    public async Task FileThatIsNeverRead_FailsAfterTheLimit()
    {
        using var client = ClientFor(NewUser());
        var file = new ByteArrayContent("Licht spaltet Wasser, dabei entsteht Sauerstoff für die Luft der Erde."u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var uploaded = await client.PostAsync("/documents/files?confirmation=material-1", new MultipartFormDataContent { { file, "file", "Notizen.txt" } }, Cancel);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var id = (await uploaded.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();

        await Sweep(TimeSpan.Zero).SweepAsync(Cancel);

        Assert.Equal((DocumentStatus.Failed, Document.AnalysisTimedOut), StatusOf(id));
    }

    // Within the limit nothing happens, a document postponed after an outage waits on purpose, and a finished one is done.
    [Fact]
    public async Task FreshPostponedReadyAndFailedDocuments_StayAsTheyAre()
    {
        using var client = ClientFor(NewUser());
        var fresh = await PasteAsync(client);
        await Splitter().SplitAsync(fresh, Cancel);
        await Sweep(TimeSpan.FromHours(1)).SweepAsync(Cancel);
        Assert.Equal(DocumentStatus.Analyzing, StatusOf(fresh).Status);

        var postponed = await PasteAsync(client);
        await Splitter().SplitAsync(postponed, Cancel);
        Update(postponed, db => db.Documents.IgnoreQueryFilters().Where(document => document.Id == postponed)
            .ExecuteUpdate(set => set.SetProperty(document => document.NextAttemptAt, DateTime.UtcNow.AddMinutes(10))));
        var failed = await PasteAsync(client, "   \n\n   " + Text);
        Update(failed, db => db.Documents.IgnoreQueryFilters().Where(document => document.Id == failed)
            .ExecuteUpdate(set => set.SetProperty(document => document.Status, DocumentStatus.Failed)
                .SetProperty(document => document.FailureReason, Document.AnalysisFailed)));

        await Sweep(TimeSpan.Zero).SweepAsync(Cancel);

        Assert.Equal(DocumentStatus.Analyzing, StatusOf(postponed).Status);
        Assert.Equal((DocumentStatus.Failed, Document.AnalysisFailed), StatusOf(failed));
        Assert.Equal(DocumentStatus.Failed, StatusOf(fresh).Status);
    }

    // The sweep fails the document while the worker asks the AI for its topics. The worker's save loses on the status,
    // the document stays failed and no DocumentAnalyzed goes out.
    [Fact]
    public async Task SweepDuringTheAnalysis_LeavesOneEnd_AndReportsNothing()
    {
        using var client = ClientFor(NewUser());
        var id = await PasteAsync(client);
        await Splitter().SplitAsync(id, Cancel);
        var sweeping = new SweepingChatClient(Chat, () => Sweep(TimeSpan.Zero).SweepAsync(Cancel));

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => Analyzer(sweeping).AnalyzeAsync(id, Cancel));

        Assert.Equal((DocumentStatus.Failed, Document.AnalysisTimedOut), StatusOf(id));
        Assert.Empty(await ReportedAsync(id));
    }

    // The limits come from Content:Analysis, and the worker doesn't start with one that makes no sense.
    [Fact]
    public void Limits_ComeFromTheConfiguration_AndAreChecked()
    {
        var configured = OptionsFrom(new()
        {
            ["Content:Analysis:AiTimeout"] = "00:00:05",
            ["Content:Analysis:TopicsTimeout"] = "00:03:00",
            ["Content:Analysis:Timeout"] = "00:30:00",
        });
        Assert.Equal(
            (TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(1)),
            (configured.Value.AiTimeout, configured.Value.TopicsTimeout, configured.Value.Timeout, configured.Value.SweepInterval));
        Assert.Equal(TimeSpan.FromSeconds(5), Analyzer(Chat, configured).AiTimeout);

        var invalid = OptionsFrom(new() { ["Content:Analysis:Timeout"] = "00:05:00" });
        Assert.Throws<OptionsValidationException>(() => invalid.Value);
    }

    public void Dispose() => ai.Dispose();

    private IChatClient Chat => ai.Services.GetRequiredService<IChatClient>();

    private DocumentSplitter Splitter() => ActivatorUtilities.CreateInstance<DocumentSplitter>(service.Services);

    private DocumentAnalyzer Analyzer(IChatClient chat, IOptions<AnalysisOptions>? options = null) =>
        options is null
            ? ActivatorUtilities.CreateInstance<DocumentAnalyzer>(service.Services, chat)
            : ActivatorUtilities.CreateInstance<DocumentAnalyzer>(service.Services, chat, options);

    private StuckAnalysisSweep Sweep(TimeSpan timeout)
    {
        var sweep = ActivatorUtilities.CreateInstance<StuckAnalysisSweep>(service.Services, NullLogger<StuckAnalysisSweep>.Instance);
        sweep.Timeout = timeout;
        return sweep;
    }

    private static IOptions<AnalysisOptions> OptionsFrom(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        AnalysisOptions.AddTo(services);
        return services.BuildServiceProvider().GetRequiredService<IOptions<AnalysisOptions>>();
    }

    private (DocumentStatus Status, string? FailureReason) StatusOf(Guid id)
    {
        using var scope = service.Services.CreateScope();
        var document = scope.ServiceProvider.GetRequiredService<ContentDbContext>().Documents.IgnoreQueryFilters().AsNoTracking()
            .Single(item => item.Id == id);
        return (document.Status, document.FailureReason);
    }

    private void Update(Guid id, Func<ContentDbContext, int> change)
    {
        using var scope = service.Services.CreateScope();
        Assert.Equal(1, change(scope.ServiceProvider.GetRequiredService<ContentDbContext>()));
    }

    private async Task<List<DocumentAnalyzed>> ReportedAsync(Guid documentId)
    {
        using var scope = service.Services.CreateScope();
        var subject = EventRoute.For(typeof(DocumentAnalyzed)).Subject;
        var payloads = await scope.ServiceProvider.GetRequiredService<ContentDbContext>().Set<OutboxMessage>()
            .Where(message => message.Subject == subject && message.Payload.Contains(documentId.ToString()))
            .Select(message => message.Payload)
            .ToListAsync(Cancel);
        return [.. payloads.Select(payload => JsonSerializer.Deserialize<DocumentAnalyzed>(payload, JsonSerializerOptions.Web)!)];
    }

    private static async Task<Guid> PasteAsync(HttpClient client, string text = Text)
    {
        using var response = await client.PostAsJsonAsync("/documents", new { text, confirmation = "material-1" }, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
    }

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    private static string NewUser() => $"alice-{Guid.NewGuid():N}";

    // Content's AI on the simulated provider.
    private static IHost SimulatedAi()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection([KeyValuePair.Create("Ai:Provider", (string?)SimulatedChatClient.Provider)]);
        builder.AddContentAi();
        return builder.Build();
    }

    // Runs the sweep while the AI works on the first prompt.
    private sealed class SweepingChatClient(IChatClient inner, Func<Task<int>> sweep) : DelegatingChatClient(inner)
    {
        private bool swept;

        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (!swept)
            {
                swept = true;
                await sweep();
            }

            return await base.GetResponseAsync(messages, options, cancellationToken);
        }
    }
}