using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Testing;

namespace Edvaniq.Services.Planning.IntegrationTests;

// #516: PUT /learning-goals/{id} changes a goal of the user with the rules of POST, keeps its kind and says when it
// changed. The topic choice is Knowledge's (hide and show, #286), PlanTests.Plan_LeavesOutHiddenTopics_AndThoseOfAnotherGoal
// shows the plan follows it.
public sealed class LearningGoalEditTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static readonly string InAMonth = Day(30);

    private static readonly string InTwoMonths = Day(60);

    // AC1 and AC4: stored, read back the same, with the time of the change.
    [Fact]
    public async Task Edit_StoresTheFields_AndWhenTheyChanged()
    {
        using var alice = ClientFor(NewUser("alice"));
        var created = await CreateAsync(alice, new NewGoal("ExamPreparation", "Mathe", "Schulaufgabe", InAMonth, null, null, null));
        Assert.Null(created.UpdatedAt);
        var before = DateTime.UtcNow;

        var (status, edited) = await PutAsync(alice, created.Id, new EditedGoal("Mathe 2", "Klausur", InTwoMonths, "UpperSecondary", "en", "eine 1"));

        Assert.Equal(HttpStatusCode.OK, status);
        var expected = created with
        {
            Name = "Mathe 2",
            ExamType = "Klausur",
            ExamDate = InTwoMonths,
            Level = "UpperSecondary",
            OutputLanguage = "en",
            Goal = "eine 1",
            UpdatedAt = edited!.UpdatedAt,
        };
        Assert.Equal(expected, edited);
        Assert.InRange(edited.UpdatedAt!.Value, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(DateTimeKind.Utc, edited.UpdatedAt.Value.Kind);
        Assert.Equal(expected, await GetAsync(alice, created.Id));
    }

    // A goal for an exam can lose its date, an exam needs no date until the user knows it.
    [Fact]
    public async Task Edit_CanClearTheExamDate()
    {
        using var alice = ClientFor(NewUser("alice"));
        var created = await CreateAsync(alice, new NewGoal("ExamPreparation", "Mathe", "Schulaufgabe", InAMonth, null, null, null));

        var (_, edited) = await PutAsync(alice, created.Id, new EditedGoal("Mathe", "Schulaufgabe", null, null, null, null));

        Assert.Null(edited!.ExamDate);
        Assert.Null((await GetAsync(alice, created.Id)).ExamDate);
    }

    // AC2: the message of POST, at the field, and nothing changes.
    [Fact]
    public async Task ExamDateInThePast_IsRejectedWithANotice()
    {
        using var alice = ClientFor(NewUser("alice"));
        var created = await CreateAsync(alice, new NewGoal("ExamPreparation", "Mathe", "Schulaufgabe", InAMonth, null, null, null));

        using var response = await alice.PutAsJsonAsync(
            $"/learning-goals/{created.Id}", new EditedGoal("Mathe", "Schulaufgabe", Day(-1), null, null, null), Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            new Dictionary<string, string> { ["ExamDate"] = "Der Prüfungstermin darf nicht in der Vergangenheit liegen." },
            await ErrorsAsync(response));
        Assert.Equal(created, await GetAsync(alice, created.Id));
    }

    // The rules go by the stored kind: free learning gets no exam.
    [Fact]
    public async Task FreeLearning_WithAnExam_IsRejected()
    {
        using var alice = ClientFor(NewUser("alice"));
        var created = await CreateAsync(alice, new NewGoal("FreeLearning", "C#", null, null, null, null, null));

        using var response = await alice.PutAsJsonAsync(
            $"/learning-goals/{created.Id}", new EditedGoal("C#", "Klausur", InAMonth, null, null, null), Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["ExamDate", "ExamType"], (await ErrorsAsync(response)).Keys.Order());
    }

    // Not included: changing the kind. A kind in the body is no field of the request and changes nothing.
    [Fact]
    public async Task KindInTheBody_IsIgnored_TheKindStays()
    {
        using var alice = ClientFor(NewUser("alice"));
        var created = await CreateAsync(alice, new NewGoal("FreeLearning", "C#", null, null, null, null, null));

        var (status, edited) = await PutAsync(alice, created.Id, new { kind = "ExamPreparation", name = "C# 2" });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(("FreeLearning", "C# 2"), (edited!.Kind, edited.Name));
        Assert.Equal("FreeLearning", (await GetAsync(alice, created.Id)).Kind);
    }

    // AC3: Bob can't change Alice's goal, it looks like one that isn't there. The default goal nobody took over has no
    // fields to change.
    [Fact]
    public async Task ForeignGoal_AndDefaultGoal_AreNotFound()
    {
        using var alice = ClientFor(NewUser("alice"));
        using var bob = ClientFor(NewUser("bob"));
        var created = await CreateAsync(alice, new NewGoal("FreeLearning", "Von Alice", null, null, null, null, null));
        var edit = new EditedGoal("Von Bob", null, null, null, null, null);
        var carolId = NewUser("carol");
        using var carol = ClientFor(carolId);
        using (var defaultCall = await carol.PostAsync("/learning-goals/default", null, Cancel))
        {
            Assert.Equal(HttpStatusCode.OK, defaultCall.StatusCode);
        }

        var (foreign, _) = await PutAsync(bob, created.Id, edit);
        var (missing, _) = await PutAsync(alice, Guid.NewGuid(), edit);
        var (untaken, _) = await PutAsync(carol, Domain.LearningGoal.DefaultIdFor(carolId), edit);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound), (foreign, missing, untaken));
        Assert.Equal(created, await GetAsync(alice, created.Id));
    }

    [Fact]
    public async Task Edit_WithoutToken_IsRejected()
    {
        using var client = service.CreateClient();
        using var response = await client.PutAsJsonAsync($"/learning-goals/{Guid.NewGuid()}", new EditedGoal("x", null, null, null, null, null), Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static string Day(int fromToday) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(fromToday).ToString("yyyy-MM-dd");

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private HttpClient ClientFor(string user)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        return client;
    }

    private static async Task<StoredGoal> CreateAsync(HttpClient client, NewGoal request)
    {
        using var response = await client.PostAsJsonAsync("/learning-goals", request, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StoredGoal>(Cancel))!;
    }

    private static async Task<(HttpStatusCode Status, StoredGoal? Goal)> PutAsync(HttpClient client, Guid id, object body)
    {
        using var response = await client.PutAsJsonAsync($"/learning-goals/{id}", body, Cancel);
        return response.StatusCode == HttpStatusCode.OK
            ? (response.StatusCode, await response.Content.ReadFromJsonAsync<StoredGoal>(Cancel))
            : (response.StatusCode, null);
    }

    private static async Task<StoredGoal> GetAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"/learning-goals/{id}", Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StoredGoal>(Cancel))!;
    }

    private static async Task<Dictionary<string, string>> ErrorsAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel));
        return problem.RootElement.GetProperty("errors").EnumerateObject()
            .ToDictionary(field => field.Name, field => field.Value.EnumerateArray().Single().GetString()!);
    }

    private sealed record NewGoal(
        string? Kind, string? Name, string? ExamType, string? ExamDate, string? Level, string? OutputLanguage, string? Goal);

    private sealed record EditedGoal(string? Name, string? ExamType, string? ExamDate, string? Level, string? OutputLanguage, string? Goal);

    private sealed record StoredGoal(
        Guid Id,
        string Kind,
        string Name,
        string? ExamType,
        string? ExamDate,
        string? Level,
        string OutputLanguage,
        string? Goal,
        DateTime CreatedAt,
        bool InPlan,
        DateTime? UpdatedAt);
}