using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Knowledge.Application;
using Edvaniq.Services.Knowledge.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Knowledge.IntegrationTests;

// #294: a topic names its sections with their documents, only for its owner.
public sealed class TopicSectionTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TopicWithMaterialOfTwoDocuments_GivesEverySection_OnlyToItsOwner()
    {
        var alice = NewUser();
        var goal = Guid.NewGuid();
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await HandleAsync(new DocumentAnalyzed(alice, first, [new("Photosynthese", null, [a, b])], goal));
        await HandleAsync(new DocumentAnalyzed(alice, second, [new("Photosynthese", null, [c])], goal));
        var topic = TopicCalled(alice, "Photosynthese");
        using var client = ClientFor(alice);

        var sections = await client.GetFromJsonAsync<SectionDto[]>($"/topics/{topic}/sections", Cancel);

        Assert.Equal(
            new[] { new SectionDto(first, a), new SectionDto(first, b), new SectionDto(second, c) }
                .OrderBy(section => section.DocumentId).ThenBy(section => section.SectionId),
            sections!.OrderBy(section => section.DocumentId).ThenBy(section => section.SectionId));
        using var foreign = ClientFor(NewUser());
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"/topics/{topic}/sections", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/topics/{Guid.NewGuid()}/sections", Cancel)).StatusCode);
    }

    // A topic added by hand has no material yet: an empty list, not 404.
    [Fact]
    public async Task TopicWithoutMaterial_GivesAnEmptyList()
    {
        var alice = NewUser();
        var topic = await service.AddTopicAsync(alice, "Zellbiologie");
        using var client = ClientFor(alice);

        var sections = await client.GetFromJsonAsync<SectionDto[]>($"/topics/{topic.Id}/sections", Cancel);

        Assert.Empty(sections!);
    }

    private async Task HandleAsync(DocumentAnalyzed analysis)
    {
        using var scope = service.Services.CreateScope();
        await ActivatorUtilities.CreateInstance<DocumentAnalyzedHandler>(scope.ServiceProvider).HandleAsync(analysis, Cancel);
        await scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>().SaveChangesAsync(Cancel);
    }

    private Guid TopicCalled(string owner, string name)
    {
        using var scope = service.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>().Topics.IgnoreQueryFilters()
            .Single(topic => topic.OwnerId == owner && topic.Name == name).Id;
    }

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    private static string NewUser() => $"alice-{Guid.NewGuid():N}";

    private sealed record SectionDto(Guid DocumentId, Guid SectionId);
}