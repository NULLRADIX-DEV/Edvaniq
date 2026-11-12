namespace Edvaniq.Services.LearningEngine.Domain;

// Where Mastery.Of stands right after a counted record: the value before decay, that record's time, and whether it and
// the one before were correct (the window of TopicStates.HasHighRecentErrorRate). PreviousCorrect is null after the
// first record.
public sealed record MasteryFold(double Value, DateTime At, bool LastCorrect, bool? PreviousCorrect);