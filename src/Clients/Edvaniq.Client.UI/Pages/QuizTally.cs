namespace Edvaniq.Client.UI.Pages;

// What a block's quiz counted on this device: Right of Answered, out of Total questions. A question the app couldn't
// show isn't answered. Only for the display, a session's result comes from Planning (#403).
public sealed record QuizTally(int Right, int Answered, int Total);