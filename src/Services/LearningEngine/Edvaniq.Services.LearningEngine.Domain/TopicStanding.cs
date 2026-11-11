namespace Edvaniq.Services.LearningEngine.Domain;

// Where a user stands with one topic right now: mastery in whole percent and the state that follows from it.
public sealed record TopicStanding(int Mastery, TopicState State);