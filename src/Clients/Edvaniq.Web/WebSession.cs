using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Edvaniq.Web;

// The session between browser and web host. The browser only ever holds this cookie: encrypted, out of reach of
// scripts, sent only over HTTPS and only to this site. Whatever the backend needs to know about the caller, the web
// host adds on the server side.
//
// A login (POST /session/login) puts the user's token from Identity into the cookie, encrypted like the rest of it, and
// the session ends with the token. POST /session starts a technical guest session without a user, only for the smoke
// test after a deploy. Pages with [Authorize] want a login, a guest session is not enough for them.
public static class WebSession
{
    public const string Policy = "session";
    public const string UserPolicy = "user";
    public const string CookieName = "__Host-edvaniq-session";
    public const string IdClaim = "sid";
    public const string KindClaim = "kind";
    public const string UserKind = "user";
    public const string TokenName = "access_token";
    public const string LoginPage = "/login";

    public static IServiceCollection AddWebSession(this IServiceCollection services, IConfiguration configuration)
    {
        // The cookie lives only as long as the keys that encrypt it. On the server they sit in a directory of the
        // operating environment, so a session survives restarts, deploys and rollbacks. Without one, as locally and in
        // tests, the default ring applies. The fixed name keeps the ring independent of where the image puts the app.
        var dataProtection = services.AddDataProtection().SetApplicationName("Edvaniq.Web");
        var keys = configuration["DataProtection:KeysDirectory"];
        if (!string.IsNullOrEmpty(keys))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keys));
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<EndedSessions>();
        services.AddWebLogin();

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
                options.LoginPath = LoginPage;
                options.ReturnUrlParameter = "returnUrl";

                // An API answers 401 and 403. A page leads to the login and comes back afterwards.
                options.Events.OnRedirectToLogin = context => Refuse(context, StatusCodes.Status401Unauthorized);
                options.Events.OnRedirectToAccessDenied = context => Refuse(context, StatusCodes.Status403Forbidden);

                // A login that was ended stays ended, even for a copy of its cookie.
                options.Events.OnValidatePrincipal = async context =>
                {
                    var ended = context.HttpContext.RequestServices.GetRequiredService<EndedSessions>();
                    if (context.Principal?.FindFirstValue(IdClaim) is { } id && ended.HasEnded(id))
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync();
                    }
                };
            });

        // [Authorize] on a page means the default policy, so that is the login.
        var user = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(KindClaim, UserKind)
            .Build();
        services.AddAuthorizationBuilder()
            .AddPolicy(Policy, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(UserPolicy, user)
            .SetDefaultPolicy(user);

        return services;
    }

    public static IEndpointRouteBuilder MapWebSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var session = app.MapGroup("/session")
            .WithMetadata(new SkipStatusCodePagesAttribute());

        // Whether there is a session, a guest session included.
        session.MapGet("/", () => TypedResults.NoContent())
            .RequireAuthorization(Policy);

        // Whether a user is signed in. A guest session is none.
        session.MapGet("/user", (HttpContext context) =>
            IsUser(context.User) ? Results.NoContent() : Results.StatusCode(StatusCodes.Status401Unauthorized));

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

        session.MapPost("/login", WebLogin.LoginAsync)
            .RequireRateLimiting(WebLogin.RateLimitPolicy);

        session.MapDelete("/", async (HttpContext context) =>
        {
            await EndAsync(context);
            await context.SignOutAsync();
            return TypedResults.NoContent();
        });

        return app;
    }

    public static bool IsUser(ClaimsPrincipal user) => user.HasClaim(KindClaim, UserKind);

    // Signs the user in with the token from Identity. The cookie ends with the token and is not renewed on the way.
    internal static async Task SignInUserAsync(HttpContext context, string token, DateTimeOffset expires)
    {
        await EndAsync(context);

        var identity = new ClaimsIdentity(
            [new Claim(IdClaim, Guid.NewGuid().ToString("N")), new Claim(KindClaim, UserKind)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        var properties = new AuthenticationProperties
        {
            IsPersistent = false,
            AllowRefresh = false,
            ExpiresUtc = expires,
        };
        properties.StoreTokens([new AuthenticationToken { Name = TokenName, Value = token }]);
        await context.SignInAsync(new ClaimsPrincipal(identity), properties);
    }

    // Remembers a login as ended until its token would have run out. A guest session has nothing to remember.
    private static async Task EndAsync(HttpContext context)
    {
        if (!IsUser(context.User) || context.User.FindFirstValue(IdClaim) is not { } id)
        {
            return;
        }

        var result = await context.AuthenticateAsync();
        var until = result.Properties?.ExpiresUtc
            ?? context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().AddHours(8);
        context.RequestServices.GetRequiredService<EndedSessions>().End(id, until);
    }

    private static Task Refuse(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/api") || path.StartsWithSegments("/session"))
        {
            context.Response.StatusCode = status;
            return Task.CompletedTask;
        }

        // A guest session on a page gets the login too. The way back is always a path of this site.
        var back = context.Request.PathBase + path + context.Request.QueryString;
        context.Response.Redirect(LoginPage + "?returnUrl=" + Uri.EscapeDataString(back));
        return Task.CompletedTask;
    }
}

// Logins that were ended, each until its token would have run out. Only logins get here, and those are limited per
// client address and per account, so the list stays small. It still never holds more than Limit, the next to run out
// goes first. In memory only: after a restart a copy of an ended cookie works again until its token runs out.
internal sealed class EndedSessions(TimeProvider time)
{
    public const int Limit = 10_000;

    private readonly Lock gate = new();
    private readonly Dictionary<string, DateTimeOffset> ended = [];

    public int Count
    {
        get
        {
            lock (gate)
            {
                return ended.Count;
            }
        }
    }

    public void End(string id, DateTimeOffset until)
    {
        lock (gate)
        {
            var now = time.GetUtcNow();
            foreach (var (key, end) in ended)
            {
                if (end <= now)
                {
                    ended.Remove(key);
                }
            }

            if (ended.Count >= Limit && !ended.ContainsKey(id))
            {
                ended.Remove(ended.MinBy(entry => entry.Value).Key);
            }

            ended[id] = until;
        }
    }

    public bool HasEnded(string id)
    {
        lock (gate)
        {
            return ended.TryGetValue(id, out var until) && time.GetUtcNow() < until;
        }
    }
}

// The section Login. How many logins a client address may try in a minute, right or wrong, before the web host answers
// 429 itself. Identity adds its own limit per account.
internal sealed class LoginOptions
{
    public const string Section = "Login";

    public int PerClientPerMinute { get; set; }
}

internal static class LoginLimit
{
    public static IServiceCollection AddLoginLimit(this IServiceCollection services)
    {
        services.AddOptions<LoginOptions>()
            .BindConfiguration(LoginOptions.Section)
            .Validate(options => options.PerClientPerMinute > 0, "Login:PerClientPerMinute must be above 0.")
            .ValidateOnStart();

        // The client address is the browser's, behind nginx from X-Forwarded-For (Program.cs).
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };
            options.AddPolicy(WebLogin.RateLimitPolicy, context =>
            {
                var limits = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<LoginOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    "login:" + context.Connection.RemoteIpAddress,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.PerClientPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }
}