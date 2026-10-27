using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.Content.Contracts;

// The analysis of a document is done (docs/spec/events.md). Knowledge takes the topics over and gives each new one its
// topicId. UserId is the OwnerId of the document. The learning goal comes with the standard goal (#215), existing topics
// as a target with the stable topic IDs (#208), key terms in M2.
public sealed record DocumentAnalyzed(string UserId, Guid DocumentId, IReadOnlyList<TopicSuggestion> Topics) : IIntegrationEvent;

// A new topic as the analysis suggests it. Parent is the index of another suggestion in the same event, which always
// comes first, or null for a topic at the top. SectionIds are the sections of the document that belong to it, each
// section of the document belongs to exactly one suggestion.
public sealed record TopicSuggestion(string Name, int? Parent, IReadOnlyList<Guid> SectionIds);
