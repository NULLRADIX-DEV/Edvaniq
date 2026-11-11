using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Edvaniq.Services.Planning.Application;
using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Planning.IntegrationTests;

// #211 AC3: /plan/today switches to the next study day at 04:00 Europe/Berlin, in winter (UTC+1) and in summer (UTC+2).
public sealed class StudyDayBoundaryTests(StudyDayBoundaryTests.ClockedService service) : IClassFixture<StudyDayBoundaryTests.ClockedService>
{
    [Theory]
    [InlineData("2026-11-17T02:59:00Z", "2026-11-16")]
    [InlineData("2026-11-17T03:00:00Z", "2026-11-17")]
    [InlineData("2027-06-15T01:59:00Z", "2027-06-14")]
    [InlineData("2027-06-15T02:00:00Z", "2027-06-15")]
    public async Task Plan_TakesTheStudyDay_ThatStartsAtFourInBerlin(string utcNow, string studyDay)
    {
        service.Clock.Now = DateTimeOffset.Parse(utcNow, System.Globalization.CultureInfo.InvariantCulture);
        using var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create($"alice-{Guid.NewGuid():N}"));

        using var response = await client.GetAsync("/plan/today", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var plan = await response.Content.ReadFromJsonAsync<Plan>(TestContext.Current.CancellationToken);
        Assert.Equal(DateOnly.Parse(studyDay, System.Globalization.CultureInfo.InvariantCulture), plan!.StudyDay);
    }

    // The service with a clock the test sets. Only the plan reads it, outbox and inbox keep the real time.
    public sealed class ClockedService(MySqlTestServer mysql) : ServiceFactory(mysql)
    {
        public TestClock Clock { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
                services.AddScoped(provider => ActivatorUtilities.CreateInstance<TodayPlans>(provider, (TimeProvider)Clock)));
        }
    }

    public sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record Plan(DateOnly StudyDay);
}