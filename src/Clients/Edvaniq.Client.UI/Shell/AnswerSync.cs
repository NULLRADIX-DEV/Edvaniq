using Edvaniq.Client.Core;

using Microsoft.AspNetCore.Components.Authorization;

namespace Edvaniq.Client.UI.Shell;

// The answers whose effect hasn't reached LearningEngine yet. An answer goes there as an event, so mastery and plan
// follow a moment later (Q4). Pages wait here until LearningEngine names the answer as the topic's newest. Only in the
// memory of the running app: a reload starts empty and simply shows the server's state. Another login or a logout
// empties it, so nobody waits for answers of another account. One per app, never registered in the web host.
public sealed class AnswerSync : IDisposable
{
    private readonly ILearningProgress progress;
    private readonly AuthenticationStateProvider? authentication;
    private readonly AnswerSyncTiming timing;
    private readonly TimeProvider time;
    private readonly Dictionary<Guid, Guid> pending = [];

    public AnswerSync(
        ILearningProgress progress,
        AuthenticationStateProvider? authentication = null,
        AnswerSyncTiming? timing = null,
        TimeProvider? time = null)
    {
        this.progress = progress;
        this.authentication = authentication;
        this.timing = timing ?? AnswerSyncTiming.Default;
        this.time = time ?? TimeProvider.System;
        if (authentication is not null)
        {
            authentication.AuthenticationStateChanged += OnAuthenticationChanged;
        }
    }

    public bool HasPending => pending.Count > 0;

    // The newest answer per topic is enough: once it has arrived, so have those before it.
    public void Track(Guid topicId, Guid answerId) => pending[topicId] = answerId;

    public void Clear() => pending.Clear();

    // Asks LearningEngine until every pending answer has arrived, or gives up after the limit. Answers that arrived are
    // done, the others stay for another try. Brings the newest state it got.
    public async Task<AnswerSyncResult> WaitAsync(CancellationToken cancellationToken = default)
    {
        var started = time.GetTimestamp();
        IReadOnlyList<TopicProgress>? latest = null;
        while (true)
        {
            var outcome = await progress.ListAsync(cancellationToken);
            if (outcome.Result == CallResult.SignedOut)
            {
                return new AnswerSyncResult(false, latest);
            }

            if (outcome.Result == CallResult.Ok)
            {
                latest = outcome.Value!;
                foreach (var topic in latest.Where(topic => pending.TryGetValue(topic.TopicId, out var answer) && topic.LastAnswerId == answer))
                {
                    pending.Remove(topic.TopicId);
                }

                if (pending.Count == 0)
                {
                    return new AnswerSyncResult(true, latest);
                }
            }

            if (time.GetElapsedTime(started) + timing.Interval > timing.Limit)
            {
                return new AnswerSyncResult(false, latest);
            }

            await Task.Delay(timing.Interval, time, cancellationToken);
        }
    }

    public void Dispose()
    {
        if (authentication is not null)
        {
            authentication.AuthenticationStateChanged -= OnAuthenticationChanged;
        }
    }

    private void OnAuthenticationChanged(Task<AuthenticationState> state) => Clear();
}

// Arrived: every pending answer is in. Topics: LearningEngine's newest state, null if no call got through.
public sealed record AnswerSyncResult(bool Arrived, IReadOnlyList<TopicProgress>? Topics);

// How often to ask and when to give up. The limit is twice the typical delay of Q4, so a slow moment isn't a failure yet.
public sealed record AnswerSyncTiming(TimeSpan Interval, TimeSpan Limit)
{
    public static readonly AnswerSyncTiming Default = new(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(10));
}