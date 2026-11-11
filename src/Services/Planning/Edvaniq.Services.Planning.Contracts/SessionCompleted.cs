using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.Planning.Contracts;

// A session ended (docs/spec/events.md), finished or broken off: Completed tells which. IntroducedTopicIds names the
// topics whose unit "Neues Thema" was finished in it, so LearningEngine counts them as introduced. EndedAt is UTC.
// Sent by Planning from #395 on.
public sealed record SessionCompleted(
    string UserId,
    Guid SessionId,
    DateTime EndedAt,
    bool Completed,
    IReadOnlyList<Guid> IntroducedTopicIds) : IIntegrationEvent;