using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;
using Edvaniq.Services.Knowledge.Domain;
using Edvaniq.SharedKernel;

namespace Edvaniq.ArchitectureTests;

// #317: the assistant takes topics with Knowledge's limit and words, and drops a second name only when Knowledge would
// take both as one topic. The client can't reference either side, so a change on one side turns this red.
public class TopicValuesTests
{
    [Fact]
    public void Limit_AndItsWords_AreThoseOfKnowledge()
    {
        Assert.Equal(Topic.NameMaxLength, TopicValues.NameMaxLength);
        Assert.Equal(FeedbackTexts.Goal.TopicTooLong, Topic.CheckName(new string('a', Topic.NameMaxLength + 1)));
        Assert.Null(Topic.CheckName(new string('a', TopicValues.NameMaxLength)));
    }

    // Where the two could part: umlauts and ß, also first, digits, signs, case, several and other spaces, letters of
    // other scripts, a name of signs only.
    [Theory]
    [InlineData("pH-Wert")]
    [InlineData("  Zell   atmung ")]
    [InlineData("Übung 3: Größen")]
    [InlineData("Äpfel und Birnen")]
    [InlineData("STRAßE")]
    [InlineData("C# 12 / .NET 10")]
    [InlineData("Tab\tund\nZeile")]
    [InlineData("Ökosystem-2.0")]
    [InlineData("Ελληνικά und Привет")]
    [InlineData("İstanbul")]
    [InlineData("!!! ???")]
    [InlineData("")]
    public void Normalize_IsThatOfKnowledge(string name) =>
        Assert.Equal(TopicName.Normalize(name), TopicValues.Normalize(name));
}