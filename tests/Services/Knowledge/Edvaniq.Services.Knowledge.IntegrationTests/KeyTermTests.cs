using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Knowledge.Application;
using Edvaniq.Services.Knowledge.Domain;
using Edvaniq.Services.Knowledge.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Knowledge.IntegrationTests;

// #292: the key terms an analysis brings stay with their topic and section, only the owner reads them, and they go with
// their document.
public sealed class KeyTermTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Photosynthesis = "Photosynthese ist der Vorgang, bei dem Pflanzen aus Licht Zucker herstellen.";

    private const string Chlorophyll = "Chlorophyll ist der grüne Farbstoff in den Blättern.";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnalysisWithTerms_KeepsThemOnTheirTopics_ForTheOwner()
    {
        var alice = NewUser();
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());
        var analysis = new DocumentAnalyzed(alice, Guid.NewGuid(),
        [
            new("Photosynthese", null, [first], [new("Photosynthese", first, Photosynthesis)]),
            new("Farbstoff", 0, [second], [new("Chlorophyll", second, Chlorophyll), new("chlorophyll", second, Chlorophyll)]),
        ], Guid.NewGuid());

        await HandleAsync(analysis);

        var (topic, subtopic) = (TopicCalled(alice, "Photosynthese"), TopicCalled(alice, "Farbstoff"));
        using var client = ClientFor(alice);
        Assert.Equal(
            new[] { new TermDto("Photosynthese", Photosynthesis, analysis.DocumentId, first) },
            await client.GetFromJsonAsync<TermDto[]>($"/topics/{topic}/terms", Cancel));
        Assert.Equal(
            new[] { new TermDto("Chlorophyll", Chlorophyll, analysis.DocumentId, second) },
            await client.GetFromJsonAsync<TermDto[]>($"/topics/{subtopic}/terms", Cancel));
        using var foreign = ClientFor(NewUser());
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"/topics/{topic}/terms", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/topics/{Guid.NewGuid()}/terms", Cancel)).StatusCode);
    }

    // The document again: its terms come new, the old ones of it go. A hidden topic still gives its terms.
    [Fact]
    public async Task SameDocumentAgain_ReplacesItsTerms_AndAHiddenTopicKeepsThem()
    {
        var alice = NewUser();
        var section = Guid.NewGuid();
        var document = Guid.NewGuid();
        var goal = Guid.NewGuid();
        await HandleAsync(new DocumentAnalyzed(alice, document, [new("Photosynthese", null, [section], [new("Photosynthese", section, Photosynthesis)])], goal));
        await HandleAsync(new DocumentAnalyzed(alice, document, [new("Photosynthese", null, [section], [new("Chlorophyll", section, Chlorophyll)])], goal));
        var topic = TopicCalled(alice, "Photosynthese");
        using var client = ClientFor(alice);
        using var hidden = await client.PutAsync($"/topics/{topic}/hidden", null, Cancel);
        Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);

        var terms = await client.GetFromJsonAsync<TermDto[]>($"/topics/{topic}/terms", Cancel);

        Assert.Equal(["Chlorophyll"], terms!.Select(term => term.Term));
    }

    [Fact]
    public async Task DeletedDocument_TakesItsTerms_TheOtherDocumentKeepsItsOwn()
    {
        var alice = NewUser();
        var goal = Guid.NewGuid();
        var (gone, kept) = (Guid.NewGuid(), Guid.NewGuid());
        var (goneSection, keptSection) = (Guid.NewGuid(), Guid.NewGuid());
        await HandleAsync(new DocumentAnalyzed(alice, gone, [new("Photosynthese", null, [goneSection], [new("Photosynthese", goneSection, Photosynthesis)])], goal));
        await HandleAsync(new DocumentAnalyzed(alice, kept, [new("Photosynthese", null, [keptSection], [new("Chlorophyll", keptSection, Chlorophyll)])], goal));

        using (var scope = service.Services.CreateScope())
        {
            await ActivatorUtilities.CreateInstance<DocumentDeletedHandler>(scope.ServiceProvider)
                .HandleAsync(new DocumentDeleted(alice, gone, goal), Cancel);
            await scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>().SaveChangesAsync(Cancel);
        }

        Assert.Equal([kept], Query(db => db.KeyTerms.IgnoreQueryFilters().Where(term => term.OwnerId == alice).Select(term => term.DocumentId).ToList()));
    }

    // Terms alone change no topic, a save with nothing but them needs no TopicSet.
    [Fact]
    public async Task SaveOfTermsAlone_NeedsNoTopicSet()
    {
        var alice = NewUser();
        await HandleAsync(new DocumentAnalyzed(alice, Guid.NewGuid(), [new("Photosynthese", null, [Guid.NewGuid()])], Guid.NewGuid()));
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        var topic = db.Topics.IgnoreQueryFilters().Single(item => item.OwnerId == alice);

        db.KeyTerms.Add(topic.KeyTerm(Guid.NewGuid(), Guid.NewGuid(), "Photosynthese", Photosynthesis, Seed.Now, 0));
        await db.SaveChangesAsync(Cancel);

        Assert.Equal(1, Query(db => db.KeyTerms.IgnoreQueryFilters().Count(term => term.OwnerId == alice)));
    }

    // What the consumer does: a scope, the handler, one save.
    private async Task HandleAsync(DocumentAnalyzed analysis)
    {
        using var scope = service.Services.CreateScope();
        await ActivatorUtilities.CreateInstance<DocumentAnalyzedHandler>(scope.ServiceProvider).HandleAsync(analysis, Cancel);
        await scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>().SaveChangesAsync(Cancel);
    }

    private Guid TopicCalled(string owner, string name) =>
        Query(db => db.Topics.IgnoreQueryFilters().Single(topic => topic.OwnerId == owner && topic.Name == name).Id);

    private T Query<T>(Func<KnowledgeDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>());
    }

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    private static string NewUser() => $"alice-{Guid.NewGuid():N}";

    private sealed record TermDto(string Term, string Definition, Guid DocumentId, Guid SectionId);
}