using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Testing;

using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Content.IntegrationTests;

// #278: a text or a file is only taken with the confirmation that it is the user's own material or theirs to use, with
// nothing about other people, in the version of the wording the client shows. The document keeps version and time.
// Material comes only as pasted text or uploaded file, Content never fetches anything from a URL.
public sealed partial class MaterialConfirmationTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Text = "Licht spaltet Wasser in der Lichtreaktion, und im Calvin-Zyklus entsteht daraus Zucker.";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("material-0")]
    [InlineData("MATERIAL-1")]
    public async Task Paste_WithoutTheCurrentConfirmation_IsRefused(string? confirmation)
    {
        var user = NewUser();
        using var client = ClientFor(user);

        using var response = await client.PostAsJsonAsync("/documents", new { text = Text, confirmation }, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([MaterialConfirmation.Missing], await ErrorAsync(response, "Confirmation"));
        Assert.Equal(0, Query(db => db.Documents.IgnoreQueryFilters().Count(document => document.OwnerId == user)));
    }

    // Refused before the body is read: no file is stored.
    [Theory]
    [InlineData("/documents/files")]
    [InlineData("/documents/files?confirmation=material-0")]
    public async Task Upload_WithoutTheCurrentConfirmation_IsRefused_AndNothingIsStored(string path)
    {
        var user = NewUser();
        using var client = ClientFor(user);

        using var response = await client.PostAsync(path, Form(), Cancel);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal([MaterialConfirmation.Missing], await ErrorAsync(response, "confirmation"));
        Assert.Equal(0, Query(db => db.StoredFiles.IgnoreQueryFilters().Count(file => file.OwnerId == user)));
    }

    [Fact]
    public async Task ConfirmedMaterial_KeepsVersionAndTime()
    {
        var user = NewUser();
        using var client = ClientFor(user);
        var before = DateTime.UtcNow.AddSeconds(-1);

        using var pasted = await client.PostAsJsonAsync("/documents", new { text = Text, confirmation = MaterialConfirmation.Version }, Cancel);
        using var uploaded = await client.PostAsync($"/documents/files?confirmation={MaterialConfirmation.Version}", Form(), Cancel);

        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (pasted.StatusCode, uploaded.StatusCode));
        var documents = Query(db => db.Documents.IgnoreQueryFilters().Where(document => document.OwnerId == user).ToList());
        Assert.Equal(2, documents.Count);
        Assert.All(documents, document =>
        {
            Assert.Equal(MaterialConfirmation.Version, document.MaterialConfirmationVersion);
            Assert.InRange(document.MaterialConfirmedAt!.Value, before, DateTime.UtcNow.AddSeconds(1));
            Assert.Equal(DateTimeKind.Utc, document.MaterialConfirmedAt.Value.Kind);
        });
    }

    // No route of Content takes a URL, a link or a source to fetch material from: not in its path, not as a parameter,
    // not as a field of a request body.
    [Fact]
    public void NoRoute_TakesAUrl()
    {
        var names = service.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(endpoint => endpoint.RoutePattern.Parameters.Select(parameter => parameter.Name)
                .Concat(endpoint.Metadata.OfType<IParameterBindingMetadata>().Select(parameter => parameter.Name))
                .Concat(endpoint.Metadata.OfType<IAcceptsMetadata>()
                    .SelectMany(accepts => accepts.RequestType?.GetProperties().Select(property => property.Name) ?? []))
                .Concat([endpoint.RoutePattern.RawText ?? ""]))
            .ToList();

        Assert.Contains("Text", names);
        Assert.DoesNotContain(names, name => UrlLike().IsMatch(name));
    }

    // A pasted URL is only text: nothing is fetched, and alone it has too few words to become a document.
    [Fact]
    public async Task PastedUrl_IsOnlyText()
    {
        using var client = ClientFor(NewUser());

        using var response = await client.PostAsJsonAsync(
            "/documents", new { text = "https://example.org/skript.pdf", confirmation = MaterialConfirmation.Version }, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([Document.TooFewWords], await ErrorAsync(response, "Text"));
    }

    private static MultipartFormDataContent Form()
    {
        var file = new ByteArrayContent("Ein Text über die Photosynthese in grünen Pflanzen und Algen am Tag."u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        return new MultipartFormDataContent { { file, "file", "Notizen.txt" } };
    }

    private static async Task<string[]> ErrorAsync(HttpResponseMessage response, string field)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        return problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(error => error.GetString()!).ToArray();
    }

    private T Query<T>(Func<ContentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ContentDbContext>());
    }

    private HttpClient ClientFor(string user)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        return client;
    }

    private static string NewUser() => $"alice-{Guid.NewGuid():N}";

    [GeneratedRegex("url|uri|link|href|source|fetch|import", RegexOptions.IgnoreCase)]
    private static partial Regex UrlLike();
}