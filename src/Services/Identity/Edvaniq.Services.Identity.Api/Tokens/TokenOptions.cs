using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Identity.Api.Tokens;

// The section Tokens: what Identity writes into a token and who may ask for one. Audience and lifetime come from
// appsettings.json. Issuer, signing key and client secrets come only from the environment.
internal sealed class TokenOptions
{
    public const string Section = "Tokens";

    public string Issuer { get; set; } = "";

    public string Audience { get; set; } = "";

    // Of a client's own token (client credentials).
    public TimeSpan Lifetime { get; set; }

    // Of a token for an account (password grant). Longer, there is no refresh token yet (docs/tokens.md).
    public TimeSpan UserLifetime { get; set; }

    // A P-256 private key, PKCS#8 in Base64.
    public string SigningKey { get; set; } = "";

    // Only while the key is changed: the key before, in the same form. Its public half stays in the key set.
    public string? PreviousSigningKey { get; set; }

    // The clients by id: web, the web host, and content, Content's worker asking Knowledge.
    public Dictionary<string, TokenClient> Clients { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class TokenClient
{
    public string Secret { get; set; } = "";

    // Whether the client may log accounts in with their password. Only web, set in appsettings.json.
    public bool AllowPassword { get; set; }

    // The audience of the client's own token, if not Tokens:Audience. Set in appsettings.json.
    public string? Audience { get; set; }
}

// Checked at the start, so a missing value stops Identity instead of the first token request. The messages name the
// key, never its value.
internal sealed class TokenOptionsValidation : IValidateOptions<TokenOptions>
{
    public const int MinimumSecretLength = 32;

    public ValidateOptionsResult Validate(string? name, TokenOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add("Tokens:Issuer is missing.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Tokens:Audience is missing.");
        }

        if (options.Lifetime <= TimeSpan.Zero)
        {
            failures.Add("Tokens:Lifetime must be longer than zero.");
        }

        if (options.UserLifetime <= TimeSpan.Zero)
        {
            failures.Add("Tokens:UserLifetime must be longer than zero.");
        }

        if (SigningKey.TryLoad(options.SigningKey, out var key))
        {
            key.Dispose();
        }
        else
        {
            failures.Add("Tokens:SigningKey must be a P-256 private key, PKCS#8 in Base64.");
        }

        if (!string.IsNullOrEmpty(options.PreviousSigningKey))
        {
            if (!SigningKey.TryLoad(options.PreviousSigningKey, out var previous))
            {
                failures.Add("Tokens:PreviousSigningKey must be a P-256 private key, PKCS#8 in Base64.");
            }
            else
            {
                using (previous)
                {
                    // The same key twice would leave the key set with one kid twice, and the change undone.
                    if (SigningKey.TryLoad(options.SigningKey, out var current))
                    {
                        using (current)
                        {
                            if (SigningKey.PublicKeyOf(current).Kid == SigningKey.PublicKeyOf(previous).Kid)
                            {
                                failures.Add("Tokens:PreviousSigningKey is the same key as Tokens:SigningKey.");
                            }
                        }
                    }
                }
            }
        }

        if (options.Clients.Count == 0)
        {
            failures.Add("Tokens:Clients has no client.");
        }

        foreach (var (id, client) in options.Clients)
        {
            if (client.Secret.Length < MinimumSecretLength)
            {
                failures.Add($"Tokens:Clients:{id}:Secret needs at least {MinimumSecretLength} characters.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}