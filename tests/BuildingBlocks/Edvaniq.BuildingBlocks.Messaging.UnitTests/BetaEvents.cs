using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.Beta.Contracts.Sub;

public sealed record Nested(Guid Id) : IIntegrationEvent;