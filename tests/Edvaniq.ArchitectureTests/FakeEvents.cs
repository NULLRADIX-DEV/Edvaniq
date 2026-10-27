using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.Content.Contracts.Fakes;

// Events that look as if Content sent them, for EventCatalogTests and EventContractTests. They live in this test project
// only, so the checks over the real services never see them.
public sealed record DocumentShared(string UserId, IReadOnlyList<SharedPart> Parts, Guid? TopicId, Visibility Visibility)
    : IIntegrationEvent;

public sealed record SharedPart(Guid SectionId, string? Note, int[] Pages);

public enum Visibility
{
    Private,
    Team = 2,
}

// Identity's event, but sent by Content.
public sealed record UserDeleted(string UserId) : IIntegrationEvent;