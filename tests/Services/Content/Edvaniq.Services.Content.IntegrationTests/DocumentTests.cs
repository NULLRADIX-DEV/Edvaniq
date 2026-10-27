using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.Content.IntegrationTests;

// A pasted text lands in the database as the user's document with status Uploaded, which the worker picks up. A text
// that can't become one is turned away with a reason the client can show.
public sealed class DocumentTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    [Fact]
    public async Task PastedText_IsStoredForTheUser_AndShowsItsStatus()
    {
        var alice = $"alice-{Guid.NewGuid():N}";
        using var client = ClientFor(alice);
        const string text = "Photosynthese\r\nLicht wird zu Zucker.";

        using var response = await client.PostAsJsonAsync("/documents", new { text }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<DocumentDto>(TestContext.Current.CancellationToken))!;
        Assert.Equal(("Photosynthese", "Uploaded", DateTimeKind.Utc), (created.Title, created.Status, created.CreatedAt.Kind));
        Assert.Equal($"/documents/{created.Id}", response.Headers.Location?.OriginalString);

        // Read back from the database, which keeps microseconds only.
        var found = await client.GetFromJsonAsync<DocumentDto>($"/documents/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(created with { CreatedAt = found!.CreatedAt }, found);
        Assert.Equal(created.CreatedAt, found.CreatedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal([found], (await client.GetFromJsonAsync<DocumentDto[]>("/documents", TestContext.Current.CancellationToken))!);

        using var scope = service.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ContentDbContext>().Documents.IgnoreQueryFilters()
            .SingleAsync(document => document.Id == created.Id, TestContext.Current.CancellationToken);
        Assert.Equal(alice, stored.OwnerId);
        Assert.Equal(text, stored.Text);
    }

    [Fact]
    public async Task EmptyText_IsRejectedWithAReason()
    {
        using var client = ClientFor($"alice-{Guid.NewGuid():N}");

        using var response = await client.PostAsJsonAsync("/documents", new { text = "  " }, TestContext.Current.CancellationToken);

        var errors = await ErrorsAsync(response);
        Assert.Equal(["Der Text ist leer. Füge den Text ein, aus dem du lernen willst."], errors["Text"]);
    }

    [Fact]
    public async Task TooLongTextAndTitle_AreRejectedWithTheirReasons()
    {
        using var client = ClientFor($"alice-{Guid.NewGuid():N}");
        var request = new { title = new string('t', 81), text = new string('a', 200_001) };

        using var response = await client.PostAsJsonAsync("/documents", request, TestContext.Current.CancellationToken);

        var errors = await ErrorsAsync(response);
        Assert.Equal(["Der Text ist zu lang. Erlaubt sind höchstens 200.000 Zeichen."], errors["Text"]);
        Assert.Equal(["Der Titel ist zu lang. Erlaubt sind höchstens 80 Zeichen."], errors["Title"]);
    }

    [Fact]
    public async Task TextAtTheLimit_IsStored()
    {
        using var client = ClientFor($"alice-{Guid.NewGuid():N}");

        using var response = await client.PostAsJsonAsync(
            "/documents", new { text = new string('ä', 200_000) }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private HttpClient ClientFor(string userId)
    {
        var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(userId));
        return client;
    }

    // The messages per field of a validation problem.
    private static async Task<Dictionary<string, string[]>> ErrorsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return problem.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
    }

    private sealed record DocumentDto(Guid Id, string Title, string Status, DateTime CreatedAt);
}
