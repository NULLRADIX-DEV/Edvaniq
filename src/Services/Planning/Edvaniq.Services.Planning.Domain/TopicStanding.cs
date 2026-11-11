namespace Edvaniq.Services.Planning.Domain;

// Mastery (0 to 100) and state of one topic as LearningEngine answered them. State null: LearningEngine named a state
// Planning doesn't know (a newer rule), the mastery still counts.
public sealed record TopicStanding(int Mastery, TopicState? State);