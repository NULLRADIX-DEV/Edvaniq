using Microsoft.Extensions.DependencyInjection.Extensions;

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
            .ValidateOnStart();
        proxy.Services.TryAddSingleton(TimeProvider.System);
        proxy.Services.AddHttpClient(nameof(BackendTokens));
        proxy.Services.AddSingleton<BackendTokens>();

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
}