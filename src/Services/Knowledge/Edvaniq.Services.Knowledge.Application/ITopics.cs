using Edvaniq.Services.Knowledge.Domain;

namespace Edvaniq.Services.Knowledge.Application;

// The current user's topics. Every change that sticks stores one TopicsChanged with it, one that changes nothing none.
public interface ITopics
{
    // Both levels, oldest first, hidden ones too. Without a learning goal all of the user's topics.
    Task<IReadOnlyList<Topic>> ListAsync(Guid? learningGoalId, CancellationToken cancellationToken);

    Task<Topic?> FindAsync(Guid id, CancellationToken cancellationToken);

    // The key terms of the topic (#292), each term once in the order they came, null if the user has no such topic. A
    // hidden topic has its terms too, as GET /topics/{id} still gives the topic itself.
    Task<IReadOnlyList<KeyTerm>?> TermsAsync(Guid topicId, CancellationToken cancellationToken);

    // The sections of Content that are the topic's material (#294), by document, null if the user has no such topic. A
    // hidden topic has them too, like its terms.
    Task<IReadOnlyList<TopicSection>?> SectionsAsync(Guid topicId, CancellationToken cancellationToken);

    // The names must have passed Topic.CheckName.
    Task<TopicResult> RenameAsync(Guid id, string name, CancellationToken cancellationToken);

    // A topic of the upper level of the learning goal, or with a parent a subtopic in the parent's goal.
    Task<TopicResult> AddAsync(Guid learningGoalId, Guid? parentId, string name, CancellationToken cancellationToken);

    Task<TopicResult> SetHiddenAsync(Guid id, bool hidden, CancellationToken cancellationToken);
}

public enum TopicError
{
    None,

    // The user has no such topic, or no such parent.
    NotFound,

    // Another topic on the same level under the same parent has the name. Analyses merge topics by name, two of one
    // name would get the sections of a later analysis by chance.
    NameTaken,

    // Topics have two levels, a subtopic takes none.
    ParentIsSubtopic,

    // A subtopic comes back only with its topic.
    ParentHidden,

    // The topics of the learning goal changed again and again while this tried, an analysis keeps running on them.
    Busy,
}

public sealed record TopicResult(Topic? Topic, TopicError Error)
{
    public static TopicResult Ok(Topic topic) => new(topic, TopicError.None);

    public static TopicResult Fail(TopicError error) => new(null, error);
}