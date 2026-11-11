namespace Edvaniq.Services.Planning.Application;

// The default daily budget from configuration (section DailyBudget in appsettings.json).
public sealed class DailyBudgetOptions
{
    public const string Section = "DailyBudget";

    public int DefaultMinutes { get; set; }
}