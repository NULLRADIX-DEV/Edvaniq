using Edvaniq.Client.Core;

namespace Edvaniq.Client.UnitTests;

// Answers like the queue says, the last answer again after that, Ok without one. Remembers what it was sent.
internal sealed class FakePasswordChange : IPasswordChange
{
    private readonly Queue<CallOutcome<bool>> answers = new();

    public List<(string Current, string New)> Sent { get; } = [];

    public FakePasswordChange Answer(CallOutcome<bool> outcome)
    {
        answers.Enqueue(outcome);
        return this;
    }

    public Task<CallOutcome<bool>> ChangeAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        Sent.Add((currentPassword, newPassword));
        var outcome = answers.Count > 1 ? answers.Dequeue() : answers.Count == 1 ? answers.Peek() : CallOutcome<bool>.Ok(true);
        return Task.FromResult(outcome);
    }
}
