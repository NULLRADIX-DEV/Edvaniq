namespace Edvaniq.Services.Content.Domain;

// A file the document can't take, with the reason the user sees.
public sealed class FileRefusedException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}