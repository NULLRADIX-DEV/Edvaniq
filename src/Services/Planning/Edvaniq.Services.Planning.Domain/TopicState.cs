namespace Edvaniq.Services.Planning.Domain;

// The topic states LearningEngine names on the wire (docs/spec/glossary.md#themenzustand), only a mirror of its names.
// Planning never works one out: the values only ever come from LearningEngine, the thresholds stay there (#367,
// TopicStateRule in the architecture tests).
public enum TopicState
{
    New,
    Weakness,
    Building,
    Secure,
}