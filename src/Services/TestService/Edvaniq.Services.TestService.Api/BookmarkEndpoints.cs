using Edvaniq.Services.TestService.Application;
using Edvaniq.Services.TestService.Domain;

namespace Edvaniq.Services.TestService.Api;

// The owner never appears in a request or a response: it comes from the token, and the owner filter applies it.
internal static class BookmarkEndpoints
{
    public static IEndpointRouteBuilder MapBookmarkEndpoints(this IEndpointRouteBuilder app)
    {
        var bookmarks = app.MapGroup("/bookmarks");

        bookmarks.MapPost("/", async (NewBookmark request, IBookmarks items, CancellationToken cancellationToken) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (!Bookmark.IsValidUrl(request.Url))
            {
                errors[nameof(request.Url)] = [$"Url is an absolute http or https link with at most {Bookmark.UrlMaxLength} characters."];
            }

            if (!Bookmark.IsValidTitle(request.Title))
            {
                errors[nameof(request.Title)] = [$"Title is required and has at most {Bookmark.TitleMaxLength} characters."];
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var bookmark = await items.AddAsync(request.Url, request.Title, cancellationToken);
            return Results.Created($"/bookmarks/{bookmark.Id}", BookmarkResponse.From(bookmark));
        });

        bookmarks.MapGet("/", async (IBookmarks items, CancellationToken cancellationToken) =>
            (await items.ListAsync(cancellationToken)).Select(BookmarkResponse.From));

        // Someone else's bookmark and a bookmark that does not exist look the same: 404.
        bookmarks.MapGet("/{id:guid}", async (Guid id, IBookmarks items, CancellationToken cancellationToken) =>
            await items.FindAsync(id, cancellationToken) is { } bookmark
                ? Results.Ok(BookmarkResponse.From(bookmark))
                : Results.NotFound());

        return app;
    }
}

internal sealed record NewBookmark(string Url, string Title);

internal sealed record BookmarkResponse(Guid Id, string Url, string Title)
{
    public static BookmarkResponse From(Bookmark bookmark) => new(bookmark.Id, bookmark.Url, bookmark.Title);
}