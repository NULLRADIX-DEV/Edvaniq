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
        return app;
    }

    private static async Task<IResult> RegisterAsync(RegistrationBody? body, Registration registration, CancellationToken cancellationToken)
    {
        var result = await registration.RegisterAsync(
            new RegistrationRequest(body?.Email, body?.Password, body?.InviteCode, body?.Pseudonym, body?.BirthYear), cancellationToken);
        return result.Accepted
            ? TypedResults.Accepted((string?)null, new Accepted(result.MailDelayed))
            : Refused(result.Refusal!, result.Detail!);
    }

    public static IResult Refused(string code, string title) =>
        TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });

    private sealed record RegistrationBody(string? Email, string? Password, string? InviteCode, string? Pseudonym, int? BirthYear);

    // Whether the mail may come late (SystemMails.DelayedAsync), the same for a new address and a known one.
    private sealed record Accepted(bool MailDelayed);
}