namespace Edvaniq.Web;

// The server is the only source of the user's data: what comes from /api and /session is never kept by the browser, so
// a page that asks again always gets the server's state, and a learning text never lands in the browser's cache. Files
// of the shell stay cacheable.
public static class NoStore
{
    // Set when the response starts, so it also wins over what a service behind the gateway sent.
    public static IApplicationBuilder UseNoStore(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/session"))
            {
                context.Response.OnStarting(static state =>
                {
                    var headers = ((HttpResponse)state).Headers;
                    headers.CacheControl = "no-store";
                    headers.Remove("Expires");
                    headers.Remove("Pragma");
                    return Task.CompletedTask;
                }, context.Response);
            }

            return next(context);
        });
}