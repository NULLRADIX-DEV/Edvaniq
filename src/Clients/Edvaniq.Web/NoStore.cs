namespace Edvaniq.Web;

// The server is the only source of the user's data: what comes from /api and /session is never kept by the browser, so
// a page that asks again always gets the server's state, and a learning text never lands in the browser's cache. Files
// of the shell stay cacheable. A page isn't kept either: its import map carries the nonce of its own policy (#352), and
// a cached copy under a newer header would lose its script.
public static class NoStore
{
    // Set when the response starts, so it also wins over what a service behind the gateway sent.
    public static IApplicationBuilder UseNoStore(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            var data = context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/session");
            context.Response.OnStarting(state =>
            {
                var response = (HttpResponse)state;
                if (data || response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var headers = response.Headers;
                    headers.CacheControl = "no-store";
                    headers.Remove("Expires");
                    headers.Remove("Pragma");
                }

                return Task.CompletedTask;
            }, context.Response);

            return next(context);
        });
}