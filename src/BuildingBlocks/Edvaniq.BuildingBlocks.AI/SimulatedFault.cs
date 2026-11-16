namespace Edvaniq.BuildingBlocks.AI;

// How the simulated provider fails on purpose, so the error paths of a service can be tested without a real one.
public enum SimulatedFault
{
    None,

    // Every request fails like a provider that answers 503.
    Outage,

    // Every request waits until the caller gives up. A caller without a timeout waits forever, as with a real provider.
    Timeout,

    // Every answer is cut in half, so it is no valid JSON.
    InvalidOutput,

    // An answer that lists items gives each of them twice, valid but repeated. The answer decides what an item is
    // (SimulatedPrompt.Fault), one without a list ignores it.
    DuplicateItems,
}
