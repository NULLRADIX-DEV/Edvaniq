using Edvaniq.BuildingBlocks.AI;

using Microsoft.Extensions.AI;

namespace Edvaniq.Services.Content.Infrastructure.Ai;

// The simulated AI next to the real one, for documents with UsesTestDouble. Never fails on purpose.
public sealed class TestDoubleAi(IEnumerable<ISimulatedAnswer> answers)
{
    public IChatClient Client { get; } = new SimulatedChatClient(answers);
}