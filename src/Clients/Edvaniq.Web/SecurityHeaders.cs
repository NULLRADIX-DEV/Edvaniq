using Yarp.ReverseProxy.Model;

namespace Edvaniq.Web;

// Every response of the web host carries these headers, errors and redirects too. There is no script-src yet: Blazor
// WebAssembly needs 'wasm-unsafe-eval' and the import map is inline, so the policy will grow with a nonce.
public static class SecurityHeaders
{
    public const string ShellPolicy = "frame-ancestors 'none'; base-uri 'self'; object-src 'none'; form-action 'self'";

    // A route may name a policy of its own, such as the one for a user's file (#350). It only counts if it is stricter
    // than the shell's, otherwise the shell's stays.
    public const string MetadataKey = "ContentSecurityPolicy";

    private static readonly Dictionary<string, string> Headers = new()
    {
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
    // What a service sent is overwritten.
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(static state =>
            {
                var context = (HttpContext)state;
                var headers = context.Response.Headers;
                foreach (var (name, value) in Headers)
                {
                    headers[name] = value;
                }

                headers.ContentSecurityPolicy = PolicyFor(context);
                return Task.CompletedTask;
            }, context);

            return next(context);
        });

    // Stricter means every directive of the shell is there with the same value or 'none'. More directives only forbid
    // more.
    public static bool IsStricter(string policy)
    {
        var own = Directives(policy);
        return Directives(ShellPolicy).All(shell =>
            own.TryGetValue(shell.Key, out var value) && (value == shell.Value || value == "'none'"));
    }

    private static string PolicyFor(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<RouteModel>()?.Config.Metadata?.GetValueOrDefault(MetadataKey) is { } policy
        && IsStricter(policy)
            ? policy
            : ShellPolicy;

    private static Dictionary<string, string> Directives(string policy) =>
        policy.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directive => directive.Split(' ', 2, StringSplitOptions.TrimEntries))
            .GroupBy(parts => parts[0].ToLowerInvariant())
            .ToDictionary(group => group.Key, group => group.First().ElementAtOrDefault(1) ?? "");
}