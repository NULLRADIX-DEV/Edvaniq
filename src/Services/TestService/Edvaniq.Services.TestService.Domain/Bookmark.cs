using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.TestService.Domain;

// A link a user keeps with a title. The first entity of the test service from #155.
public sealed class Bookmark(string ownerId, string url, string title) : IOwnedByUser
{
    public const int UrlMaxLength = 2000;

    public const int TitleMaxLength = 200;

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string OwnerId { get; private set; } = ownerId;

    public string Url { get; private set; } = url;

    public string Title { get; private set; } = title;

    // Only absolute http and https links, nothing a browser would run like javascript:.
    public static bool IsValidUrl(string? url) =>
        url is { Length: <= UrlMaxLength }
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https";

    public static bool IsValidTitle(string? title) =>
        !string.IsNullOrWhiteSpace(title) && title.Length <= TitleMaxLength;
}