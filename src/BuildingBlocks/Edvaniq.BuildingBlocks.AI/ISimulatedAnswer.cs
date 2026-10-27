namespace Edvaniq.BuildingBlocks.AI;

// One kind of answer of the simulated provider. The service that sends a prompt registers the answer to it, because only
// it knows what the prompt asks for. An answer depends on the prompt alone, randomness comes from prompt.Random().
public interface ISimulatedAnswer
{
    bool Answers(SimulatedPrompt prompt);

    string Answer(SimulatedPrompt prompt);
}
