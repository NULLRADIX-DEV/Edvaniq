using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Knowledge.Domain;
using Edvaniq.Services.Knowledge.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Knowledge.IntegrationTests;

// The user sees their topics in two levels, per learning goal or all of them, and can rename one without its id
// changing.
public sealed class TopicTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    [Fact]
    public async Task Topics_AreListedInTwoLevels_PerLearningGoal()
    {
        var alice = NewUser();
        var biology = Guid.NewGuid();
        var cell = Topic.Create(alice, biology, "Zelle", Seed.Now);
        var nucleus = cell.AddSubtopic("Zellkern", Seed.Now.AddSeconds(1));
        var mitochondria = cell.AddSubtopic("Mitochondrien", Seed.Now.AddSeconds(2));
        var genetics = Topic.Create(alice, biology, "Genetik", Seed.Now.AddSeconds(3));
        var algebra = Topic.Create(alice, Guid.NewGuid(), "Algebra", Seed.Now.AddSeconds(4));
        await service.AddAsync(algebra, mitochondria, genetics, nucleus, cell);
        using var client = ClientFor(alice);

        var ofBiology = await ListAsync(client, $"/topics?learningGoalId={biology}");

        Assert.Equal(
            [
                new Tree(cell.Id, biology, "Zelle", [new Leaf(nucleus.Id, "Zellkern"), new Leaf(mitochondria.Id, "Mitochondrien")]),
                new Tree(genetics.Id, biology, "Genetik", []),
            ],
            ofBiology,
            TreeComparer.Instance);
        Assert.Equal([cell.Id, genetics.Id, algebra.Id], (await ListAsync(client, "/topics")).Select(tree => tree.Id));
    }

    [Fact]
    public async Task Rename_KeepsTheId()
    {
        var alice = NewUser();
        var topic = await service.AddTopicAsync(alice, "Zelle");
        using var client = ClientFor(alice);

        using var response = await client.PatchAsJsonAsync($"/topics/{topic.Id}", new { name = "  Zellbiologie " }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var renamed = await response.Content.ReadFromJsonAsync<TopicDto>(TestContext.Current.CancellationToken);
        Assert.Equal(new TopicDto(topic.Id, topic.LearningGoalId, null, "Zellbiologie"), renamed);
        Assert.Equal(renamed, await client.GetFromJsonAsync<TopicDto>($"/topics/{topic.Id}", TestContext.Current.CancellationToken));

        using var scope = service.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>().Topics.IgnoreQueryFilters()
            .Where(item => item.OwnerId == alice).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal((topic.Id, "Zellbiologie"), (Assert.Single(stored).Id, stored[0].Name));
    }

    [Fact]
    public async Task RenamedSubtopic_StaysUnderItsTopic()
    {
        var alice = NewUser();
        var cell = Topic.Create(alice, Guid.NewGuid(), "Zelle", Seed.Now);
        var nucleus = cell.AddSubtopic("Kern", Seed.Now);
        await service.AddAsync(cell, nucleus);
        using var client = ClientFor(alice);

        using var response = await client.PatchAsJsonAsync($"/topics/{nucleus.Id}", new { name = "Zellkern" }, TestContext.Current.CancellationToken);

        Assert.Equal(
            new TopicDto(nucleus.Id, cell.LearningGoalId, cell.Id, "Zellkern"),
            await response.Content.ReadFromJsonAsync<TopicDto>(TestContext.Current.CancellationToken));
        Assert.Equal([new Leaf(nucleus.Id, "Zellkern")], Assert.Single(await ListAsync(client, "/topics")).Subtopics);
    }

    [Theory]
    [InlineData(" ", "Der Name ist leer. Gib dem Thema einen Namen.")]
    [InlineData(null, "Der Name ist leer. Gib dem Thema einen Namen.")]
    [InlineData("123456789012345678901234567890123456789012345678901234567890123456789012345678901", "Der Name ist zu lang. Erlaubt sind höchstens 80 Zeichen.")]
    public async Task Rename_WithoutAValidName_IsRejectedWithAReason(string? name, string reason)
    {
        var alice = NewUser();
        var topic = await service.AddTopicAsync(alice, "Zelle");
        using var client = ClientFor(alice);

        using var response = await client.PatchAsJsonAsync($"/topics/{topic.Id}", new { name }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal([reason], problem.GetProperty("errors").GetProperty("Name").Deserialize<string[]>()!);
        Assert.Equal("Zelle", (await client.GetFromJsonAsync<TopicDto>($"/topics/{topic.Id}", TestContext.Current.CancellationToken))!.Name);
    }

    // The learning goal is no part of the key, so a topic can belong to a second goal later and keep its id.
    [Fact]
    public void TopicKey_IsTheIdAlone()
    {
        using var context = new KnowledgeDbContextFactory().CreateDbContext([]);

        var key = context.Model.FindEntityType(typeof(Topic))!.FindPrimaryKey()!;

        Assert.Equal([nameof(Topic.Id)], key.Properties.Select(property => property.Name));
    }

    private static string NewUser() => $"alice-{Guid.NewGuid():N}";

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    private static async Task<Tree[]> ListAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<Tree[]>(path, TestContext.Current.CancellationToken))!;

    private sealed record Tree(Guid Id, Guid LearningGoalId, string Name, Leaf[] Subtopics);

    private sealed record Leaf(Guid Id, string Name);

    private sealed record TopicDto(Guid Id, Guid LearningGoalId, Guid? ParentId, string Name);

    // Records compare arrays by reference, this compares the subtopics by content.
    private sealed class TreeComparer : IEqualityComparer<Tree>
    {
        public static readonly TreeComparer Instance = new();

        public bool Equals(Tree? x, Tree? y) =>
            x is not null && y is not null
            && (x.Id, x.LearningGoalId, x.Name) == (y.Id, y.LearningGoalId, y.Name)
            && x.Subtopics.SequenceEqual(y.Subtopics);

        public int GetHashCode(Tree tree) => tree.Id.GetHashCode();
    }
}