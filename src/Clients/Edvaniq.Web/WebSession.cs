using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace Edvaniq.Web;

// The session between browser and web host. The browser only ever holds this cookie: encrypted, out of reach of
// scripts, sent only over HTTPS and only to this site. Whatever the backend needs to know about the caller, the web
// host adds on the server side.
//
// There is no login yet. POST /session starts a technical guest session; a real login will replace only that endpoint.
public static class WebSession
{
    public const string Policy = "session";
    public const string CookieName = "__Host-edvaniq-session";
    public const string IdClaim = "sid";

    public static IServiceCollection AddWebSession(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                // __Host- makes the browser refuse the cookie unless it is Secure, has Path=/ and no Domain.
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;

                // An API answers 401 and 403. The default would redirect to a login page that does not exist.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policy, policy => policy.RequireAuthenticatedUser());

        return services;
    }

    public static IEndpointRouteBuilder MapWebSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var session = app.MapGroup("/session")
            .WithMetadata(new SkipStatusCodePagesAttribute());

        session.MapGet("/", () => TypedResults.NoContent())
            .RequireAuthorization(Policy);

        // Starting a session twice keeps the first one.
        session.MapPost("/", async (HttpContext context) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                var identity = new ClaimsIdentity(
                    [new Claim(IdClaim, Guid.NewGuid().ToString("N"))],
                    CookieAuthenticationDefaults.AuthenticationScheme);
                await context.SignInAsync(new ClaimsPrincipal(identity));
            }

            return TypedResults.NoContent();
        });

        session.MapDelete("/", async (HttpContext context) =>
        {
            await context.SignOutAsync();
            return TypedResults.NoContent();
        });

        return app;
    }
}