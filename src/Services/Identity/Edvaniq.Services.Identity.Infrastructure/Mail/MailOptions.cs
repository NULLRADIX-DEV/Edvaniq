using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Identity.Infrastructure.Mail;

// How Identity sends its mails (#248), section Mail.
public sealed class MailOptions
{
    public const string Section = "Mail";

    public MailProvider Provider { get; set; } = MailProvider.None;

    // The address of the web app the links in a mail lead to, like https://app.example.org/. Always required, so the
    // links are right from the first mail on. HTTPS, only a loopback address may use HTTP for local runs.
    public string? PublicBaseUrl { get; set; }

    // Whether the worker sends the mails in the background. Tests switch it off and send by hand.
    public bool Dispatch { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(10);

    public Uri BaseAddress => new(PublicBaseUrl!.EndsWith('/') ? PublicBaseUrl : PublicBaseUrl + "/");
}

public enum MailProvider
{
    // No mail service yet (#247): mails wait in the outbox until their MaxAge and then go.
    None,

    // Keeps the mails in memory and sends nothing, for tests and local runs.
    Recording,
}

public sealed class MailOptionsValidation : IValidateOptions<MailOptions>
{
    public ValidateOptionsResult Validate(string? name, MailOptions options) =>
        Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var address)
        && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp && address.IsLoopback)
        && string.IsNullOrEmpty(address.Query) && string.IsNullOrEmpty(address.Fragment) && string.IsNullOrEmpty(address.UserInfo)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{MailOptions.Section}:PublicBaseUrl must be the absolute HTTPS address of the web app, without query or fragment.");
}