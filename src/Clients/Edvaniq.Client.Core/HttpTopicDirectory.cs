using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Edvaniq.Client.Core;

// Talks to Knowledge and Content through the web host, which wants a login. The HttpClient carries the base address of
// the host and keeps the session cookie.
public sealed class HttpTopicDirectory(HttpClient http) : ITopicDirectory
{
    public const string TopicsPath = "api/knowledge/topics";
    public const string DocumentsPath = "api/content/documents";

    public Task<CallOutcome<IReadOnlyList<TopicInfo>>> ListTopicsAsync(CancellationToken cancellationToken = default) =>
        GetAsync($"{TopicsPath}?includeHidden=true", DirectoryJson.Default.IReadOnlyListTopicInfo, cancellationToken);

    public Task<CallOutcome<IReadOnlyList<DocumentInfo>>> ListDocumentsAsync(CancellationToken cancellationToken = default) =>
        GetAsync(DocumentsPath, DirectoryJson.Default.IReadOnlyListDocumentInfo, cancellationToken);

    public Task<CallOutcome<IReadOnlyList<KeyTermInfo>>> TermsAsync(Guid topicId, CancellationToken cancellationToken = default) =>
        GetAsync($"{TopicsPath}/{topicId}/terms", DirectoryJson.Default.IReadOnlyListKeyTermInfo, cancellationToken);

    public Task<CallOutcome<IReadOnlyList<TopicSectionInfo>>> TopicSectionsAsync(Guid topicId, CancellationToken cancellationToken = default) =>
        GetAsync($"{TopicsPath}/{topicId}/sections", DirectoryJson.Default.IReadOnlyListTopicSectionInfo, cancellationToken);

    public Task<CallOutcome<IReadOnlyList<DocumentSectionInfo>>> DocumentSectionsAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        GetAsync($"{DocumentsPath}/{documentId}/sections", DirectoryJson.Default.IReadOnlyListDocumentSectionInfo, cancellationToken);

    private Task<CallOutcome<T>> GetAsync<T>(string path, JsonTypeInfo<T> answer, CancellationToken cancellationToken) =>
        HostCall.GuardAsync(
            async () =>
            {
                using var response = await http.GetAsync(path, cancellationToken);
                switch (response.StatusCode)
                {
                    case HttpStatusCode.Unauthorized:
                        return CallOutcome<T>.SignedOut;
                    case HttpStatusCode.NotFound:
                        return new CallOutcome<T>(CallResult.NotFound);
                    case var _ when !response.IsSuccessStatusCode:
                        return CallOutcome<T>.Failed(await ProblemTrace.ReadAsync(response, cancellationToken));
                }

                return await response.Content.ReadFromJsonAsync(answer, cancellationToken) is { } value
                    ? CallOutcome<T>.Ok(value)
                    : CallOutcome<T>.Failed();
            },
            cancellationToken);
}

// Source-generated for the trimmed browser app. An answer without what makes a topic, a term or a section fails.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectRequiredConstructorParameters = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(IReadOnlyList<TopicInfo>))]
[JsonSerializable(typeof(IReadOnlyList<DocumentInfo>))]
[JsonSerializable(typeof(IReadOnlyList<KeyTermInfo>))]
[JsonSerializable(typeof(IReadOnlyList<TopicSectionInfo>))]
[JsonSerializable(typeof(IReadOnlyList<DocumentSectionInfo>))]
internal sealed partial class DirectoryJson : JsonSerializerContext;