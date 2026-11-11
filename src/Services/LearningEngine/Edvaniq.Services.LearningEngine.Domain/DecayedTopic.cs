namespace Edvaniq.Services.LearningEngine.Domain;

// A topic that was secure when the user last answered on it and isn't any more, only because time went by. Mastery now
// and at the last answer in whole percent.
public sealed record DecayedTopic(int Mastery, int MasteryAtLastAnswer, DateTime LastAnsweredAt);