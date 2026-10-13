using System.Threading.RateLimiting;

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Yarp.ReverseProxy.Model;
using Yarp.ReverseProxy.Transforms;

namespace Edvaniq.Web;

// The token the backend sees for a call from the browser. There is no login yet, so it is the web host's own technical
// token from Identity. Only routes with the metadata BackendToken=true carry it: what the browser sent as
// Authorization has already been removed there, the token takes its place. The browser never gets to see it.
//
// A real login will put the user's token here instead. The services do not notice the difference.
public static class BackendToken
{
    public const string MetadataKey = "BackendToken";

    public static IReverseProxyBuilder AddBackendToken(this IReverseProxyBuilder proxy)
    {
        proxy.Services.AddOptions<BackendTokenOptions>()
            .BindConfiguration(BackendTokenOptions.Section)
            .Validate(options => !string.IsNullOrEmpty(options.ClientSecret), "BackendToken:ClientSecret is missing.")
            .Validate(
                options => options.PerClientPerMinute > 0 && options.TotalPerMinute > 0,
                "BackendToken:PerClientPerMinute and BackendToken:TotalPerMinute must be above 0.")
            .ValidateOnStart();
        proxy.Services.TryAddSingleton(TimeProvider.System);
        proxy.Services.AddHttpClient(nameof(BackendTokens));
        proxy.Services.AddSingleton<BackendTokens>();

        // Anyone can start a guest session and send what carries the token, and the ping writes a row each time. So
        // these calls count, per client address and in total, and the database gets at most TotalPerMinute writes a
        // minute however many addresses send. Beyond that the answer is 429, other calls pass freely. The client
        // address is the browser's, behind nginx from X-Forwarded-For (Program.cs).
        proxy.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                Limit(context => "client:" + context.Connection.RemoteIpAddress, limits => limits.PerClientPerMinute),
                Limit(_ => "total", limits => limits.TotalPerMinute));
        });

        proxy.AddTransforms(context =>
        {
            if (context.Route.Metadata?.GetValueOrDefault(MetadataKey) != "true")
            {
                return;
            }

            context.AddRequestTransform(AddTokenAsync);
        });

        return proxy;
    }

    // A window of a minute per key for calls with the token. The keys never meet the one of the calls without it.
    private static PartitionedRateLimiter<HttpContext> Limit(Func<HttpContext, string> key, Func<BackendTokenOptions, int> permits) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<RouteModel>()?.Config.Metadata?.GetValueOrDefault(MetadataKey) != "true")
            {
                return RateLimitPartition.GetNoLimiter("free");
            }

            var limits = context.RequestServices.GetRequiredService<IOptions<BackendTokenOptions>>().Value;
            return RateLimitPartition.GetFixedWindowLimiter(key(context), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits(limits),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
        });

    // Without a token the call would only fail at the service. Setting the status here keeps YARP from forwarding it.
    private static async ValueTask AddTokenAsync(RequestTransformContext transform)
    {
        var context = transform.HttpContext;
        try
        {
            var token = await context.RequestServices.GetRequiredService<BackendTokens>().GetAsync(context.RequestAborted);
            transform.ProxyRequest.Headers.Authorization = new("Bearer", token);
        }
        catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            context.RequestServices.GetRequiredService<ILogger<BackendTokens>>()
                .LogWarning(exception, "No token from Identity, the call to {Path} is not forwarded.", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
        }
    }
}

// The section BackendToken. The client secret comes only from the environment.
internal sealed class BackendTokenOptions
{
    public const string Section = "BackendToken";

    public string TokenEndpoint { get; set; } = "";

    public string ClientId { get; set; } = "";

    public string ClientSecret { get; set; } = "";

    public int PerClientPerMinute { get; set; }

    public int TotalPerMinute { get; set; }
}