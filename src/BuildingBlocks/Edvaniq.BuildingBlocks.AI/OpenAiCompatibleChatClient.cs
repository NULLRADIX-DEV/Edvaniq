using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.AI;

namespace Edvaniq.BuildingBlocks.AI;

// A provider with an OpenAI-compatible chat completions API. The key only goes into the Authorization header of the
// HttpClient, and no exception carries a header or the body of an answer, which may echo the key or the prompt.
internal sealed class OpenAiCompatibleChatClient(IHttpClientFactory httpClients, OpenAiCompatibleSettings settings, AiPrices prices) : IChatClient
{
    public const string Provider = "OpenAiCompatible";

    public const string HttpClientName = "ai";

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var http = httpClients.CreateClient(HttpClientName);
        using var response = await http.PostAsJsonAsync("chat/completions", Request(messages, options), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The AI provider answered {(int)response.StatusCode}.", null, response.StatusCode);
        }

        JsonDocument answer;
        try
        {
            answer = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The AI provider sent no valid JSON.");
        }

        using (answer)
        {
            var read = Read(answer.RootElement);
            AiCost.Set(read, CostOf(answer.RootElement, read));
            return read;
        }
    }

    // The whole answer as one update, nobody streams yet.
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text) { ModelId = response.ModelId };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null ? null
        : serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata(Provider, defaultModelId: settings.Model)
        : serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose()
    {
    }

    private JsonObject Request(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var request = (JsonObject)settings.ExtraBody.DeepClone();
        request["model"] = options?.ModelId ?? settings.Model;
        request["messages"] = new JsonArray([.. messages.Select(message => (JsonNode)new JsonObject
        {
            ["role"] = message.Role.Value,
            ["content"] = message.Text,
        })]);
        if (options?.Temperature is { } temperature)
        {
            request["temperature"] = temperature;
        }

        if (options?.MaxOutputTokens is { } maxTokens)
        {
            request["max_tokens"] = maxTokens;
        }

        if (settings.SchemaOutput && ResponseFormat(options?.ResponseFormat) is { } format)
        {
            request["response_format"] = format;
        }

        return request;
    }

    // The form the answer should keep to. The caller still checks it, the schema only makes a bad answer rarer.
    private static JsonObject? ResponseFormat(ChatResponseFormat? format) => format switch
    {
        ChatResponseFormatJson { Schema: { } schema } json => new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = json.SchemaName ?? "answer",
                ["strict"] = true,
                ["schema"] = JsonNode.Parse(schema.GetRawText()),
            },
        },
        ChatResponseFormatJson => new JsonObject { ["type"] = "json_object" },
        _ => null,
    };

    private static ChatResponse Read(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object
            || !answer.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0
            || !choices[0].TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("The AI provider sent no answer.");
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, content.GetString()))
        {
            ResponseId = Text(answer, "id"),
            ModelId = Text(answer, "model"),
            FinishReason = Text(choices[0], "finish_reason") is { } reason ? new ChatFinishReason(reason) : null,
            Usage = answer.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
                ? new UsageDetails
                {
                    InputTokenCount = Count(usage, "prompt_tokens"),
                    OutputTokenCount = Count(usage, "completion_tokens"),
                    TotalTokenCount = Count(usage, "total_tokens"),
                }
                : null,
        };
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // What the provider billed, when it says so (it may have to be asked for it, that goes into ExtraBody), otherwise
    // from the price of the model that answered, or else the configured one.
    private decimal? CostOf(JsonElement answer, ChatResponse response) =>
        answer.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
        && usage.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Number && cost.TryGetDecimal(out var billed)
            ? billed
            : prices.CostOf(response.ModelId, response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0)
                ?? prices.CostOf(settings.Model, response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0);

    private static long? Count(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var count) ? count : null;
}