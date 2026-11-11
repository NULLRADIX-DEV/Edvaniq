using Edvaniq.Services.Planning.Application;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Planning.IntegrationTests;

// The default daily budget comes from the configuration of the service, a wrong value stops the start.
public sealed class DailyBudgetTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    [Fact]
    public async Task DefaultBudget_ComesFromAppSettings()
    {
        var budgets = service.Services.GetRequiredService<IDailyBudgets>();

        var budget = await budgets.ForUserAsync("someone-new", TestContext.Current.CancellationToken);

        Assert.All(
            Enumerable.Range(0, 7),
            offset => Assert.Equal(30, budget.MinutesOn(new DateOnly(2026, 11, 16).AddDays(offset))));
    }

    [Fact]
    public async Task DefaultBudget_CanBeSetInConfiguration()
    {
        await using var configured = service.WithWebHostBuilder(builder => builder.UseSetting("DailyBudget:DefaultMinutes", "50"));

        var budget = await configured.Services.GetRequiredService<IDailyBudgets>()
            .ForUserAsync("someone-new", TestContext.Current.CancellationToken);

        Assert.Equal(50, budget.MinutesOn(new DateOnly(2026, 11, 16)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("721")]
    public void Start_WithBudgetOutsideTheBounds_Fails(string minutes)
    {
        using var configured = service.WithWebHostBuilder(builder => builder.UseSetting("DailyBudget:DefaultMinutes", minutes));

        var exception = Assert.ThrowsAny<OptionsValidationException>(() => configured.CreateClient());
        Assert.Contains("DailyBudget:DefaultMinutes must be between 1 and 720", exception.Message);
    }
}