using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// #302: whether a document's generations fit into the day, and what the user reads when they don't.
public sealed class GenerationQuotaTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 23, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(20, 0, 20, true)]
    [InlineData(20, 15, 5, true)]
    [InlineData(20, 15, 6, false)]
    [InlineData(20, 0, 30, true)]
    [InlineData(20, 1, 30, false)]
    [InlineData(20, 20, 1, false)]
    public void Allows_WhatIsLeft_OrABigDocumentOnAFreshDay(int perDay, int used, int needed, bool allowed) =>
        Assert.Equal(allowed, new GenerationQuotaState(perDay, used, Now).Allows(needed));

    // A big document can take more than the day, what is left never goes below 0.
    [Fact]
    public void Left_IsNeverBelowZero() => Assert.Equal(0, new GenerationQuotaState(20, 30, Now).Left);

    [Fact]
    public void Messages_NameTheTopics_AndWhenTheQuotaStartsAgain()
    {
        Assert.Equal(
            "Dein Tageskontingent an Generierungen reicht für dieses Dokument nicht: 3 Themen, frei ist heute noch 1. Es wird am 13.11. um 04:00 Uhr zurückgesetzt.",
            GenerationQuota.TooMany(3, 1, Now));
        Assert.Equal(
            "Dein Tageskontingent an Generierungen ist aufgebraucht. Es wird am 13.11. um 04:00 Uhr zurückgesetzt.",
            GenerationQuota.UsedUp(Now));
        Assert.All([GenerationQuota.TooMany(30, 0, Now), GenerationQuota.UsedUp(Now)], message => Assert.InRange(message.Length, 1, 300));
    }

    [Fact]
    public void Charge_CountsOnTheStudyDay()
    {
        var document = Document.Paste("alice", Guid.NewGuid(), null, "Licht spaltet Wasser.", Now);
        var topic = Guid.NewGuid();

        var charge = GenerationCharge.For(document, topic, Now);

        Assert.Equal(("alice", document.Id, topic, new DateOnly(2026, 11, 12)), (charge.OwnerId, charge.DocumentId, charge.TopicId, charge.StudyDay));
    }
}