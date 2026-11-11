using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Edvaniq.Client.Core;

// Talks to Content and Knowledge through the web host, which wants a login for both. The HttpClient carries the base
// address of the host and has to keep the session cookie, as the browser does. Its timeout has to be longer than the
// host's own wait for the gateway, so the host's error answer gets through instead of a timeout here.
public sealed class HttpLearningMaterial(HttpClient http) : ILearningMaterial
{
    public const string DocumentsPath = "api/content/documents";
    public const string TopicsPath = "api/knowledge/topics";
    public const string FilesPath = "api/content/documents/files";

    // The most Content takes, counted like .NET and the browser count a string's length.
    public const int TextMaxLength = 200_000;

    public const int TitleMaxLength = 80;

    // Same words as Content, so a text refused here reads like one refused there.
    public const string EmptyText = "Der Text ist leer. Füge den Text ein, aus dem du lernen willst.";
    public const string TextTooLong = "Der Text ist zu lang. Erlaubt sind höchstens 200.000 Zeichen.";
    public const string TitleTooLong = "Der Titel ist zu lang. Erlaubt sind höchstens 80 Zeichen.";

    // An umlaut stays two bytes instead of the six of ü, so 200.000 characters stay well below a megabyte on the
    // way through the proxies. Only this body is written so, nothing of it ends up in a page.
    private static readonly PasteJson Relaxed = new(new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    public Task<CallOutcome<IReadOnlyList<DocumentInfo>>> ListDocumentsAsync(CancellationToken cancellationToken = default) =>
        CallAsync(token => http.GetAsync(DocumentsPath, token), MaterialJson.Default.IReadOnlyListDocumentInfo, cancellationToken);

    public Task<CallOutcome<IReadOnlyList<TopicInfo>>> ListTopicsAsync(CancellationToken cancellationToken = default) =>
        CallAsync(token => http.GetAsync(TopicsPath, token), MaterialJson.Default.IReadOnlyListTopicInfo, cancellationToken);

    public Task<CallOutcome<DocumentInfo>> PasteAsync(string? title, string text, CancellationToken cancellationToken = default) =>
        CallAsync(
            token => http.PostAsync(DocumentsPath, JsonContent.Create(new PastedText(title, text), Relaxed.PastedText), token),
            MaterialJson.Default.DocumentInfo,
            cancellationToken);

    public Task<CallOutcome<DocumentInfo>> RetryAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        CallAsync(token => http.PostAsync($"{DocumentsPath}/{documentId}/retry", content: null, token), MaterialJson.Default.DocumentInfo, cancellationToken);

    // The file streams up as it is read, as multipart under the name "file". Content judges it by its bytes. A file too
    // large comes back as TooLarge without a message, the page words it.
    public Task<CallOutcome<DocumentInfo>> UploadAsync(string fileName, Stream content, CancellationToken cancellationToken = default) =>
        CallAsync(
            async token =>
            {
                using var form = new MultipartFormDataContent();
                form.Add(new StreamContent(content), "file", fileName);
                return await http.PostAsync(FilesPath, form, token);
            },
            MaterialJson.Default.DocumentInfo,
            cancellationToken,
            tooLarge: []);

    private static Task<CallOutcome<T>> CallAsync<T>(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        JsonTypeInfo<T> answer,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? tooLarge = null) =>
        HostCall.GuardAsync(
            async () =>
            {
                using var response = await send(cancellationToken);
                switch (response.StatusCode)
                {
                    case HttpStatusCode.Unauthorized:
                        return CallOutcome<T>.SignedOut;
                    case HttpStatusCode.RequestEntityTooLarge:
                        return new CallOutcome<T>(CallResult.TooLarge, Messages: tooLarge ?? [TextTooLong]);
                    case HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity:
                        var (messages, traceId) = await RejectionAsync(response, cancellationToken);
                        return messages.Count > 0
                            ? new CallOutcome<T>(CallResult.Rejected, Messages: messages)
                            : CallOutcome<T>.Failed(traceId);
                    case var _ when !response.IsSuccessStatusCode:
                        return CallOutcome<T>.Failed(await ProblemTrace.ReadAsync(response, cancellationToken));
                }

                return await response.Content.ReadFromJsonAsync(answer, cancellationToken) is { } value
                    ? CallOutcome<T>.Ok(value)
                    : CallOutcome<T>.Failed();
            },
            cancellationToken);

    // The messages of a validation problem, meant to be shown as they are, and its trace. Anything else says nothing a
    // user could fix. The body can be read only once.
    private static async Task<(IReadOnlyList<string> Messages, string? TraceId)> RejectionAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase))
        {
            return ([], null);
        }

        try
        {
            var problem = await response.Content.ReadFromJsonAsync(MaterialJson.Default.ValidationProblem, cancellationToken);
            IReadOnlyList<string> messages = problem?.Errors is { } errors
                ? [.. errors.Values.SelectMany(texts => texts ?? []).Where(text => !string.IsNullOrWhiteSpace(text))]
                : [];
            return (messages, ProblemTrace.Check(problem?.TraceId));
        }
        catch (JsonException)
        {
            return ([], null);
        }
    }
}

internal sealed record PastedText(string? Title, string Text);

internal sealed record ValidationProblem(Dictionary<string, string[]?>? Errors, string? TraceId);

// Source-generated, so trimming the browser app cannot cut away what the serializer needs. An answer without the fields
// that make a document or a topic fails instead of turning into an empty one.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectRequiredConstructorParameters = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(IReadOnlyList<DocumentInfo>))]
[JsonSerializable(typeof(IReadOnlyList<TopicInfo>))]
[JsonSerializable(typeof(DocumentInfo))]
[JsonSerializable(typeof(ValidationProblem))]
internal sealed partial class MaterialJson : JsonSerializerContext;

[JsonSerializable(typeof(PastedText))]
internal sealed partial class PasteJson : JsonSerializerContext;