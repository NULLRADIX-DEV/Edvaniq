using Edvaniq.BuildingBlocks.Domain;
using Edvaniq.SharedKernel;

namespace Edvaniq.Services.Knowledge.Domain;

// A key term of a topic (#292) with its definition, a verbatim quote of Content's section SectionId of the document
// DocumentId. Content checked the quote against that section before it sent the term. A term goes with its topic, and
// with its document when that is deleted or analysed again.
public sealed class KeyTerm : IOwnedByUser
{
    public const int TermMaxLength = 80;

    public const int DefinitionMaxLength = 300;

    private KeyTerm(string ownerId, Guid topicId, Guid documentId, Guid sectionId, string term, string definition, DateTime createdAt, int position)
    {
        OwnerId = ownerId;
        TopicId = topicId;
        DocumentId = documentId;
        SectionId = sectionId;
        Term = term;
        Definition = definition;
        CreatedAt = createdAt;
        Position = position;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string OwnerId { get; private set; }

    public Guid TopicId { get; private set; }

    public Guid DocumentId { get; private set; }

    public Guid SectionId { get; private set; }

    public string Term { get; private set; }

    public string Definition { get; private set; }

    // UTC.
    public DateTime CreatedAt { get; private set; }

    // The place in the analysis, keeps the order of terms that came at the same time.
    public int Position { get; private set; }

    // Two terms are the same after TopicName.Normalize, like two topic names.
    public static string KeyOf(string term) => TopicName.Normalize(term);

    // Cuts what is too long, an event that keeps throwing would come again and again.
    internal static KeyTerm Of(Topic topic, Guid documentId, Guid sectionId, string term, string definition, DateTime createdAt, int position) =>
        new(topic.OwnerId, topic.Id, documentId, sectionId, Cut(term, TermMaxLength), Cut(definition, DefinitionMaxLength), createdAt, position);

    private static string Cut(string text, int length)
    {
        var trimmed = text.Trim();
        return trimmed.Length > length ? trimmed[..length].TrimEnd() : trimmed;
    }
}