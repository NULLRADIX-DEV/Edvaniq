using Edvaniq.Services.Knowledge.Domain;

namespace Edvaniq.Services.Knowledge.Application;

// Which topics go when a document goes. A topic goes if it had sections of the document or came from its analysis, has
// no section of another document left and no subtopic that stays. So a topic two documents brought stays with the
// sections of the other one, and a topic the user added stays anyway.
public static class DocumentRemoval
{
    // topics holds every topic in question together with all its subtopics. withSectionsOfDocument are the topics that
    // had sections of the document, withOtherSections those with sections of another document.
    public static IReadOnlySet<Guid> TopicsToRemove(
        Guid documentId, IReadOnlyList<Topic> topics, IReadOnlySet<Guid> withSectionsOfDocument, IReadOnlySet<Guid> withOtherSections)
    {
        var removed = new HashSet<Guid>();
        foreach (var topic in topics.Where(topic => topic.IsSubtopic).Concat(topics.Where(topic => !topic.IsSubtopic)))
        {
            if ((withSectionsOfDocument.Contains(topic.Id) || topic.DocumentId == documentId)
                && !withOtherSections.Contains(topic.Id)
                && topics.Where(child => child.ParentId == topic.Id).All(child => removed.Contains(child.Id)))
            {
                removed.Add(topic.Id);
            }
        }

        return removed;
    }
}