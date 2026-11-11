using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Planning.Domain;
using Edvaniq.Testing;

namespace Edvaniq.Services.Planning.IntegrationTests;

// POST /learning-goals stores a goal with all its fields, GET lists and shows the user's own.
public sealed class LearningGoalCreationTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static readonly string InAMonth = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30).ToString("yyyy-MM-dd");

    [Fact]
    public async Task Create_WithoutToken_IsRejected()
    {
        using var client = service.CreateClient();
        using var response = await client.PostAsJsonAsync("/learning-goals", FreeGoal("C#"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExamPreparation_KeepsAllFields()
    {
        using var alice = ClientFor(NewUser("alice"));
        var request = new NewGoal("ExamPreparation", "Mathe Schulaufgabe", "Schulaufgabe", InAMonth, "MiddleSecondary", "en", "Eine 2 schaffen");

        var created = await CreateAsync(alice, request);

        var expected = new StoredGoal(created.Id, "ExamPreparation", "Mathe Schulaufgabe", "Schulaufgabe", InAMonth, "MiddleSecondary", "en", "Eine 2 schaffen");
        Assert.Equal(expected, created);
        Assert.Equal(expected, await GetAsync<StoredGoal>(alice, $"/learning-goals/{created.Id}"));
        Assert.Equal([expected], await GetAsync<StoredGoal[]>(alice, "/learning-goals"));
    }

    // Free learning has no exam: no exam type, no date, so the planner plans rolling. Output language defaults to German.
    [Fact]
    public async Task FreeLearning_NeedsOnlyKindAndName()
    {
        using var alice = ClientFor(NewUser("alice"));

        var created = await CreateAsync(alice, FreeGoal("C#"));

        Assert.Equal(new StoredGoal(created.Id, "FreeLearning", "C#", null, null, null, "de", null), created);
    }

    [Fact]
    public async Task InvalidInput_IsRejectedAtTheFields()
    {
        using var alice = ClientFor(NewUser("alice"));
        var request = new NewGoal("ExamPreparation", " ", null, "2020-01-01", "Grundschule", "fr", new string('a', 201));

        using var response = await alice.PostAsJsonAsync("/learning-goals", request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var errors = await ErrorsAsync(response);
        Assert.Equal(["ExamDate", "ExamType", "Goal", "Level", "Name", "OutputLanguage"], errors.Keys.Order());
        Assert.Equal("Gib dem Lernziel einen Namen.", errors["Name"]);
        Assert.Equal("Der Prüfungstermin darf nicht in der Vergangenheit liegen.", errors["ExamDate"]);
        Assert.Empty(await GetAsync<StoredGoal[]>(alice, "/learning-goals"));
    }

    [Fact]
    public async Task FreeLearning_WithAnExam_IsRejected()
    {
        using var alice = ClientFor(NewUser("alice"));
        var request = new NewGoal("FreeLearning", "C#", "Klausur", InAMonth, null, null, null);

        using var response = await alice.PostAsJsonAsync("/learning-goals", request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["ExamDate", "ExamType"], (await ErrorsAsync(response)).Keys.Order());
    }

    // What Content already put on the default goal belongs to the first real goal, under the same id.
    [Fact]
    public async Task FirstGoal_TakesOverTheDefaultGoal()
    {
        var user = NewUser("alice");
        using var alice = ClientFor(user);
        var defaultId = await DefaultGoalAsync(alice);

        // Without fields it is no goal the user made, so the list and the goal page leave it out.
        Assert.Empty(await GetAsync<StoredGoal[]>(alice, "/learning-goals"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(alice, $"/learning-goals/{defaultId}"));

        var first = await CreateAsync(alice, FreeGoal("Erstes"));
        var second = await CreateAsync(alice, FreeGoal("Zweites"));

        Assert.Equal(defaultId, first.Id);
        Assert.NotEqual(defaultId, second.Id);
        Assert.Equal(["Erstes", "Zweites"], (await GetAsync<StoredGoal[]>(alice, "/learning-goals")).Select(goal => goal.Name));
        Assert.Equal(defaultId, await DefaultGoalAsync(alice));
    }

    // Content keeps getting the same default goal, also when the user created a goal before any text came in.
    [Fact]
    public async Task DefaultGoal_AfterAGoalWasCreated_IsThatGoal()
    {
        var user = NewUser("alice");
        using var alice = ClientFor(user);

        var created = await CreateAsync(alice, FreeGoal("C#"));

        Assert.Equal(LearningGoal.DefaultIdFor(user), created.Id);
        Assert.Equal(created.Id, await DefaultGoalAsync(alice));
        Assert.Equal(created.Id, await DefaultGoalAsync(alice));
        Assert.Single(await GetAsync<StoredGoal[]>(alice, "/learning-goals"));
    }

    // Of calls at the same time exactly one takes over the default goal, the others get goals of their own.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallsAtTheSameTime_TakeOverTheDefaultGoalOnce(bool defaultGoalExists)
    {
        var user = NewUser("alice");
        using var alice = ClientFor(user);
        if (defaultGoalExists)
        {
            await DefaultGoalAsync(alice);
        }

        var clients = Enumerable.Range(0, 10).Select(_ => ClientFor(user)).ToList();
        StoredGoal[] goals;
        try
        {
            goals = await Task.WhenAll(clients.Select((client, i) => CreateAsync(client, FreeGoal($"Ziel {i}"))));
        }
        finally
        {
            clients.ForEach(client => client.Dispose());
        }

        Assert.Equal(10, goals.Select(goal => goal.Id).Distinct().Count());
        Assert.Single(goals, goal => goal.Id == LearningGoal.DefaultIdFor(user));
        Assert.Equal(10, (await GetAsync<StoredGoal[]>(alice, "/learning-goals")).Length);
    }

    [Fact]
    public async Task OtherUser_DoesNotSeeTheGoals()
    {
        using var alice = ClientFor(NewUser("alice"));
        using var bob = ClientFor(NewUser("bob"));

        var goal = await CreateAsync(alice, FreeGoal("Von Alice"));
        var another = await CreateAsync(alice, FreeGoal("Noch eins von Alice"));

        Assert.Empty(await GetAsync<StoredGoal[]>(bob, "/learning-goals"));
        foreach (var id in new[] { goal.Id, another.Id })
        {
            Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(bob, $"/learning-goals/{id}"));
        }

        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(bob, $"/learning-goals/{Guid.NewGuid()}"));
        Assert.Equal(2, (await GetAsync<StoredGoal[]>(alice, "/learning-goals")).Length);
    }

    private static NewGoal FreeGoal(string name) => new("FreeLearning", name, null, null, null, null, null);

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private HttpClient ClientFor(string user)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        return client;
    }

    private static async Task<StoredGoal> CreateAsync(HttpClient client, NewGoal request)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await client.PostAsJsonAsync("/learning-goals", request, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var goal = (await response.Content.ReadFromJsonAsync<StoredGoal>(cancellationToken))!;
        Assert.Equal($"/learning-goals/{goal.Id}", response.Headers.Location?.OriginalString);
        return goal;
    }

    private static async Task<Guid> DefaultGoalAsync(HttpClient client)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await client.PostAsync("/learning-goals/default", content: null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DefaultGoal>(cancellationToken))!.Id;
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await client.GetAsync(path, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken))!;
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private static async Task<Dictionary<string, string>> ErrorsAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return problem.RootElement.GetProperty("errors").EnumerateObject()
            .ToDictionary(field => field.Name, field => field.Value.EnumerateArray().Single().GetString()!);
    }

    private sealed record NewGoal(
        string? Kind, string? Name, string? ExamType, string? ExamDate, string? Level, string? OutputLanguage, string? Goal);

    private sealed record StoredGoal(
        Guid Id, string Kind, string Name, string? ExamType, string? ExamDate, string? Level, string OutputLanguage, string? Goal);

    private sealed record DefaultGoal(Guid Id);
}