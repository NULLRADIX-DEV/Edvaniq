using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.ServiceName.Domain;

// Example of data that belongs to one user, for the isolation test of the template. Replace it with the service's first
// real entity (docs/service-template.md#beispiel).
public sealed class ExampleItem(string ownerId, string text) : IOwnedByUser
{
    public const int TextMaxLength = 200;

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string OwnerId { get; private set; } = ownerId;

    public string Text { get; private set; } = text;

    public static bool IsValidText(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= TextMaxLength;
}
