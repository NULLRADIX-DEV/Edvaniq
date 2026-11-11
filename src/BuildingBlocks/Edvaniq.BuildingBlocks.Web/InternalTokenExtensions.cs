using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Edvaniq.BuildingBlocks.Web;

// Tokens one service gets for itself to ask another (docs/tokens.md). They carry their own audience, so the normal check
// of every service and the gateway turns them away with 401: such a token never acts as a user. Only an endpoint that
// asks for it with RequireInternalClient takes one, and only from the client it names.
public static class InternalTokenExtensions
{
    public const string Audience = "edvaniq-internal";

    public const string Scheme = "Internal";

    // Call it after AddTokenValidation. Issuer and keys are the same as for Bearer, only the audience differs.
    public static TBuilder AddInternalTokenValidation<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddAuthentication().AddJwtBearer(Scheme, options => options.MapInboundClaims = false);
        builder.Services.AddOptions<JwtBearerOptions>(Scheme)
            .Configure<IOptionsMonitor<JwtBearerOptions>>((options, all) =>
            {
                var bearer = all.Get(JwtBearerDefaults.AuthenticationScheme);
                options.Authority = bearer.Authority;
                options.MetadataAddress = bearer.MetadataAddress;
                options.RequireHttpsMetadata = bearer.RequireHttpsMetadata;
                options.Configuration = bearer.Configuration;
                options.ConfigurationManager = bearer.ConfigurationManager;
                options.TokenValidationParameters = bearer.TokenValidationParameters.Clone();
                options.TokenValidationParameters.ValidAudience = null;
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