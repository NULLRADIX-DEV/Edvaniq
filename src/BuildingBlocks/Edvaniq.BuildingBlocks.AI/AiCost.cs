using Microsoft.Extensions.AI;

namespace Edvaniq.BuildingBlocks.AI;

// What one answer cost, in the billing currency of the provider (US dollars). Every client puts it into the answer's
// AdditionalProperties, so callers never need to know where it came from.
public static class AiCost
{
    public const string Property = "edvaniq.cost";

    public static decimal? Of(ChatResponse response) =>
        response.AdditionalProperties?.TryGetValue(Property, out var value) == true && value is decimal cost ? cost : null;

    internal static void Set(ChatResponse response, decimal? cost)
    {
        if (cost is { } known)
        {
            (response.AdditionalProperties ??= [])[Property] = known;
        }
    }
}