using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Edvaniq.Services.Planning.Application;
using Edvaniq.Services.Planning.Infrastructure;
using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Planning.IntegrationTests;

// GET and PUT /daily-budget: the user's own minutes per weekday, the configured default until they store some.
public sealed class DailyBudgetTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static readonly Budget Week = new(10, 20, 30, 40, 50, 60, 720);

    [Fact]
    public async Task DefaultBudget_ComesFromAppSettings()
    {
        using var scope = service.Services.CreateScope();
        var budgets = scope.ServiceProvider.GetRequiredService<IDailyBudgets>();

        var budget = await budgets.ForUserAsync("someone-new", TestContext.Current.CancellationToken);

        Assert.All(
            Enumerable.Range(0, 7),
            offset => Assert.Equal(30, budget.MinutesOn(new DateOnly(2026, 11, 16).AddDays(offset))));
    }

    [Fact]
    public async Task DefaultBudget_CanBeSetInConfiguration()
    {
        await using var configured = service.WithWebHostBuilder(builder => builder.UseSetting("DailyBudget:DefaultMinutes", "50"));
        using var scope = configured.Services.CreateScope();

        var budget = await scope.ServiceProvider.GetRequiredService<IDailyBudgets>()
            .ForUserAsync("someone-new", TestContext.Current.CancellationToken);

        Assert.Equal(50, budget.MinutesOn(new DateOnly(2026, 11, 16)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("721")]
    public void Start_WithBudgetOutsideTheBounds_Fails(string minutes)
    {
        using var configured = service.WithWebHostBuilder(builder => builder.UseSetting("DailyBudget:DefaultMinutes", minutes));

        var exception = StartFailure.Of<OptionsValidationException>(() => configured.CreateClient());
        Assert.Contains("DailyBudget:DefaultMinutes must be between 1 and 720", exception.Message);
    }

    // AK3
    [Fact]
    public async Task UserWithoutBudget_GetsTheDefault()
    {
        using var client = ClientOf(NewUser("alice"));

        var budget = await GetAsync(client);

        Assert.Equal(new Budget(30, 30, 30, 30, 30, 30, 30, IsDefault: true), budget);
    }

    // AK1
    [Fact]
    public async Task StoredBudget_ComesBackPerWeekday()
    {
        using var client = ClientOf(NewUser("alice"));

        using var response = await client.PutAsJsonAsync("/daily-budget", Week, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Week, await response.Content.ReadFromJsonAsync<Budget>(TestContext.Current.CancellationToken));
        Assert.Equal(Week, await GetAsync(client));
    }

    [Fact]
    public async Task StoredBudget_ReplacesTheOneBefore()
    {
        using var client = ClientOf(NewUser("alice"));
        await PutAsync(client, Week);
        var changed = Week with { Monday = 90 };

        await PutAsync(client, changed);

        Assert.Equal(changed, await GetAsync(client));
    }

    // The planner asks by owner and gets the stored minutes, the 03:30 boundary is the planner's business.
    [Fact]
    public async Task StoredBudget_IsWhatThePlannerGets()
    {
        var alice = NewUser("alice");
        using var client = ClientOf(alice);
        await PutAsync(client, Week);

        using var scope = service.Services.CreateScope();
        var budget = await scope.ServiceProvider.GetRequiredService<IDailyBudgets>()
            .ForUserAsync(alice, TestContext.Current.CancellationToken);

        Assert.Equal(10, budget.MinutesOn(DayOfWeek.Monday));
        Assert.Equal(720, budget.MinutesOn(DayOfWeek.Sunday));
    }

    // AK2
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(721)]
    public async Task InvalidMinutes_AreRejectedAtTheirDay_AndNothingIsStored(int minutes)
    {
        var alice = NewUser("alice");
        using var client = ClientOf(alice);

        using var response = await client.PutAsJsonAsync("/daily-budget", Week with { Thursday = minutes }, TestContext.Current.CancellationToken);

        var errors = await ValidationErrorsAsync(response);
        Assert.Equal(["Thursday"], errors.Keys);
        Assert.Equal(["Gib für jeden Tag zwischen 1 und 720 Minuten an."], errors["Thursday"]);
        Assert.Equal(0, RowsOf(alice));
    }

    [Fact]
    public async Task MissingDays_AreRejectedAtEachDay()
    {
        using var client = ClientOf(NewUser("alice"));

        using var response = await client.PutAsJsonAsync("/daily-budget", new { monday = 30 }, TestContext.Current.CancellationToken);

        var errors = await ValidationErrorsAsync(response);
        Assert.Equal(["Friday", "Saturday", "Sunday", "Thursday", "Tuesday", "Wednesday"], errors.Keys.Order());
    }

    // A value that is no whole number never reaches the budget: 400 as a problem, with the message at its day.
    [Theory]
    [InlineData("\"abc\"")]
    [InlineData("\"30\"")]
    [InlineData("1.5")]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task MinutesOfTheWrongType_AreRejectedAtTheirDay(string monday)
    {
        var alice = NewUser("alice");
        using var client = ClientOf(alice);
        using var body = new StringContent(
            $$"""{"monday":{{monday}},"tuesday":30,"wednesday":30,"thursday":30,"friday":30,"saturday":30,"sunday":30}""",
            Encoding.UTF8,
            "application/json");

        using var response = await client.PutAsync("/daily-budget", body, TestContext.Current.CancellationToken);

        var errors = await ValidationErrorsAsync(response);
        Assert.Equal(["Monday"], errors.Keys);
        Assert.Equal(0, RowsOf(alice));
    }

    // AK4
    [Fact]
    public async Task Budget_WithoutToken_IsRejected()
    {
        using var client = service.CreateClient();

        using var get = await client.GetAsync("/daily-budget", TestContext.Current.CancellationToken);
        using var put = await client.PutAsJsonAsync("/daily-budget", Week, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
    }

    [Fact]
    public async Task EachUser_ReadsAndChangesOnlyTheirOwnBudget()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        using var aliceClient = ClientOf(alice);
        using var bobClient = ClientOf(bob);
        await PutAsync(aliceClient, Week);

        Assert.True((await GetAsync(bobClient)).IsDefault);

        var bobs = new Budget(15, 15, 15, 15, 15, 15, 15);
        await PutAsync(bobClient, bobs);

        Assert.Equal(Week, await GetAsync(aliceClient));
        Assert.Equal(bobs, await GetAsync(bobClient));
        Assert.Equal(1, RowsOf(alice));
        Assert.Equal(1, RowsOf(bob));
    }

    // Both find no row and insert one, the key lets one through and the other updates it.
    [Fact]
    public async Task SavesAtTheSameTime_LeaveOneRowWithOneOfThem()
    {
        var alice = NewUser("alice");
        Budget[] budgets = [.. Enumerable.Range(1, 2).Select(i => new Budget(i, i, i, i, i, i, i))];

        var responses = await Task.WhenAll(budgets.Select(async budget =>
        {
            using var client = ClientOf(alice);
            using var response = await client.PutAsJsonAsync("/daily-budget", budget, TestContext.Current.CancellationToken);
            return response.StatusCode;
        }));

        Assert.All(responses, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(1, RowsOf(alice));
        using var client = ClientOf(alice);
        Assert.Contains(await GetAsync(client), budgets);
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private HttpClient ClientOf(string user)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        return client;
    }

    private static async Task<Budget> GetAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Budget>("/daily-budget", TestContext.Current.CancellationToken))!;

    private static async Task PutAsync(HttpClient client, Budget budget)
    {
        using var response = await client.PutAsJsonAsync("/daily-budget", budget, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<Dictionary<string, string[]>> ValidationErrorsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return problem.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
    }

    private int RowsOf(string user)
    {
        using var scope = service.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<PlanningDbContext>()
            .DailyBudgets.IgnoreQueryFilters().Count(setting => setting.OwnerId == user);
    }

    private sealed record Budget(
        int Monday,
        int Tuesday,
        int Wednesday,
        int Thursday,
        int Friday,
        int Saturday,
        int Sunday,
        bool IsDefault = false);
}