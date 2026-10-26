using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.TestService.Contracts;

// Sent when a user adds a bookmark. Carries ids only, the link itself stays in the service.
public sealed record BookmarkAdded(Guid BookmarkId, string OwnerId) : IIntegrationEvent;