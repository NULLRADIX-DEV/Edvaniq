namespace Edvaniq.Client.UI.Pages;

// What SummaryView tells a caller (#358): still coming, there, not there for now (failed, limited, no material, slow, no
// connection) or the topic is gone.
public enum SummaryState
{
    Pending,
    Ready,
    Unavailable,
    Gone,
}

// How a new topic unit went on this device, for the session runner (#396). SummaryShown: the summary was there when the
// user went on. Check is null when the topic had no check questions yet. Only the display: what a session counts comes
// from Planning (#403).
public sealed record NewTopicUnitResult(bool SummaryShown, QuizTally? Check);
