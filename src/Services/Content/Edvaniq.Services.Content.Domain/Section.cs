using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Content.Domain;

// A piece of a document's text (docs/spec/glossary.md#abschnitt), the ground for topics and questions. It keeps only
// where it lies in the text, the text itself stays in the document. Pasted text has no pages, the page comes with PDF
// (M2).
public sealed class Section : IOwnedByUser
{
    private Section(Guid documentId, string ownerId, int ordinal, int start, int length)
    {
        DocumentId = documentId;
        OwnerId = ownerId;
        Ordinal = ordinal;
        Start = start;
        Length = length;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid DocumentId { get; private set; }

    public string OwnerId { get; private set; }

    // 0 for the first section of a document, in the order of the text.
    public int Ordinal { get; private set; }

    // Index of the first character in Document.Text.
    public int Start { get; private set; }

    public int Length { get; private set; }

    internal static Section Of(Document document, int ordinal, TextSpan span) =>
        new(document.Id, document.OwnerId, ordinal, span.Start, span.Length);
}
