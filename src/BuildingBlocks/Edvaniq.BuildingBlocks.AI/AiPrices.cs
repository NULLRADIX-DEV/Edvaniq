using System.Globalization;

using Microsoft.Extensions.Configuration;

namespace Edvaniq.BuildingBlocks.AI;

// Prices per million tokens by model, the fallback when the provider doesn't say what an answer cost. They come from
// the environment of the server as a list (Ai:Prices:0:Model, :Input, :Output), a model name may contain a colon.
internal sealed class AiPrices
{
    public const string Section = "Ai:Prices";

    private readonly Dictionary<string, (decimal Input, decimal Output)> prices;

    private AiPrices(Dictionary<string, (decimal Input, decimal Output)> prices) => this.prices = prices;

    public static AiPrices Read(IConfiguration configuration)
    {
        var prices = new Dictionary<string, (decimal, decimal)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in configuration.GetSection(Section).GetChildren())
        {
            var model = entry["Model"]?.Trim();
            if (string.IsNullOrEmpty(model))
            {
                throw new InvalidOperationException($"{Section}:{entry.Key}:Model is missing.");
            }

            if (!prices.TryAdd(model, (Price(entry, "Input"), Price(entry, "Output"))))
            {
                throw new InvalidOperationException($"{Section}:{entry.Key} names a model a second time.");
            }
        }

        return new AiPrices(prices);
    }

    // Null when the model has no price.
    public decimal? CostOf(string? model, long inputTokens, long outputTokens) =>
        model is not null && prices.TryGetValue(model, out var price)
            ? ((inputTokens * price.Input) + (outputTokens * price.Output)) / 1_000_000m
            : null;

    private static decimal Price(IConfigurationSection entry, string key) =>
        decimal.TryParse(entry[key], NumberStyles.Number, CultureInfo.InvariantCulture, out var price) && price >= 0
            ? price
            : throw new InvalidOperationException($"{Section}:{entry.Key}:{key} must be a price of at least 0, like 0.27.");
}