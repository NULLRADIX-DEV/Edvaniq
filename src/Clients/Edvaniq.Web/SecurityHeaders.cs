using System.Security.Cryptography;

using Yarp.ReverseProxy.Model;

namespace Edvaniq.Web;

// Every response of the web host carries these headers, errors and redirects too. Scripts come only from the host
// itself (#352): WebAssembly needs 'wasm-unsafe-eval', and the one inline script, the import map, carries a nonce that
// is new for every response. Styles come only from files, there is no inline style in the shell. Nothing is framed,
// not even a page of our own.
public static class SecurityHeaders
{
    public const string NonceToken = "{nonce}";

    public const string ShellPolicy =
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval' 'nonce-{nonce}'; style-src 'self'; img-src 'self'; " +
        "font-src 'self'; connect-src 'self'; frame-src 'none'; frame-ancestors 'none'; base-uri 'self'; object-src 'none'; form-action 'self'";

    // dotnet watch reloads the page over a web socket on a port of its own. Only in Development.
    private const string DevelopmentConnect = "connect-src 'self' ws://localhost:* wss://localhost:*";

    // A route may name a policy of its own, such as the one for a user's file (#350). It only counts if it is stricter
    // than the shell's, otherwise the shell's stays.
    public const string MetadataKey = "ContentSecurityPolicy";

    // Directives that fall back to default-src when a policy leaves them out.
    private static readonly HashSet<string> FetchDirectives =
        ["script-src", "style-src", "img-src", "font-src", "connect-src", "frame-src", "object-src", "media-src", "manifest-src"];

    private static readonly object NonceKey = new();

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
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, bool development = false) =>
        app.Use((context, next) =>
        {
            context.Items[NonceKey] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            context.Response.OnStarting(state =>
            {
                var context = (HttpContext)state;
                var headers = context.Response.Headers;
                foreach (var (name, value) in Headers)
                {
                    headers[name] = value;
                }

                headers.ContentSecurityPolicy = PolicyFor(context, development);
                return Task.CompletedTask;
            }, context);

            return next(context);
        });

    // The nonce of this response, for the inline script of the page.
    public static string NonceOf(HttpContext context) => (string)context.Items[NonceKey]!;

    // Stricter means every directive of the shell is there with the same value or 'none'. A fetch directive left out
    // counts as the policy's default-src, as in the browser. More directives only forbid more.
    public static bool IsStricter(string policy)
    {
        var own = Directives(policy);
        return Directives(ShellPolicy).All(shell =>
            ValueOf(own, shell.Key) is { } value && (value == shell.Value || value == "'none'"));
    }

    private static string? ValueOf(Dictionary<string, string> policy, string directive) =>
        policy.TryGetValue(directive, out var value) ? value
        : FetchDirectives.Contains(directive) ? policy.GetValueOrDefault("default-src")
        : null;

    private static string PolicyFor(HttpContext context, bool development)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<RouteModel>()?.Config.Metadata?.GetValueOrDefault(MetadataKey) is { } policy
            && IsStricter(policy))
        {
            return policy;
        }

        var shell = ShellPolicy.Replace(NonceToken, NonceOf(context), StringComparison.Ordinal);
        return development ? shell.Replace("connect-src 'self'", DevelopmentConnect, StringComparison.Ordinal) : shell;
    }

    private static Dictionary<string, string> Directives(string policy) =>
        policy.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directive => directive.Split(' ', 2, StringSplitOptions.TrimEntries))
            .GroupBy(parts => parts[0].ToLowerInvariant())
            .ToDictionary(group => group.Key, group => group.First().ElementAtOrDefault(1) ?? "");
}