using System.Reflection;

using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// #283: a document fails only with a reason from the fixed list, and the list covers every code of the issue.
public sealed class DocumentFailureTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void All_HoldsEveryReason_OnceAndWithItsOwnText()
    {
        var declared = typeof(DocumentFailures).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(DocumentFailure))
            .Select(field => (DocumentFailure)field.GetValue(null)!)
            .ToList();

        Assert.Equal(declared.Order(Comparer), DocumentFailures.All.Order(Comparer));
        Assert.Equal(DocumentFailures.All.Count, DocumentFailures.All.Select(failure => failure.Text).Distinct().Count());
        Assert.All(DocumentFailures.All, failure => Assert.InRange(failure.Text.Length, 1, Document.FailureReasonMaxLength));
    }

    [Fact]
    public void EveryCodeOfTheList_HasAReason() =>
        Assert.Equal(Enum.GetValues<FailureCode>().Order(), DocumentFailures.All.Select(failure => failure.Code).Distinct().Order());

    [Fact]
    public void Fail_StoresTextAndCode_AndRetryClearsBoth()
    {
        var document = Document.Paste("alice", Guid.NewGuid(), null, "Licht spaltet Wasser.", Now);
        document.Split();

        document.Fail(DocumentFailures.AiPaused);

        Assert.Equal(
            (DocumentStatus.Failed, Document.AiPaused, (FailureCode?)FailureCode.AiUnavailable),
            (document.Status, document.FailureReason, document.FailureCode));
        document.Retry(Now);
        Assert.Equal(((string?)null, (FailureCode?)null), (document.FailureReason, document.FailureCode));
    }

    [Theory]
    [InlineData(Document.NothingToLearn, FailureCode.NoText)]
    [InlineData(Document.TooManyPages, FailureCode.TooLarge)]
    [InlineData(FileCheck.TakesTooLong, FailureCode.Unreadable)]
    [InlineData(Document.TopicsMissing, FailureCode.Unknown)]
    [InlineData(Document.AiBudgetReached, FailureCode.LimitReached)]
    [InlineData(Document.NoTextInFile, FailureCode.NoText)]
    [InlineData(Document.AnalysisTimedOut, FailureCode.Unknown)]
    [InlineData("Ein Grund, den es nicht mehr gibt.", FailureCode.Unknown)]
    public void CodeOf_AStoredText_FindsItsPlace_OrUnknown(string text, FailureCode code) =>
        Assert.Equal(code, DocumentFailures.CodeOf(text));

    [Fact]
    public void QuotaFailures_AreLimitReached_AndTheirTextsAreFoundAgain()
    {
        DocumentFailure[] failures =
        [
            DocumentFailures.PagesOverQuota(12, 3, Now),
            DocumentFailures.GenerationsOverQuota(3, 1, Now),
        ];

        Assert.Equal(
            [PageQuota.TooManyPages(12, 3, Now), GenerationQuota.TooMany(3, 1, Now)],
            failures.Select(failure => failure.Text));
        Assert.All(failures, failure => Assert.Equal(FailureCode.LimitReached, failure.Code));
        Assert.All(failures, failure => Assert.Equal(FailureCode.LimitReached, DocumentFailures.CodeOf(failure.Text)));
    }

    [Fact]
    public void FileRefusal_CarriesAReasonFromTheList()
    {
        var refused = new FileRefusedException(DocumentFailures.PackedTooTightly);

        Assert.Equal((FailureCode.Unreadable, FileCheck.PackedTooTightly), (refused.Failure.Code, refused.Reason));
    }

    private static readonly Comparer<DocumentFailure> Comparer =
        Comparer<DocumentFailure>.Create((a, b) => string.CompareOrdinal(a.Text, b.Text));
}