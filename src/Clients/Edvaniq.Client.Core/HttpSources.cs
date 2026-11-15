using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edvaniq.Client.Core;

// Talks to Content through the web host, which wants a login, like HttpTopicDirectory.
public sealed class HttpSources(HttpClient http) : ISources
{
    public const string SectionsPath = "api/content/sections";
    public const string DocumentsPath = "api/content/documents";

    // The uploaded file through the web host, relative to the app's base address. A PDF opens at the page, the browser's
    // viewer reads #page.
    public static string FilePath(Guid documentId, int? page = null) =>
        $"{DocumentsPath}/{documentId:D}/file" + (page is { } number ? "#page=" + number.ToString(CultureInfo.InvariantCulture) : "");

    public Task<CallOutcome<SectionSource>> SectionAsync(Guid sectionId, CancellationToken cancellationToken = default) =>
        HostCall.GuardAsync(
            async () =>
            {
                using var response = await http.GetAsync($"{SectionsPath}/{sectionId:D}/source", cancellationToken);
                switch (response.StatusCode)
                {
                    case HttpStatusCode.Unauthorized:
                        return CallOutcome<SectionSource>.SignedOut;
                    case HttpStatusCode.NotFound:
                        return new CallOutcome<SectionSource>(CallResult.NotFound);
                    case var _ when !response.IsSuccessStatusCode:
                        return CallOutcome<SectionSource>.Failed(await ProblemTrace.ReadAsync(response, cancellationToken));
                }

                return await response.Content.ReadFromJsonAsync(SourceJson.Default.SectionSource, cancellationToken) is { } source && Valid(source)
                    ? CallOutcome<SectionSource>.Ok(source)
                    : CallOutcome<SectionSource>.Failed();
            },
            cancellationToken);

    // A mark has to lie within its text, or the page would cut the text wrong.
    private static bool Valid(SectionSource source) =>
        source.Text is null
            ? source.Start is null && source.Length is null
            : source is { Start: >= 0, Length: >= 0 } && source.Start + source.Length <= source.Text.Length;
}

// Source-generated for the trimmed browser app. An answer without document, section or title fails.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(SectionSource))]
internal sealed partial class SourceJson : JsonSerializerContext;