using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.UnitTests;

// #301: how many pages a document counts, and what the user reads when they don't fit.
public sealed class PageQuotaTests
{
    // 23:30 UTC on 12.11. is 00:30 in Berlin, still the study day 12.11., so the quota starts again on 13.11. at 04:00.
    private static readonly DateTime Now = new(2026, 11, 12, 23, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3_000, 1)]
    [InlineData(3_001, 2)]
    [InlineData(6_000, 2)]
    [InlineData(200_000, 67)]
    public void TextWithoutPages_CountsByItsLength(int characters, int pages) =>
        Assert.Equal(pages, PageQuota.PagesOfText(new string('a', characters)));

    [Fact]
    public void Pdf_CountsItsPages_OtherFilesTheirLength()
    {
        var pdf = Document.Upload("alice", Guid.NewGuid(), "Skript.pdf", Guid.NewGuid(), FileFormat.Pdf, Now);
        pdf.TakeText([.. Enumerable.Repeat("kurz", 12)], Now);
        var text = Document.Upload("alice", Guid.NewGuid(), "Notizen.txt", Guid.NewGuid(), FileFormat.Text, Now);
        text.TakeText([string.Concat(Enumerable.Repeat("Wasser ", 1_000))], Now);

        Assert.Equal(12, PageQuota.PagesOf(pdf, 12));
        Assert.Equal(3, PageQuota.PagesOf(text, 1));
    }

    [Fact]
    public void Messages_NameThePages_AndWhenTheQuotaStartsAgain()
    {
        Assert.Equal(
            "Dein Tageskontingent reicht für dieses Dokument nicht: 12 Seiten, frei sind heute noch 3. Es wird am 13.11. um 04:00 Uhr zurückgesetzt.",
            PageQuota.TooManyPages(12, 3, Now));
        Assert.Equal(
            "Dein Tageskontingent reicht für dieses Dokument nicht: 2 Seiten, frei ist heute noch 1. Es wird am 13.11. um 04:00 Uhr zurückgesetzt.",
            PageQuota.TooManyPages(2, 1, Now));
        Assert.Equal(
            "Dein Tageskontingent an Seiten ist aufgebraucht. Es wird am 13.11. um 04:00 Uhr zurückgesetzt.",
            PageQuota.UsedUp(Now));
        Assert.All([PageQuota.TooManyPages(999, 0, Now), PageQuota.UsedUp(Now)], message => Assert.InRange(message.Length, 1, 300));
    }

    [Fact]
    public void Charge_CountsOnTheStudyDay()
    {
        var document = Document.Paste("alice", Guid.NewGuid(), null, "Licht spaltet Wasser.", Now);

        var charge = PageCharge.For(document, 4, Now);

        Assert.Equal(("alice", document.Id, new DateOnly(2026, 11, 12), 4), (charge.OwnerId, charge.DocumentId, charge.StudyDay, charge.Pages));
    }
}