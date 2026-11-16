using System.Text.Json;
using System.Text.Json.Nodes;

namespace Edvaniq.ArchitectureTests;

// #419: every route the web host forwards has to land on a route of the gateway. Both live in their own appsettings.json
// and the tests of each process run against a stub of the other, so a web route the gateway doesn't know would only
// show up on the server, as a 404 of the gateway.
public class GatewayRouteTests
{
    // The gateway answers it itself, without a route.
    private static readonly string[] OwnEndpoints = ["/status"];

    [Fact]
    public void EveryWebRoute_HasAGatewayRoute()
    {
        var gateway = Routes("src/Gateway/Edvaniq.Gateway/appsettings.json");
        var web = Routes("src/Clients/Edvaniq.Web/appsettings.json");

        Assert.True(web.Count >= 20, "Expected the routes of the web host.");
        Assert.All(web, route =>
        {
            var path = Forwarded(route);
            Assert.True(
                OwnEndpoints.Contains(path) || gateway.Any(target => Matches(target, path, route.Methods)),
                $"The web route {route.Name} forwards {string.Join(',', route.Methods ?? ["*"])} {path}, but no route of the gateway takes it.");
        });
    }

    private sealed record Route(string Name, string Path, string[]? Methods, JsonArray? Transforms);

    // The path the web host sends to the gateway.
    private static string Forwarded(Route route)
    {
        var path = route.Path;
        foreach (var transform in route.Transforms ?? [])
        {
            if (transform?["PathSet"]?.GetValue<string>() is { } set)
            {
                path = set;
            }
            else if (transform?["PathRemovePrefix"]?.GetValue<string>() is { } prefix && path.StartsWith(prefix, StringComparison.Ordinal))
            {
                path = path[prefix.Length..];
            }
        }

        return path;
    }

    private static bool Matches(Route target, string path, string[]? methods)
    {
        var pathMatches = target.Path.EndsWith("/{**rest}", StringComparison.Ordinal)
            ? path.StartsWith(target.Path[..^"{**rest}".Length], StringComparison.Ordinal)
            : path == target.Path;
        var methodsMatch = target.Methods is null || (methods is not null && methods.All(target.Methods.Contains));
        return pathMatches && methodsMatch;
    }

    private static List<Route> Routes(string file)
    {
        var settings = JsonNode.Parse(
            File.ReadAllText(Path.Combine(Repository.Root, file)),
            documentOptions: new() { CommentHandling = JsonCommentHandling.Skip })!;
        return settings["ReverseProxy"]!["Routes"]!.AsObject()
            .Select(route => new Route(
                route.Key,
                route.Value!["Match"]!["Path"]!.GetValue<string>(),
                route.Value["Match"]!["Methods"]?.AsArray().Select(method => method!.GetValue<string>()).ToArray(),
                route.Value["Transforms"]?.AsArray()))
            .ToList();
    }
}