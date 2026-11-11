using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.BuildingBlocks.Web;

// Tokens one service gets for itself to ask another (docs/tokens.md). They carry their own audience, so the normal check
// of every service and the gateway turns them away with 401: such a token never acts as a user. Only an endpoint that
// asks for it with RequireInternalClient takes one, and only from the client it names.
public static class InternalTokenExtensions
{
    public const string Audience = "edvaniq-internal";

    public const string Scheme = "Internal";

    // Call it after AddTokenValidation. Issuer and where the keys come from are read from the Bearer section, only the
    // audience differs. Read from the configuration, not from the Bearer options: those come from the same options
    // monitor, and asking it while it builds the Internal options blocks for good.
    public static TBuilder AddInternalTokenValidation<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddAuthentication().AddJwtBearer(Scheme, options => options.MapInboundClaims = false);
        builder.Services.AddOptions<JwtBearerOptions>(Scheme)
            .Configure<IConfiguration>((options, configuration) =>
            {
                var bearer = configuration.GetSection($"Authentication:Schemes:{JwtBearerDefaults.AuthenticationScheme}");
                options.Authority = bearer["Authority"];
                options.MetadataAddress = bearer["MetadataAddress"] ?? "";
                if (bool.TryParse(bearer["RequireHttpsMetadata"], out var requireHttps))
                {
                    options.RequireHttpsMetadata = requireHttps;
                }

                string[] issuers = [.. bearer.GetSection("ValidIssuers").Get<string[]>() ?? [], .. bearer["ValidIssuer"] is { Length: > 0 } issuer ? [issuer] : Array.Empty<string>()];
                options.TokenValidationParameters.ValidIssuers = issuers;
                options.TokenValidationParameters.ValidAudiences = [Audience];
            });

        return builder;
    }

    // Only a token of clientId itself: the internal audience, the client and no user behind it (the subject is the client).
    public static TBuilder RequireInternalClient<TBuilder>(this TBuilder endpoints, string clientId)
        where TBuilder : IEndpointConventionBuilder =>
        endpoints.RequireAuthorization(policy => policy
            .AddAuthenticationSchemes(Scheme)
            .RequireAuthenticatedUser()
            .RequireClaim("client_id", clientId)
            .RequireClaim(CurrentUser.IdClaim, clientId));
}