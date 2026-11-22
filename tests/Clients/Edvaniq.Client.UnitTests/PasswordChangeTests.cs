using Bunit;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Client.UI.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Client.UnitTests;

// #254: the settings page changes the password: three fields, the repeat checked before anything goes out, Identity's
// sentence at its field, a hint to wait, and a confirmation that leaves the session alone.
public sealed class PasswordChangeTests : BunitContext
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private readonly FakePasswordChange passwords = new();

    public PasswordChangeTests()
    {
        Services.AddSingleton<IDailyBudget>(new FakeDailyBudget());
        Services.AddSingleton<IPasswordChange>(passwords);
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("WebAssembly", isInteractive: true));
    }

    // Labels, password type and the autocomplete values a password manager reads.
    [Fact]
    public void Section_HasThreeLabelledPasswordFields()
    {
        var page = Render<Settings>();

        Assert.Equal(FeedbackTexts.Password.Heading, page.Find("#password-heading").TextContent);
        Assert.Equal("password-heading", page.Find(".password-section").GetAttribute("aria-labelledby"));
        AssertField(page, "password-current", FeedbackTexts.Password.CurrentLabel, "current-password");
        AssertField(page, "password-new", FeedbackTexts.Password.NewLabel, "new-password");
        AssertField(page, "password-repeat", FeedbackTexts.Password.RepeatLabel, "new-password");
        Assert.Equal("password-new-hint", page.Find("#password-new").GetAttribute("aria-describedby"));
        Assert.Equal(FeedbackTexts.Password.Save, page.Find(".password-form button[type=submit]").TextContent);
    }

    [Fact]
    public void Success_ConfirmsAndClearsTheFields()
    {
        var page = Render<Settings>();

        Fill(page, "the old password", "the new password", "the new password");
        Submit(page);

        Assert.Equal([("the old password", "the new password")], passwords.Sent);
        Assert.Equal(FeedbackTexts.Password.Changed, page.Find(".password-status .password-changed p").TextContent);
        Assert.All(new[] { "password-current", "password-new", "password-repeat" }, id => Assert.Equal("", page.Find($"#{id}").GetAttribute("value") ?? ""));
        Assert.Empty(page.FindAll(".password-form .field-error"));
    }

    // Typing again takes the confirmation away, it was about the last change.
    [Fact]
    public void Typing_AfterSuccess_RemovesTheConfirmation()
    {
        var page = Render<Settings>();
        Fill(page, "the old password", "the new password", "the new password");
        Submit(page);

        page.Find("#password-current").Input("x");

        Assert.Empty(page.FindAll(".password-changed"));
    }

    // The repeat is the client's to check: nothing goes out.
    [Fact]
    public void RepeatThatDiffers_IsNamedAtTheField_AndNotSent()
    {
        var page = Render<Settings>();

        Fill(page, "the old password", "the new password", "another new password");
        Submit(page);

        Assert.Empty(passwords.Sent);
        AssertFieldError(page, "password-repeat", FeedbackTexts.Password.Mismatch);
        Assert.Empty(page.FindAll(".password-changed"));
    }

    [Fact]
    public void EmptyFields_AreNamed_AndNotSent()
    {
        var page = Render<Settings>();

        Submit(page);

        Assert.Empty(passwords.Sent);
        AssertFieldError(page, "password-current", FeedbackTexts.Password.CurrentMissing);
        AssertFieldError(page, "password-new", FeedbackTexts.Password.NewMissing);
    }

    [Fact]
    public void WrongCurrentPassword_ShowsIdentitysSentence_AtThatField_AndKeepsTheFields()
    {
        const string sentence = "Dein aktuelles Passwort stimmt nicht.";
        passwords.Answer(Rejected(PasswordChangeFields.Current, sentence));
        var page = Render<Settings>();

        Fill(page, "a wrong password", "the new password", "the new password");
        Submit(page);

        AssertFieldError(page, "password-current", sentence);
        Assert.Empty(page.FindAll("#password-new-error"));
        Assert.Equal("the new password", page.Find("#password-new").GetAttribute("value"));
        Assert.Empty(page.FindAll(".password-changed"));
    }

    [Fact]
    public void RuleOfTheNewPassword_ShowsIdentitysSentence_AtTheNewField()
    {
        const string sentence = "Dein Passwort braucht mindestens 12 Zeichen.";
        passwords.Answer(Rejected(PasswordChangeFields.New, sentence));
        var page = Render<Settings>();

        Fill(page, "the old password", "short", "short");
        Submit(page);

        AssertFieldError(page, "password-new", sentence);
        Assert.Empty(page.FindAll("#password-current-error"));
    }

    // 429: the sentence about waiting, in the alert region and not at a field.
    [Fact]
    public void TooManyTries_SaysToWait_WithIdentitysSentence()
    {
        const string sentence = "Zu viele falsche Passwörter. Warte ein paar Minuten.";
        passwords.Answer(new CallOutcome<bool>(CallResult.Limited, Messages: [sentence]));
        var page = Render<Settings>();

        Fill(page, "the old password", "the new password", "the new password");
        Submit(page);

        Assert.Equal(sentence, page.Find(".password-form [role=alert] .password-limited p").TextContent);
        Assert.Empty(page.FindAll(".password-form .field-error"));
    }

    [Fact]
    public void TooManyTries_WithoutASentence_UsesTheOwn()
    {
        passwords.Answer(new CallOutcome<bool>(CallResult.Limited, Messages: []));
        var page = Render<Settings>();

        Fill(page, "the old password", "the new password", "the new password");
        Submit(page);

        Assert.Equal(FeedbackTexts.Password.Limited, page.Find(".password-limited p").TextContent);
    }

    [Fact]
    public void NotThrough_SaysSo_WithTheTrace_AndKeepsTheFields()
    {
        passwords.Answer(CallOutcome<bool>.Failed(TraceId));
        var page = Render<Settings>();

        Fill(page, "the old password", "the new password", "the new password");
        Submit(page);

        Assert.Contains(FeedbackTexts.Password.NotThrough, page.Find(".password-form [role=alert]").TextContent);
        Assert.Equal(TraceId, page.Find(".password-form [role=alert] .request-id code").TextContent);
        Assert.Equal("the old password", page.Find("#password-current").GetAttribute("value"));
    }

    // The account was deleted while the session still runs: that is no outage, and retrying won't help.
    [Fact]
    public void AccountGone_SaysSo_InsteadOfAnOutage()
    {
        passwords.Answer(new CallOutcome<bool>(CallResult.NotFound));
        var page = Render<Settings>();

        Fill(page, "the old password", "the new password", "the new password");
        Submit(page);

        var alert = page.Find(".password-form [role=alert]").TextContent;
        Assert.Contains(FeedbackTexts.Password.AccountGone, alert);
        Assert.DoesNotContain(FeedbackTexts.Password.NotThrough, alert);
    }

    // The next try starts clean: neither an old error nor an old hint stays.
    [Fact]
    public void NextTry_RemovesWhatWentWrongBefore()
    {
        passwords.Answer(Rejected(PasswordChangeFields.Current, "Dein aktuelles Passwort stimmt nicht.")).Answer(CallOutcome<bool>.Ok(true));
        var page = Render<Settings>();
        Fill(page, "a wrong password", "the new password", "the new password");
        Submit(page);

        Fill(page, "the old password", "the new password", "the new password");
        Submit(page);

        Assert.Empty(page.FindAll(".password-form .field-error"));
        Assert.Equal(2, passwords.Sent.Count);
        Assert.Single(page.FindAll(".password-changed"));
    }

    // The shell leads to the login, the page says nothing of its own and keeps what was typed.
    [Fact]
    public void SignedOut_KeepsTheFields_AndSaysNothing()
    {
        passwords.Answer(CallOutcome<bool>.SignedOut);
        var page = Render<Settings>();

        Fill(page, "the old password", "the new password", "the new password");
        Submit(page);

        Assert.Empty(page.FindAll(".password-changed, .password-failed, .password-limited, .password-form .field-error"));
        Assert.Equal("the new password", page.Find("#password-new").GetAttribute("value"));
    }

    [Fact]
    public void WhilePrerendering_NothingShows()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));

        var page = Render<Settings>();

        Assert.Empty(page.FindAll(".password-section"));
        Assert.Empty(passwords.Sent);
    }

    private static CallOutcome<bool> Rejected(string field, string sentence) =>
        new(CallResult.Rejected, Messages: [sentence], FieldErrors: new Dictionary<string, string> { [field] = sentence });

    private static void Fill(IRenderedComponent<Settings> page, string current, string next, string repeat)
    {
        page.Find("#password-current").Input(current);
        page.Find("#password-new").Input(next);
        page.Find("#password-repeat").Input(repeat);
    }

    private static void Submit(IRenderedComponent<Settings> page) => page.Find(".password-form").Submit();

    private static void AssertField(IRenderedComponent<Settings> page, string id, string label, string autocomplete)
    {
        Assert.Equal(label + " (Pflichtfeld)", page.Find($"label[for={id}]").TextContent);
        var input = page.Find($"input#{id}");
        Assert.Equal("password", input.GetAttribute("type"));
        Assert.Equal(autocomplete, input.GetAttribute("autocomplete"));
    }

    // The message hangs on the field, so a screen reader reads it with the field.
    private static void AssertFieldError(IRenderedComponent<Settings> page, string id, string message)
    {
        var input = page.Find($"#{id}");
        Assert.Equal("true", input.GetAttribute("aria-invalid"));
        Assert.Contains($"{id}-error", input.GetAttribute("aria-describedby"), StringComparison.Ordinal);
        Assert.EndsWith(message, page.Find($"#{id}-error").TextContent, StringComparison.Ordinal);
    }
}
