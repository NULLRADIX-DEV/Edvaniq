using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Content.IntegrationTests;

// A pasted text belongs to the user's default learning goal, which Planning gives with the user's own token. Without
// Planning the text is turned away with a hint and nothing is stored.
public sealed class LearningGoalTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    [Fact]
    public async Task PastedText_BelongsToTheDefaultLearningGoal()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        using var client = ClientFor(alice);

        var id = await PasteAsync(client);

        Assert.Equal(service.Planning.GoalOf(alice), (await StoredAsync(alice)).Single(document => document.Id == id).LearningGoalId);
    }

    // Planning gets the caller's token and nothing else of the browser's request, no cookie and no other header.
    [Fact]
    public async Task Planning_GetsTheUsersTokenAndNoOtherHeader()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        var token = TestTokens.Create(alice);
        using var client = service.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/documents") { Content = JsonContent.Create(new { text = TestTexts.Lesson }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Cookie", "__Host-edvaniq-session=secret");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var asked = Assert.Single(service.Planning.Requests, received => received.User == alice);
        Assert.Equal((HttpMethod.Post, "/learning-goals/default", $"Bearer {token}"), (asked.Method, asked.Path, asked.Authorization));
        Assert.DoesNotContain(asked.Headers, header => header is "Cookie" or "X-Forwarded-For");
    }

    // Planning keeps the default goal once per user, so the second text lands in the same goal.
    [Fact]
    public async Task PastingTwice_UsesTheSameGoal()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        using var client = ClientFor(alice);

        await PasteAsync(client);
        await PasteAsync(client);

        var stored = await StoredAsync(alice);
        Assert.Equal(2, stored.Count);
        Assert.All(stored, document => Assert.Equal(service.Planning.GoalOf(alice), document.LearningGoalId));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task PlanningFailing_TurnsTheTextAwayWithAHint_AndStoresNothing(HttpStatusCode status)
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        service.Planning.Down(alice, status);
        using var client = ClientFor(alice);

        using var response = await client.PostAsJsonAsync("/documents", new { text = TestTexts.Lesson }, TestContext.Current.CancellationToken);

        await AssertUnavailableAsync(response);
        Assert.Empty(await StoredAsync(alice));
    }

    // The user waits at the button: a Planning that never answers still ends in a hint, well within 10 s.
    [Fact]
    public async Task PlanningHanging_AnswersWithin10Seconds()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        service.Planning.Hang(alice);
        using var client = ClientFor(alice);
        var watch = Stopwatch.StartNew();

        using var response = await client.PostAsJsonAsync("/documents", new { text = TestTexts.Lesson }, TestContext.Current.CancellationToken);

        watch.Stop();
        await AssertUnavailableAsync(response);
        Assert.InRange(watch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        Assert.Empty(await StoredAsync(alice));
    }

    private static async Task AssertUnavailableAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Der Text ließ sich gerade nicht speichern. Versuch es gleich noch einmal.", problem.GetProperty("title").GetString());
    }

    private static async Task<Guid> PasteAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/documents", new { text = TestTexts.Lesson }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
    }

    private async Task<List<Domain.Document>> StoredAsync(string userId)
    {
        using var scope = service.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ContentDbContext>().Documents.IgnoreQueryFilters()
            .Where(document => document.OwnerId == userId)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }
}