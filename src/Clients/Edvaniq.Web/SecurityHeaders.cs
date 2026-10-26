namespace Edvaniq.Web;

// Every response of the web host carries these headers, errors and redirects too. There is no script-src yet: Blazor
// WebAssembly needs 'wasm-unsafe-eval' and the import map is inline, so the policy will grow with a nonce.
public static class SecurityHeaders
{
    private static readonly Dictionary<string, string> Headers = new()
    {
        ["Content-Security-Policy"] = "frame-ancestors 'none'; base-uri 'self'; object-src 'none'; form-action 'self'",
        ["X-Frame-Options"] = "DENY",
        ["X-Content-Type-Options"] = "nosniff",
        ["Referrer-Policy"] = "strict-origin-when-cross-origin",
        ["Cross-Origin-Opener-Policy"] = "same-origin",
        ["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()",

        // Until the start with real users, search engines keep the shell out of their index. A robots.txt that forbids
        // crawling would do the opposite: the crawler never sees this header and may list a linked address anyway.
        ["X-Robots-Tag"] = "noindex, nofollow",
    };

    // Added when the response starts, so an answer that something resets on its way, like the error page, gets them too.
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(static state =>
            {
                var headers = ((HttpResponse)state).Headers;
                foreach (var (name, value) in Headers)
                {
                    headers[name] = value;
                }

                return Task.CompletedTask;
            }, context.Response);

            return next(context);
        });
}