using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Edvaniq.Gateway;

// The token check answers 401 and 403 with a bare status. This adds a problem the caller can read. When the keys of
// Identity can't be fetched, the token may well be fine, so the caller gets 503 and knows to try again instead of
// throwing the token away.
internal static class TokenProblems
{
    private const string KeysUnavailable = "Edvaniq.Gateway.KeysUnavailable";

    public static IServiceCollection AddTokenProblems(this IServiceCollection services) =>
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => options.Events = new JwtBearerEvents
        {
            // The token handler fetches the keys itself and reports a failed fetch only as "no key found", just like a
            // token signed with an unknown key. Asking for the keys once more tells the two apart.
            OnAuthenticationFailed = async context =>
            {
                if (context.Exception is SecurityTokenSignatureKeyNotFoundException
                    && context.Options.ConfigurationManager is { } keys
                    && !await CanFetchAsync(keys, context.HttpContext.RequestAborted))
                {
                    context.HttpContext.Items[KeysUnavailable] = true;
                    context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger(typeof(TokenProblems))
                        .LogWarning("The keys of the token issuer can't be fetched, tokens are not checked.");
                }
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                if (context.Response.HasStarted)
                {
                    return;
                }

                if (context.HttpContext.Items.ContainsKey(KeysUnavailable))
                {
                    await Results.Problem(
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        detail: "Tokens can't be checked right now.").ExecuteAsync(context.HttpContext);
                    return;
                }

                context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;
                await Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    detail: "This path needs a valid token.").ExecuteAsync(context.HttpContext);
            },
            OnForbidden = context => Results.Problem(statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context.HttpContext),
        });

    private static async Task<bool> CanFetchAsync(IConfigurationManager<OpenIdConnectConfiguration> keys, CancellationToken cancellationToken)
    {
        try
        {
            await keys.GetConfigurationAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or IOException)
        {
            return false;
        }
    }
}