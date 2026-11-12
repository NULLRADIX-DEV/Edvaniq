namespace Edvaniq.Services.Content.Domain;

// A file the document can't take, with the reason from the fixed list (DocumentFailures).
public sealed class FileRefusedException(DocumentFailure failure) : Exception(failure.Text)
{
    public DocumentFailure Failure { get; } = failure;

    // What the user reads.
    public string Reason => Failure.Text;
}