using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.Identity.Contracts;

// Every service with user data removes it, the template brings the handler. UserId is the token's sub, i.e. the OwnerId.
public sealed record UserDeleted(string UserId) : IIntegrationEvent;