using System.Security.Claims;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Edvaniq.Services.Identity.Api.Tokens;

// Identity checks a user's token for the few endpoints that take one (#254). It is the issuer, so it needs no request
// to a key set: it checks with the public half of its own signing key, with issuer and audience as every service does.
// Endpoints without the policy stay as they are, there is no fallback policy.
internal static class UserTokenCheck
{
    public const string Policy = "user";

    public static TBuilder AddUserTokenCheck<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            // Keep "sub" as "sub" instead of renaming it to the long SOAP claim type.
            .AddJwtBearer(options => options.MapInboundClaims = false);

        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<TokenOptions>, SigningKey>((options, tokens, key) =>
            {
                // No Authority: the keys are the ones in this process, whatever the configuration says.
                options.Authority = null;
                options.MetadataAddress = "";
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = tokens.Value.Issuer,
                    ValidateAudience = true,
                    ValidAudience = tokens.Value.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = key.ValidationKeys,
                    ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
                    ValidTypes = ["at+jwt"],
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ValidateLifetime = true,
                    NameClaimType = "sub",
                };
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Policy, policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(context => AccountOf(context.User) is not null));

        return builder;
    }

    // The account of a user's token. A client's own token names the client as subject (RFC 9068), so it has none, and a
    // token of an internal client is turned away by its audience already.
    public static Guid? AccountOf(ClaimsPrincipal user) =>
        user.FindFirst("sub")?.Value is { } subject
        && Guid.TryParseExact(subject, "D", out var id)
        && user.FindFirst("client_id")?.Value != subject
            ? id
            : null;
}
