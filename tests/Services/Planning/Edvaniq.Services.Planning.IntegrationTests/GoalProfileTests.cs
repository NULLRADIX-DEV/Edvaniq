using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Testing;

namespace Edvaniq.Services.Planning.IntegrationTests;

// #323: Content asks for no more of a goal than it needs, level and output language. The user's route shows the user's
// goals only, the internal one any goal, but only to Content's own internal token.
public sealed class GoalProfileTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Content = "content";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UserRoute_GivesIdLevelAndLanguageOnly_AlsoForTheDefaultGoal()
    {
        using var alice = ClientWith(TestTokens.Create(NewUser("alice")));
        using var bob = ClientWith(TestTokens.Create(NewUser("bob")));
        var goal = await CreateAsync(alice, "Abitur Bio", "UpperSecondary", "en");

        // Bob never created a goal, his default one is nobody's taken-over goal and has no name.
        var defaultGoal = await DefaultAsync(bob);

        var profile = await alice.GetFromJsonAsync<JsonElement>($"/learning-goals/{goal}/profile", Cancel);
        var ofDefault = await bob.GetFromJsonAsync<JsonElement>($"/learning-goals/{defaultGoal}/profile", Cancel);

        Assert.Equal(["id", "level", "outputLanguage"], profile.EnumerateObject().Select(field => field.Name));
        Assert.Equal((goal, "UpperSecondary", "en"), (profile.GetProperty("id").GetGuid(), profile.GetProperty("level").GetString(), profile.GetProperty("outputLanguage").GetString()));
        Assert.Equal(defaultGoal, ofDefault.GetProperty("id").GetGuid());
    }

    // Someone else's goal looks like one that does not exist, also to Content's token on the user's route.
    [Fact]
    public async Task UserRoute_ForAForeignOrUnknownGoal_IsNotFound()
    {
        using var alice = ClientWith(TestTokens.Create(NewUser("alice")));
        using var bob = ClientWith(TestTokens.Create(NewUser("bob")));
        using var content = ClientWith(TestTokens.CreateForClient(Content));
        var goal = await CreateAsync(alice, "Abitur Bio", "UpperSecondary", "de");

        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(bob, $"/learning-goals/{goal}/profile"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(bob, $"/learning-goals/{Guid.NewGuid()}/profile"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(alice, $"/learning-goals/{Guid.NewGuid()}/profile"));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(content, $"/learning-goals/{goal}/profile"));
    }

    [Fact]
    public async Task InternalRoute_GivesContentLevelAndLanguageOnly_OfAnyUser()
    {
        using var alice = ClientWith(TestTokens.Create(NewUser("alice")));
        using var content = ClientWith(TestTokens.CreateForClient(Content));
        var goal = await CreateAsync(alice, "Ausbildung", "Apprenticeship", "de");

        var profile = await content.GetFromJsonAsync<JsonElement>($"/internal/learning-goals/{goal}/profile", Cancel);

        Assert.Equal(["level", "outputLanguage"], profile.EnumerateObject().Select(field => field.Name));
        Assert.Equal(("Apprenticeship", "de"), (profile.GetProperty("level").GetString(), profile.GetProperty("outputLanguage").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(content, $"/internal/learning-goals/{Guid.NewGuid()}/profile"));
    }

    // A user's token is for the services' audience, the internal scheme doesn't take it (401, as in Knowledge). An
    // internal token of another client or of a user is no Content (403).
    [Fact]
    public async Task InternalRoute_TurnsAwayEveryOtherToken()
    {
        using var alice = ClientWith(TestTokens.Create(NewUser("alice")));
        using var knowledge = ClientWith(TestTokens.CreateForClient("knowledge"));
        using var internalUser = ClientWith(TestTokens.Create("alice", audience: TestTokens.InternalAudience));
        using var nobody = ClientWith(null);
        var goal = await CreateAsync(alice, "Ausbildung", "Apprenticeship", "de");
        var path = $"/internal/learning-goals/{goal}/profile";

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(alice, path));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(knowledge, path));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(internalUser, path));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(nobody, path));
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private HttpClient ClientWith(string? token)
    {
        var client = service.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string name, string level, string language)
    {
        using var response = await client.PostAsJsonAsync(
            "/learning-goals", new { kind = "FreeLearning", name, level, outputLanguage = language }, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> DefaultAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/learning-goals/default", null, Cancel);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, Cancel);
        return response.StatusCode;
    }
}