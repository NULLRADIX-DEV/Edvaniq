using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.Services.Content.Contracts;

// The user deleted a document (docs/spec/events.md). Every receiver removes what came from it. UserId is the OwnerId of
// the document, LearningGoalId the goal it was pasted into, null for a document from before the default goal (#215).
public sealed record DocumentDeleted(string UserId, Guid DocumentId, Guid? LearningGoalId = null) : IIntegrationEvent;