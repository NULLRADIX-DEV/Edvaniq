namespace Edvaniq.Services.LearningEngine.Domain;

// Where a user stands with a topic, always exactly one of these (docs/spec/glossary.md#themenzustand). Worked out by
// TopicStates, never stored.
public enum TopicState
{
    New = 0,
    Weakness = 1,
    Building = 2,
    Secure = 3,
}