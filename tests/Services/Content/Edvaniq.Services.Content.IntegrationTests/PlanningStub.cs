using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Edvaniq.Services.Content.IntegrationTests;

// Answers in place of Planning, behind the real client with its token handler and resilience. Every user gets one goal
// of their own, as Planning gives the default goal. The user comes from the sub of the token that arrives, so a request
// without the caller's token finds no goal. The tests share it, each with its own users.
public sealed class PlanningStub : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Guid> goals = new();
    private readonly ConcurrentDictionary<string, HttpStatusCode> down = new();
    private readonly ConcurrentDictionary<string, bool> hanging = new();

    // Every request that came in, as it arrived.
    public ConcurrentQueue<Received> Requests { get; } = new();

    public Guid GoalOf(string userId) => goals.GetOrAdd(userId, _ => Guid.NewGuid());

    // Planning answers the user with this status.
    public void Down(string userId, HttpStatusCode status = HttpStatusCode.ServiceUnavailable) => down[userId] = status;

    // Planning never answers the user.
    public void Hang(string userId) => hanging[userId] = true;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var user = UserOf(request.Headers.Authorization?.ToString());
        Requests.Enqueue(new(
            request.Method,
            request.RequestUri!.AbsolutePath,
            user,
            request.Headers.Authorization?.ToString(),
            [.. request.Headers.Select(header => header.Key)]));

        if (user is null)
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        if (hanging.ContainsKey(user))
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (down.TryGetValue(user, out var status))
        {
            return new HttpResponseMessage(status);
        }

        return request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/learning-goals/default"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = GoalOf(user) }) }
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    // The sub of a bearer token, unchecked: the service checked it before it asked.
    private static string? UserOf(string? authorization)
    {
        if (authorization?.Split(' ') is not ["Bearer", var token] || token.Split('.') is not [_, var payload, _])
        {
            return null;
        }

        var base64 = payload.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
        return json.RootElement.TryGetProperty("sub", out var sub) ? sub.GetString() : null;
    }

    public sealed record Received(HttpMethod Method, string Path, string? User, string? Authorization, IReadOnlyList<string> Headers);
}