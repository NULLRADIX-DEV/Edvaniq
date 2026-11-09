namespace Edvaniq.Services.Assessment.Contracts;

// The mode an answer counts in (docs/spec/modes.md). The number goes over the broker, so it never changes.
public enum Mode
{
    Practice = 0,
    Placement = 1,
    MockExam = 2,
}