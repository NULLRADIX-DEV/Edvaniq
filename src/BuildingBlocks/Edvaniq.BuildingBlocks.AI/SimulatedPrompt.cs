using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.AI;

namespace Edvaniq.BuildingBlocks.AI;

// The messages of a request as the simulated provider sees them, with a seed that only depends on them.
public sealed class SimulatedPrompt
{
    public SimulatedPrompt(IEnumerable<ChatMessage> messages)
    {
        Messages = [.. messages];
        Seed = SeedOf(Messages);
    }

    public IReadOnlyList<ChatMessage> Messages { get; }

    public string System => TextOf(ChatRole.System);

    public string User => TextOf(ChatRole.User);

    // Same messages, same seed, in every process and on every machine.
    public int Seed { get; }

    // Randomness for an answer that still comes out the same for the same prompt.
    public Random Random() => new(Seed);

    // Not string.GetHashCode, that one changes with every process. The '\0' keeps ["ab", "c"] and ["a", "bc"] apart.
    internal static int SeedOf(IReadOnlyList<ChatMessage> messages)
    {
        var text = string.Concat(messages.Select(message => $"{message.Role.Value}\n{message.Text}\0"));
        return BinaryPrimitives.ReadInt32LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private string TextOf(ChatRole role) =>
        string.Join('\n', Messages.Where(message => message.Role == role).Select(message => message.Text));
}
