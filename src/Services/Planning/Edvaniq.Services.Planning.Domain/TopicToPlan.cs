namespace Edvaniq.Services.Planning.Domain;

// A topic as the planner sees it. Size is how much there is to learn (e.g. the number of sections), mastery comes from
// LearningEngine (0 to 100). IsNew: not introduced yet (docs/spec/glossary.md#eingeführt), so it gets no review and no
// time in the final phase. False keeps the planner as it was before #369. IsWeakness: LearningEngine says weakness, so
// the topic keeps its learn time when the time is too short (#370).
public sealed record TopicToPlan(Guid TopicId, int Size, int Mastery, bool IsNew = false, bool IsWeakness = false);