using Microsoft.Extensions.Primitives;

namespace Edvaniq.Web;

// Second wall next to SameSite=Strict: a request that changes something and doesn't come from our own pages is refused
// before anything else sees it, the gateway included. Callers that aren't browsers (the app, the smoke test) send
// neither Sec-Fetch-Site nor Origin and pass, they have no user's cookie to abuse.
public static class OriginCheck
{
    public static IApplicationBuilder UseOriginCheck(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (!ChangesState(context.Request.Method) || IsFromOwnSite(context.Request))
            {
                await next();
                return;
            }

            var request = context.Request;
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(OriginCheck)).LogInformation(
                "Refused {Method} {Path}: Sec-Fetch-Site {Site}, Origin {Origin}, Host {Host}",
                request.Method,
                request.Path,
                request.Headers["Sec-Fetch-Site"].ToString(),
                request.Headers.Origin.ToString(),
                request.Host.ToString());

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = { Status = StatusCodes.Status403Forbidden },
            });
        });

    private static bool ChangesState(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    // A page can't set Sec-Fetch-Site, so it decides when it's there. Origin is only the fallback for older browsers,
    // so a proxy that doesn't pass the public host can't break the modern ones.
    private static bool IsFromOwnSite(HttpRequest request) =>
        request.Headers.TryGetValue("Sec-Fetch-Site", out var site)
            ? site == "same-origin"
            : !request.Headers.TryGetValue("Origin", out var origin) || IsOwnOrigin(origin, request);

    // Compared with the public scheme and host, UseForwardedHeaders runs before. "null" (a sandboxed frame, a redirect
    // across sites) fails here too.
    private static bool IsOwnOrigin(StringValues origin, HttpRequest request) =>
        origin.Count == 1
        && Uri.TryCreate(origin[0], UriKind.Absolute, out var given)
        && Uri.TryCreate($"{request.Scheme}://{request.Host}", UriKind.Absolute, out var own)
        && given.Equals(own);
}