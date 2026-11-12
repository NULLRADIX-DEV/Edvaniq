using Edvaniq.Services.Content.Application;

namespace Edvaniq.Services.Content.UnitTests;

// #289: users take turns at the worker, each with their oldest document first.
public sealed class FairOrderTests
{
    private static readonly DateTime Start = new(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ManyDocumentsOfOneUser_LetTheOtherUserIn_AfterTheirFirst()
    {
        var (a1, a2, a3, b1) = (Waiting("alice", 0), Waiting("alice", 1), Waiting("alice", 2), Waiting("bob", 3));

        Assert.Equal([a1.Id, b1.Id, a2.Id, a3.Id], FairOrder.Of([a3, b1, a1, a2], 10));
    }

    [Fact]
    public void EachTurn_GoesOldestFirst()
    {
        var (a1, a2, b1, b2, c1) = (Waiting("alice", 2), Waiting("alice", 5), Waiting("bob", 1), Waiting("bob", 3), Waiting("carol", 4));

        Assert.Equal([b1.Id, a1.Id, c1.Id, b2.Id, a2.Id], FairOrder.Of([a1, a2, b1, b2, c1], 10));
    }

    [Fact]
    public void OneUser_GetsAllTheirDocumentsInARow()
    {
        var documents = Enumerable.Range(0, 5).Select(minute => Waiting("alice", minute)).ToList();

        Assert.Equal(documents.Select(document => document.Id), FairOrder.Of(documents, 10));
    }

    [Fact]
    public void Take_CutsTheTurns_AndADocumentCountsOnce()
    {
        var (a1, a2, b1) = (Waiting("alice", 0), Waiting("alice", 1), Waiting("bob", 2));

        Assert.Equal([a1.Id, b1.Id], FairOrder.Of([a1, a1, b1, a2, b1], 2));
    }

    [Fact]
    public void Owners_AreTold_ApartByteForByte()
    {
        var (upper, lower, again) = (Waiting("Alice", 0), Waiting("alice", 1), Waiting("Alice", 2));

        Assert.Equal([upper.Id, lower.Id, again.Id], FairOrder.Of([upper, lower, again], 10));
    }

    private static WaitingDocument Waiting(string owner, int minute) => new(Guid.NewGuid(), owner, Start.AddMinutes(minute));
}