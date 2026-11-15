using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.Planning.Contracts;

// A session ended (docs/spec/events.md), finished or broken off: Completed tells which. IntroducedTopicIds names the
// topics whose unit "Neues Thema" was finished in it, so LearningEngine counts them as introduced. EndedAt is UTC.
// UnitStartedAt (#1040) is when the unit started: the first session on the same block, broken off ones included, set
// on breaking off too. UTC, null in events sent before. Sent by Planning from #395 on.
public sealed record SessionCompleted(
    string UserId,
    Guid SessionId,
    DateTime EndedAt,
    bool Completed,
    IReadOnlyList<Guid> IntroducedTopicIds,
    DateTime? UnitStartedAt = null) : IUserEvent;