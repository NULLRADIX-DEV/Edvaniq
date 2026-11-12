using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// #512: the settings page sets one daily budget for every day, says what is wrong at the field and leads to the plan.
public sealed class DailyBudgetTests : BunitContext
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private readonly FakeDailyBudget budget = new();

    public DailyBudgetTests()
    {
        Services.AddSingleton<IDailyBudget>(budget);
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    // AC4: a visible label with the unit, the hint hangs on the field, a number keyboard on a phone.
    [Fact]
    public void Field_HasALabelWithTheUnit()
    {
        var page = Render<Settings>();

        Assert.Equal(FeedbackTexts.Budget.PageHeading, page.Find("h1").TextContent);
        Assert.Equal(FeedbackTexts.Budget.Label + " (Pflichtfeld)", page.Find("label[for=budget-minutes]").TextContent);
        var input = page.Find("input#budget-minutes");
        Assert.Equal("number", input.GetAttribute("type"));
        Assert.Equal("numeric", input.GetAttribute("inputmode"));
        Assert.Equal("1", input.GetAttribute("min"));
        Assert.Equal("720", input.GetAttribute("max"));
        Assert.Equal("budget-minutes-hint", input.GetAttribute("aria-describedby"));
        Assert.Equal("60", input.GetAttribute("value"));
    }

    // AC1 and AC3: the value goes out once for all days, the page says it counts for today's plan and leads there.
    [Fact]
    public void Save_SetsAllDays_AndLeadsToThePlan()
    {
        var page = Render<Settings>();

        page.Find("#budget-minutes").Input("45");
        page.Find("form").Submit();

        Assert.Equal([45], budget.Set);
        Assert.Equal(FeedbackTexts.Budget.SavedWith(45), page.Find("[role=status] .budget-saved p").TextContent);
        var link = page.Find(".state-actions a");
        Assert.Equal(FeedbackTexts.Budget.ToPlan, link.TextContent);
        Assert.Equal("./", link.GetAttribute("href"));
    }

    // AC2: anything but a whole number from 1 to 720 gets Planning's sentence at the field, nothing goes out.
    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("721")]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("abc")]
    public void InvalidValue_IsNamedAtTheField_AndNotSent(string value)
    {
        var page = Render<Settings>();

        page.Find("#budget-minutes").Input(value);
        page.Find("form").Submit();

        Assert.Empty(budget.Set);
        AssertFieldError(page, DailyBudgetValues.OutOfRange);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("720")]
    public void Limits_AreAllowed(string value)
    {
        var page = Render<Settings>();

        page.Find("#budget-minutes").Input(value);
        page.Find("form").Submit();

        Assert.Equal([int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)], budget.Set);
        Assert.Null(page.Find("#budget-minutes").GetAttribute("aria-invalid"));
    }

    // AC2: Planning's own verdict lands at the field too.
    [Fact]
    public void RefusedByPlanning_IsNamedAtTheField()
    {
        budget.Answer(new CallOutcome<DailyBudgetInfo>(CallResult.Rejected, Messages: ["Satz von Planning."]));
        var page = Render<Settings>();

        page.Find("form").Submit();

        AssertFieldError(page, "Satz von Planning.");
        Assert.Empty(page.FindAll(".budget-saved"));
    }

    [Fact]
    public void SaveNotThrough_SaysSo_WithTheTrace_AndKeepsTheValue()
    {
        budget.Answer(CallOutcome<DailyBudgetInfo>.Failed(TraceId));
        var page = Render<Settings>();

        page.Find("#budget-minutes").Input("90");
        page.Find("form").Submit();

        Assert.Contains(FeedbackTexts.Budget.NotThrough, page.Find("form [role=alert]").TextContent);
        Assert.Equal(TraceId, page.Find("form [role=alert] .request-id code").TextContent);
        Assert.Equal("90", page.Find("#budget-minutes").GetAttribute("value"));
        Assert.Empty(page.FindAll(".budget-saved"));
    }

    // Days set one by one elsewhere: the field shows the value of most days and says that saving makes them equal.
    [Fact]
    public void MixedDays_ShowTheUsualValue_WithAHint()
    {
        budget.Get(CallOutcome<DailyBudgetInfo>.Ok(FakeDailyBudget.Week(10, 30, 30, 45, 30, 45, 60)));
        var page = Render<Settings>();

        Assert.Equal("30", page.Find("#budget-minutes").GetAttribute("value"));
        Assert.Equal(FeedbackTexts.Budget.Mixed, page.Find(".budget-note").TextContent);

        page.Find("form").Submit();

        Assert.Equal([30], budget.Set);
        Assert.Empty(page.FindAll(".budget-note"));
    }

    [Fact]
    public void Default_SaysSo()
    {
        budget.Get(CallOutcome<DailyBudgetInfo>.Ok(FakeDailyBudget.Week(60) with { IsDefault = true }));

        var page = Render<Settings>();

        Assert.Equal(FeedbackTexts.Budget.IsDefault, page.Find(".budget-note").TextContent);
    }

    [Fact]
    public void LoadNotThrough_ShowsAHint_AndLoadsAgainOnRetry()
    {
        budget.Get(CallOutcome<DailyBudgetInfo>.Failed(TraceId)).Get(CallOutcome<DailyBudgetInfo>.Ok(FakeDailyBudget.Week(20)));
        var page = Render<Settings>();

        Assert.Contains(FeedbackTexts.Budget.LoadFailed, page.Find("[role=status]").TextContent);
        Assert.Empty(page.FindAll("form"));

        page.Find(".state-actions button").Click();

        Assert.Equal(2, budget.Gets);
        Assert.Equal("20", page.Find("#budget-minutes").GetAttribute("value"));
    }

    [Fact]
    public void WhilePrerendering_NothingIsAsked()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));

        var page = Render<Settings>();

        Assert.Equal(0, budget.Gets);
        Assert.Empty(page.FindAll("form"));
    }

    private static void AssertFieldError(IRenderedComponent<Settings> page, string message)
    {
        var input = page.Find("#budget-minutes");
        Assert.Equal("true", input.GetAttribute("aria-invalid"));
        Assert.Contains("budget-minutes-error", input.GetAttribute("aria-describedby"), StringComparison.Ordinal);
        Assert.EndsWith(message, page.Find("#budget-minutes-error").TextContent, StringComparison.Ordinal);
    }
}