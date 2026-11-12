using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Edvaniq.BuildingBlocks.Web;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Edvaniq.Testing;

// "User A sees nothing of user B", for every endpoint of a service. The service lists its endpoints once: Item for a
// route that names one resource, List for one that returns several, Exempt with a reason for one without user data.
// AssertCovers compares that list with the endpoints the running service really has, so a new endpoint fails the build
// until it is listed. RunAsync seeds data for A and shows that B can't read, change or delete it, and that B gets the
// very same answer for A's resource as for one that does not exist.
public sealed partial class IsolationCheck
{
    private readonly List<Entry> entries = [];

    // A route with one parameter, the id of A's resource. The body has to be valid, otherwise a 400 from the validation
    // would hide whether the owner check ran.
    public IsolationCheck Item(string method, string route, object? body = null) =>
        Add(new Entry(Kind.Item, method, route, body, null));

    // A route without parameters whose answer must not mention A's data when B asks.
    public IsolationCheck List(string method, string route) =>
        Add(new Entry(Kind.List, method, route, null, null));

    // A route the check leaves out, such as one that only creates the caller's own data. The reason shows in the test
    // output, so a review sees every exemption.
    public IsolationCheck Exempt(string method, string route, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("An exemption needs a reason.", nameof(reason));
        }

        return Add(new Entry(Kind.Exempt, method, route, null, reason));
    }

    // Every endpoint a user's token reaches is listed, and nothing listed is gone. Endpoints open to anyone (health) and
    // those only for another service's internal token have no user data of a caller and are left out.
    public void AssertCovers(IServiceProvider services)
    {
        var found = services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null && !IsInternal(endpoint))
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(method => Key(method, endpoint.RoutePattern.RawText ?? "")))
            .ToHashSet(StringComparer.Ordinal);
        var listed = entries.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);

        var output = TestContext.Current.TestOutputHelper;
        foreach (var exempt in entries.Where(entry => entry.Kind == Kind.Exempt))
        {
            output?.WriteLine($"Exempt: {exempt.Key} ({exempt.Reason})");
        }

        var missing = found.Except(listed).Order(StringComparer.Ordinal).ToList();
        var gone = listed.Except(found).Order(StringComparer.Ordinal).ToList();
        if (missing.Count > 0 || gone.Count > 0)
        {
            throw new IsolationCheckException(
                $"The isolation check doesn't match the endpoints. Not listed: {Names(missing)}. Listed but not there: {Names(gone)}.");
        }
    }

    // seed creates A's data and says how A reads it back. Each run has two new users, so the database can be shared.
    public async Task RunAsync<TEntryPoint>(WebApplicationFactory<TEntryPoint> factory, Func<string, Task<OwnedResource>> seed)
        where TEntryPoint : class
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var aliceId = $"alice-{Guid.NewGuid():N}";
        using var alice = ClientFor(factory, aliceId);
        using var bob = ClientFor(factory, $"bob-{Guid.NewGuid():N}");

        var owned = await seed(aliceId);
        var before = await ReadAsync(alice, owned, cancellationToken);

        foreach (var list in entries.Where(entry => entry.Kind == Kind.List))
        {
            await CheckListAsync(list, alice, bob, owned, cancellationToken);
        }

        // Deleting last, so the other verbs still find A's resource.
        foreach (var item in entries.Where(entry => entry.Kind == Kind.Item).OrderBy(entry => entry.Method == "DELETE"))
        {
            await CheckItemAsync(item, bob, owned, cancellationToken);
        }

        var after = await ReadAsync(alice, owned, cancellationToken);
        if (after != before)
        {
            throw new IsolationCheckException($"User B changed A's resource {owned.ReadPath}. Before: {before} After: {after}");
        }
    }

    private IsolationCheck Add(Entry entry)
    {
        if (entry.Kind == Kind.Item && RouteParameter().Matches(entry.Route).Count != 1)
        {
            throw new ArgumentException($"{entry.Key} needs exactly one route parameter, the id of the resource.");
        }

        entries.Add(entry);
        return this;
    }

    private static async Task CheckListAsync(Entry list, HttpClient alice, HttpClient bob, OwnedResource owned, CancellationToken cancellationToken)
    {
        // Without A's data in A's own answer the check would pass for nothing.
        var mine = await SendAsync(alice, list.Method, list.Route, body: null, cancellationToken);
        if (!mine.Status.IsSuccess() || !Mentions(mine.Body, owned))
        {
            throw new InvalidOperationException($"{list.Key} doesn't show A's resource to A ({(int)mine.Status}), the seed doesn't fit.");
        }

        var theirs = await SendAsync(bob, list.Method, list.Route, body: null, cancellationToken);
        if (Mentions(theirs.Body, owned))
        {
            throw new IsolationCheckException($"{list.Key} shows A's resource to B: {theirs.Body}");
        }
    }

    private static async Task CheckItemAsync(Entry item, HttpClient bob, OwnedResource owned, CancellationToken cancellationToken)
    {
        var foreign = await SendAsync(bob, item.Method, item.PathFor(owned.Id), item.Body, cancellationToken);
        var missing = await SendAsync(bob, item.Method, item.PathFor(Guid.NewGuid()), item.Body, cancellationToken);

        if (missing.Status == HttpStatusCode.BadRequest)
        {
            throw new InvalidOperationException($"{item.Key} turns the body away (400), the check can't see the owner check behind it.");
        }

        if (foreign.Status.IsSuccess())
        {
            throw new IsolationCheckException($"{item.Key} lets B at A's resource ({(int)foreign.Status}): {foreign.Body}");
        }

        if (foreign.Status != missing.Status || foreign.Comparable != missing.Comparable)
        {
            throw new IsolationCheckException(
                $"{item.Key} answers B differently for A's resource than for a missing one. "
                + $"A's: {(int)foreign.Status} {foreign.Comparable} Missing: {(int)missing.Status} {missing.Comparable}");
        }
    }

    private static async Task<string> ReadAsync(HttpClient alice, OwnedResource owned, CancellationToken cancellationToken)
    {
        var read = await SendAsync(alice, "GET", owned.ReadPath, body: null, cancellationToken);
        if (!read.Status.IsSuccess())
        {
            throw new InvalidOperationException($"A can't read A's resource at {owned.ReadPath} ({(int)read.Status}), the seed doesn't fit.");
        }

        return read.Body;
    }

    private static bool Mentions(string body, OwnedResource owned) =>
        body.Contains(owned.Id.ToString(), StringComparison.OrdinalIgnoreCase)
        || body.Contains(owned.Marker, StringComparison.Ordinal);

    private static async Task<Answer> SendAsync(HttpClient client, string method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return new Answer(response.StatusCode, text, Comparable(response.Content.Headers.ContentType?.MediaType, text));
    }

    // The answer without what differs on every request anyway: the trace.
    private static string Comparable(string? mediaType, string body)
    {
        if (mediaType != "application/problem+json" || JsonNode.Parse(body) is not JsonObject problem)
        {
            return $"{mediaType} {body}";
        }

        problem.Remove("traceId");
        return $"{mediaType} {problem.ToJsonString()}";
    }

    private static HttpClient ClientFor<TEntryPoint>(WebApplicationFactory<TEntryPoint> factory, string userId)
        where TEntryPoint : class
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    private static bool IsInternal(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>()
            .Any(policy => policy.AuthenticationSchemes.Contains(InternalTokenExtensions.Scheme));

    // "/topics/" and "/topics" are the same route; the method in capitals.
    private static string Key(string method, string route)
    {
        var trimmed = route.Length > 1 ? route.TrimEnd('/') : route;
        return $"{method.ToUpperInvariant()} {(trimmed.StartsWith('/') ? trimmed : "/" + trimmed)}";
    }

    private static string Names(IReadOnlyList<string> keys) => keys.Count == 0 ? "none" : string.Join(", ", keys);

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();

    private enum Kind
    {
        Item,
        List,
        Exempt,
    }

    private sealed record Entry(Kind Kind, string Method, string Route, object? Body, string? Reason)
    {
        public string Key => IsolationCheck.Key(Method, Route);

        public string PathFor(Guid id) => RouteParameter().Replace(Route, id.ToString(), 1);
    }

    private sealed record Answer(HttpStatusCode Status, string Body, string Comparable);
}

// A's resource: its id, the path A reads it at, and a value of it that only A's data has (a name, a text), which no
// answer to B may contain.
public sealed record OwnedResource(Guid Id, string ReadPath, string Marker);

public sealed class IsolationCheckException(string message) : Exception(message);

internal static class HttpStatusExtensions
{
    public static bool IsSuccess(this HttpStatusCode status) => (int)status is >= 200 and < 300;
}