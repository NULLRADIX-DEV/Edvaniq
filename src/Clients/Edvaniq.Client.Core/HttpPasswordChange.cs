using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edvaniq.Client.Core;

// Talks to Identity through the web host, which wants a login. Identity puts the sentence for the user into the title of
// its problems and tells them apart by the code, so the page can show each at its field.
public sealed class HttpPasswordChange(HttpClient http) : IPasswordChange
{
    public const string Path = "api/identity/accounts/password";

    // The codes of Identity's refusals (docs/tokens.md, "Passwort ändern").
    private const string CurrentInvalid = "current_password_invalid";
    private const string PasswordRules = "password_rules";

    public Task<CallOutcome<bool>> ChangeAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default) =>
        HostCall.GuardAsync(
            async () =>
            {
                using var response = await http.PostAsJsonAsync(
                    Path, new PasswordBody(currentPassword, newPassword), PasswordJson.Default.PasswordBody, cancellationToken);
                switch (response.StatusCode)
                {
                    case HttpStatusCode.NoContent:
                        return CallOutcome<bool>.Ok(true);
                    case HttpStatusCode.Unauthorized:
                        return CallOutcome<bool>.SignedOut;
                    case HttpStatusCode.TooManyRequests:
                        return new CallOutcome<bool>(
                            CallResult.Limited, Messages: await ProblemTitle.ReadAsync(response, cancellationToken) is { } wait ? [wait] : []);
                    case HttpStatusCode.BadRequest:
                        var problem = await ReadAsync(response, cancellationToken);
                        var field = problem?.Code switch
                        {
                            CurrentInvalid => PasswordChangeFields.Current,
                            PasswordRules => PasswordChangeFields.New,
                            _ => null,
                        };
                        return field is not null && ProblemTitle.Sentence(problem?.Title) is { } sentence
                            ? new CallOutcome<bool>(
                                CallResult.Rejected, Messages: [sentence], FieldErrors: new Dictionary<string, string> { [field] = sentence })
                            : CallOutcome<bool>.Failed(ProblemTrace.Check(problem?.TraceId));
                    case HttpStatusCode.NotFound:
                        return new CallOutcome<bool>(CallResult.NotFound);
                    default:
                        return CallOutcome<bool>.Failed(await ProblemTrace.ReadAsync(response, cancellationToken));
                }
            },
            cancellationToken);

    // Nothing for a body that is no problem, an HTML page from a proxy for instance.
    private static async Task<PasswordProblem?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return await response.Content.ReadFromJsonAsync(PasswordJson.Default.PasswordProblem, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed record PasswordBody(string CurrentPassword, string NewPassword);

internal sealed record PasswordProblem(string? Code = null, string? Title = null, string? TraceId = null);

// Source-generated for the trimmed browser app.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(PasswordBody))]
[JsonSerializable(typeof(PasswordProblem))]
internal sealed partial class PasswordJson : JsonSerializerContext;
