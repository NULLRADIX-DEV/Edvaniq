using Edvaniq.Services.Identity.Application;

namespace Edvaniq.Services.Identity.Api.Accounts;

// What a user does with their account without being logged in (docs/tokens.md, "Registrierung"). Open to anyone by
// design: the gateway passes them without a token, the web host limits them per client address. A refusal is a problem
// with the sentence for the user as title and a code the client tells apart.
internal static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/accounts", RegisterAsync).AllowAnonymous();
        app.MapPost("/accounts/confirmations", ConfirmAsync).AllowAnonymous();
        app.MapPost("/accounts/confirmations/resend", ResendAsync).AllowAnonymous();

        // The versions a registration has to agree to (#261). The same for everyone and rarely new, so caches may keep
        // it a few minutes.
        app.MapGet("/legal-versions", (HttpContext context, LegalVersions legal) =>
        {
            context.Response.Headers.CacheControl = "public, max-age=300";
            return TypedResults.Ok(new LegalVersionsAnswer(legal.Terms, legal.Privacy));
        }).AllowAnonymous();

        return app;
    }

    private static async Task<IResult> RegisterAsync(RegistrationBody? body, Registration registration, CancellationToken cancellationToken)
    {
        var result = await registration.RegisterAsync(
            new RegistrationRequest(
                body?.Email, body?.Password, body?.InviteCode, body?.Pseudonym, body?.BirthYear, body?.AcceptedTermsVersion, body?.AcceptedPrivacyVersion),
            cancellationToken);
        return result.Accepted
            ? TypedResults.Accepted((string?)null, new Accepted(result.MailDelayed))
            : Refused(result.Refusal!, result.Detail!);
    }

    public static IResult Refused(string code, string title) =>
        TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });

    private static async Task<IResult> ConfirmAsync(TokenBody? body, Confirmation confirmation, CancellationToken cancellationToken) =>
        await confirmation.ConfirmAsync(body?.Token, cancellationToken) switch
        {
            ConfirmResult.Confirmed => TypedResults.NoContent(),
            ConfirmResult.Expired => Refused("token_expired", "Dieser Link ist abgelaufen. Fordere einen neuen an."),
            ConfirmResult.Used => Refused("token_used", "Dieser Link wurde schon benutzt oder durch einen neueren ersetzt."),
            _ => Refused("token_invalid", "Dieser Link stimmt nicht. Kopier ihn vollständig aus der Mail oder fordere einen neuen an."),
        };

    private static async Task<IResult> ResendAsync(EmailBody? body, Confirmation confirmation, CancellationToken cancellationToken) =>
        TypedResults.Accepted((string?)null, new Accepted(await confirmation.ResendAsync(body?.Email, cancellationToken)));

    private sealed record LegalVersionsAnswer(string TermsVersion, string PrivacyVersion);

    private sealed record TokenBody(string? Token);

    private sealed record EmailBody(string? Email);

    private sealed record RegistrationBody(
        string? Email, string? Password, string? InviteCode, string? Pseudonym, int? BirthYear, string? AcceptedTermsVersion, string? AcceptedPrivacyVersion);

    // Whether the mail may come late (SystemMails.DelayedAsync), the same for a new address and a known one.
    private sealed record Accepted(bool MailDelayed);
}