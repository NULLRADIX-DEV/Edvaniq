using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.Identity.Contracts;

// A user is deleted. Every service with data of users removes theirs (docs/spec/object-model.md, Nutzer), the handler for
// it comes with the service template. UserId is the sub of the user's tokens, the OwnerId of the rows. Identity sends it
// once an account can be deleted (M2).
public sealed record UserDeleted(string UserId) : IIntegrationEvent;