using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Content.Domain;

// A page of a document read from a file. Like a section it keeps only where it lies in Document.Text, so a section
// finds its page by its start. Pasted text has no pages.
public sealed class DocumentPage : IOwnedByUser
{
    private DocumentPage(Guid documentId, string ownerId, int number, int start, int length)
    {
        DocumentId = documentId;
        OwnerId = ownerId;
        Number = number;
        Start = start;
        Length = length;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid DocumentId { get; private set; }

    public string OwnerId { get; private set; }

    // As the PDF counts them, from 1. A text file is one page.
    public int Number { get; private set; }

    // Index of the first character in Document.Text. An empty page has length 0.
    public int Start { get; private set; }

    public int Length { get; private set; }

    internal static DocumentPage Of(Document document, int number, int start, int length) =>
        new(document.Id, document.OwnerId, number, start, length);
}