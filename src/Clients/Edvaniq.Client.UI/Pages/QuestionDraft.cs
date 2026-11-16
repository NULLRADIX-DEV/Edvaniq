using Edvaniq.Client.Core;

namespace Edvaniq.Client.UI.Pages;

// A question left open because the login ran out, with what was ticked and the time measured so far (QuestionCard).
public sealed record QuestionDraft(AskedQuestion Asked, long ShownAt, int? Selected, int? ResponseTimeMs, IReadOnlyList<int>? Ticked = null);