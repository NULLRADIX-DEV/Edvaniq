using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.Content.Contracts;

// The analysis of a document is done (docs/spec/events.md). Knowledge takes the topics over and gives each new one its
// topicId. UserId is the OwnerId of the document, LearningGoalId the goal the text was pasted into, null in messages from
// before the default goal (#215). Existing topics as a target come with the stable topic IDs (#208), key terms in M2.
public sealed record DocumentAnalyzed(
    string UserId, Guid DocumentId, IReadOnlyList<TopicSuggestion> Topics, Guid? LearningGoalId = null) : IUserEvent;

// A new topic as the analysis suggests it. Parent is the index of another suggestion in the same event, which always
// comes first, or null for a topic at the top. SectionIds are the sections of the document that belong to it, each
// section of the document belongs to exactly one suggestion.
// Terms are its key terms (#292), each with a definition quoted verbatim from one of its sections or those of its
// subtopics, null when the analysis gave none or came from before.
public sealed record TopicSuggestion(string Name, int? Parent, IReadOnlyList<Guid> SectionIds, IReadOnlyList<KeyTermSuggestion>? Terms = null);

// A key term of a topic. Definition is a verbatim excerpt of the section SectionId, the analysis checked it there.
public sealed record KeyTermSuggestion(string Term, Guid SectionId, string Definition);