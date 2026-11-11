using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Content.IntegrationTests;

// The usage adds up per UTC day, per user and per document, for the cost report in M6. The tests share the database,
// so each one uses a day of its own far back and users and documents of its own.
public sealed class AiUsageReportTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    [Fact]
    public async Task Usage_AddsUpPerDayUserAndDocument()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        var bob = $"bob-{Guid.NewGuid():N}";
        var day = AiUsageSeed.SomeDay();
        var first = await AiUsageSeed.AddAsync(service.Services, alice, day, (1, 0.25m), (2, 0.25m));
        var second = await AiUsageSeed.AddAsync(service.Services, alice, day.AddDays(1), (1, null));
        var third = await AiUsageSeed.AddAsync(service.Services, bob, day, (1, 1m));
        using var scope = service.Services.CreateScope();
        var report = scope.ServiceProvider.GetRequiredService<AiUsageReport>();
        var cancellation = TestContext.Current.CancellationToken;

        var days = await report.PerDayAsync(day, day.AddDays(1), cancellation);
        var users = await report.PerUserAsync(day, day.AddDays(1), cancellation);
        var documents = await report.PerDocumentAsync(day, day, cancellation);

        Assert.Equal(new AiUsageTotal(2, 3, 30, 9, 1.5m, 0, 4500), days[day]);
        Assert.Equal(new AiUsageTotal(1, 1, 10, 3, 0m, 1, 1500), days[day.AddDays(1)]);
        Assert.Equal(new AiUsageTotal(2, 3, 30, 9, 0.5m, 1, 4500), users[alice]);
        Assert.Equal(new AiUsageTotal(1, 1, 10, 3, 1m, 0, 1500), users[bob]);
        Assert.Equal(new AiUsageTotal(1, 2, 20, 6, 0.5m, 0, 3000), documents[first]);
        Assert.Equal(new AiUsageTotal(1, 1, 10, 3, 1m, 0, 1500), documents[third]);
        Assert.False(documents.ContainsKey(second));
    }

    // The day ends at midnight UTC, not at the study day's 04:00.
    [Fact]
    public async Task Day_IsTheUtcDay()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        var day = AiUsageSeed.SomeDay();
        await AiUsageSeed.AddAsync(service.Services, alice, day, new TimeOnly(23, 59, 59), (1, 1m));
        await AiUsageSeed.AddAsync(service.Services, alice, day.AddDays(1), new TimeOnly(0, 0, 1), (1, 2m));
        using var scope = service.Services.CreateScope();

        var days = await scope.ServiceProvider.GetRequiredService<AiUsageReport>()
            .PerDayAsync(day, day.AddDays(1), TestContext.Current.CancellationToken);

        Assert.Equal((1m, 2m), (days[day].Cost, days[day.AddDays(1)].Cost));
    }
}

// Attempts written straight to the database, 10 input and 3 output tokens and 1.5 s each, one call per AddAsync.
internal static class AiUsageSeed
{
    // A day of its own for a test, far before any real use.
    public static DateOnly SomeDay() => new DateOnly(2000, 1, 1).AddDays(Random.Shared.Next(0, 9000));

    public static Task<Guid> AddAsync(IServiceProvider services, string user, DateOnly day, params (int Attempt, decimal? Cost)[] attempts) =>
        AddAsync(services, user, day, new TimeOnly(10, 0), attempts);

    // Returns the document of the call.
    public static async Task<Guid> AddAsync(
        IServiceProvider services, string user, DateOnly day, TimeOnly time, params (int Attempt, decimal? Cost)[] attempts)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        var at = day.ToDateTime(time, DateTimeKind.Utc);
        var document = Document.Paste(user, Guid.NewGuid(), null, "Licht spaltet Wasser.", at);
        var call = Guid.NewGuid();
        db.AiAttempts.AddRange(attempts.Select(attempt => AiAttempt.Of(
            call, attempt.Attempt, document, AiAttempt.Topics, "vendor/model-1", "topics-1", at, TimeSpan.FromMilliseconds(1500), 10, 3,
            attempt.Cost, AiAttemptOutcome.Answered)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(attempts.Length, await db.AiAttempts.IgnoreQueryFilters().CountAsync(attempt => attempt.CallId == call, TestContext.Current.CancellationToken));
        return document.Id;
    }
}