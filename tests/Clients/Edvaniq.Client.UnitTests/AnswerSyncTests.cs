using System.Security.Claims;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Shell;

using Microsoft.AspNetCore.Components.Authorization;

namespace Edvaniq.Client.UnitTests;

// #222: waiting until LearningEngine names the answer as the topic's newest, or giving up after the limit.
public sealed class AnswerSyncTests
{
    private static readonly AnswerSyncTiming Fast = new(TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(200));

    private static readonly Guid Topic = Guid.Parse("5f0c4a3e-1b2d-4c5e-8f9a-0b1c2d3e4f50");
    private static readonly Guid Other = Guid.Parse("7b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e");
    private static readonly Guid Answer = Guid.Parse("8c9d0e1f-2a3b-4c4d-9e5f-6a7b8c9d0e1f");
    private static readonly Guid Earlier = Guid.Parse("0b6f7a1e-6a5d-4a8e-9a11-2b9f1c3d4e01");

    [Fact]
    public async Task WaitsUntilTheAnswerIsTheNewest()
    {
        var progress = new FakeLearningProgress()
            .Answer(new TopicProgress(Topic, 50, TopicState.New, Earlier))
            .Answer(CallOutcome<IReadOnlyList<TopicProgress>>.Failed())
            .Answer(new TopicProgress(Topic, 62, TopicState.Building, Answer));
        var sync = new AnswerSync(progress, timing: Fast);
        sync.Track(Topic, Answer);

        var result = await sync.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Arrived);
        Assert.Equal(new TopicProgress(Topic, 62, TopicState.Building, Answer), Assert.Single(result.Topics!));
        Assert.Equal(3, progress.Calls);
        Assert.False(sync.HasPending);
    }

    // The answer may leave the mastery as it was. Only the answer id says it has arrived, not a changed value.
    [Fact]
    public async Task AnUnchangedMastery_IsNoSign_EitherWay()
    {
        var progress = new FakeLearningProgress()
            .Answer(new TopicProgress(Topic, 62, TopicState.Building, Earlier))
            .Answer(new TopicProgress(Topic, 62, TopicState.Building, Answer));
        var sync = new AnswerSync(progress, timing: Fast);
        sync.Track(Topic, Answer);

        var result = await sync.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Arrived);
        Assert.Equal(2, progress.Calls);
    }

    // AC3: after the limit it gives up, and the answer stays pending for another try.
    [Fact]
    public async Task GivesUpAfterTheLimit_AndKeepsTheAnswer()
    {
        var progress = new FakeLearningProgress().Answer(new TopicProgress(Topic, 50, TopicState.New, Earlier));
        var sync = new AnswerSync(progress, timing: Fast);
        sync.Track(Topic, Answer);

        var result = await sync.WaitAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Arrived);
        Assert.True(sync.HasPending);
        Assert.True(progress.Calls > 1);
    }

    // Several topics: done only when each has its answer.
    [Fact]
    public async Task WaitsForEveryTopic()
    {
        var otherAnswer = Guid.NewGuid();
        var progress = new FakeLearningProgress()
            .Answer(new TopicProgress(Topic, 62, TopicState.Building, Answer), new TopicProgress(Other, 50, TopicState.New, null))
            .Answer(new TopicProgress(Topic, 62, TopicState.Building, Answer), new TopicProgress(Other, 37, TopicState.Weakness, otherAnswer));
        var sync = new AnswerSync(progress, timing: Fast);
        sync.Track(Topic, Answer);
        sync.Track(Other, otherAnswer);

        var result = await sync.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Arrived);
        Assert.Equal(2, progress.Calls);
    }

    [Fact]
    public async Task SignedOut_StopsAtOnce()
    {
        var progress = new FakeLearningProgress().Answer(CallOutcome<IReadOnlyList<TopicProgress>>.SignedOut);
        var sync = new AnswerSync(progress, timing: Fast);
        sync.Track(Topic, Answer);

        var result = await sync.WaitAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Arrived);
        Assert.Equal(1, progress.Calls);
    }

    // Another login or a logout: the answers of the account before are none of the next one's business.
    [Fact]
    public void AChangedLogin_ForgetsPendingAnswers()
    {
        var authentication = new TestAuthentication();
        using var sync = new AnswerSync(new FakeLearningProgress(), authentication, Fast);
        sync.Track(Topic, Answer);

        authentication.Change();

        Assert.False(sync.HasPending);
    }

    private sealed class TestAuthentication : AuthenticationStateProvider
    {
        private static readonly Task<AuthenticationState> Nobody =
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Nobody;

        public void Change() => NotifyAuthenticationStateChanged(Nobody);
    }
}