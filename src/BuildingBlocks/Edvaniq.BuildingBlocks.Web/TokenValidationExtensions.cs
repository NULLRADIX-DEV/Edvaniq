using Edvaniq.BuildingBlocks.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Edvaniq.BuildingBlocks.Web;

public static class TokenValidationExtensions
{
    // Every request needs a valid token. The service only knows the contract from Authentication:Schemes:Bearer, which
    // AddJwtBearer reads by itself: ValidIssuer, ValidAudiences and where the keys come from (Authority outside
    // Development). Whoever issues the tokens can change without a change here.
    public static TBuilder AddTokenValidation<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            // Keep "sub" as "sub" instead of renaming it to the long SOAP claim type.
            .AddJwtBearer(options => options.MapInboundClaims = false);

        // Without a configured issuer or audience .NET silently skips that check, so the service would accept tokens
        // meant for any other app. A missing value stops the start instead.
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Validate(
                options => ChecksIssuerAndAudience(options.TokenValidationParameters),
                "Authentication:Schemes:Bearer needs ValidIssuer and ValidAudiences.")
            .ValidateOnStart();

        // The fallback applies to every endpoint without its own rule, so a new endpoint is protected by default.
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(CurrentUser.IdClaim)
            .Build();
        builder.Services.AddAuthorizationBuilder()
            .SetDefaultPolicy(policy)
            .SetFallbackPolicy(policy);

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, CurrentUser>();

        return builder;
    }

    private static bool ChecksIssuerAndAudience(TokenValidationParameters parameters) =>
        parameters.ValidateIssuer
        && parameters.ValidateAudience
        && (!string.IsNullOrEmpty(parameters.ValidIssuer) || parameters.ValidIssuers?.Any() == true)
        && (!string.IsNullOrEmpty(parameters.ValidAudience) || parameters.ValidAudiences?.Any() == true);
}
