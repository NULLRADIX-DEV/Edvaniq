using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Edvaniq.Services.Content.Application;

namespace Edvaniq.Services.Content.IntegrationTests;

// Answers in place of Planning, behind the real client with its token handler and resilience. Every user gets one goal
// of their own, as Planning gives the default goal. The user comes from the sub of the token that arrives, so a request
// without the caller's token finds no goal. The tests share it, each with its own users.
public sealed class PlanningStub : HttpMessageHandler, IGoalProfiles
{
    private readonly ConcurrentDictionary<string, Guid> goals = new();
    private readonly ConcurrentDictionary<string, HttpStatusCode> down = new();
    private readonly ConcurrentDictionary<string, bool> hanging = new();
    private readonly ConcurrentDictionary<Guid, bool> forgotten = new();
    private readonly ConcurrentDictionary<string, (string? Level, string? OutputLanguage)> profiles = new();
    private readonly ConcurrentDictionary<string, string> names = new();

    // Every request that came in, as it arrived.
    public ConcurrentQueue<Received> Requests { get; } = new();

    public Guid GoalOf(string userId) => goals.GetOrAdd(userId, _ => Guid.NewGuid());

    // The user's goal has this level and output language. Without it both are null, as on a default goal nobody took over.
    public void Profile(string userId, string? level, string? outputLanguage) => profiles[userId] = (level, outputLanguage);

    // Planning names the goal in its answers too, which Content has no use for (#312).
    public void Name(string userId, string name) => names[userId] = name;

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

        var (level, outputLanguage) = profiles.GetValueOrDefault(user);
        if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/learning-goals/default")
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = GoalOf(user), name = names.GetValueOrDefault(user), level, outputLanguage }) };
        }

        // Only the user's own goal is found, someone else's looks like none.
        return request.Method == HttpMethod.Get
            && request.RequestUri.AbsolutePath.Split('/') is ["", "learning-goals", var id, "profile"]
            && Guid.TryParse(id, out var goal)
            && goals.TryGetValue(user, out var own)
            && own == goal
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = goal, name = names.GetValueOrDefault(user), level, outputLanguage }) }
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    // The worker's question for a goal's profile (IGoalProfiles), answered from the same goals and profiles, whoever owns
    // the goal. A goal of a user marked down can't be asked, a forgotten goal is gone.
    public Task<LearnerProfile?> OfAsync(Guid learningGoalId, CancellationToken cancellationToken)
    {
        WorkerAsked.Enqueue(learningGoalId);
        var owner = goals.FirstOrDefault(goal => goal.Value == learningGoalId).Key;
        if (owner is not null && down.ContainsKey(owner))
        {
            throw new LearningGoalUnavailableException(new HttpRequestException("Planning is down."));
        }

        if (owner is null || forgotten.ContainsKey(learningGoalId))
        {
            return Task.FromResult<LearnerProfile?>(null);
        }

        var (level, outputLanguage) = profiles.GetValueOrDefault(owner);
        return Task.FromResult<LearnerProfile?>(LearnerProfile.Of(level, outputLanguage));
    }

    // Every goal the worker asked for, in order.
    public ConcurrentQueue<Guid> WorkerAsked { get; } = new();

    // Planning doesn't know the goal any more.
    public void Forget(Guid learningGoalId) => forgotten[learningGoalId] = true;

    // Planning is back for the user.
    public void Up(string userId) => down.TryRemove(userId, out _);

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