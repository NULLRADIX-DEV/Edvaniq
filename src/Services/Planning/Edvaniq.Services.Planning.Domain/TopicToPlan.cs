namespace Edvaniq.Services.Planning.Domain;

// A topic as the planner sees it. Size is how much there is to learn (e.g. the number of sections), mastery comes from
// LearningEngine (0 to 100).
public sealed record TopicToPlan(Guid TopicId, int Size, int Mastery);