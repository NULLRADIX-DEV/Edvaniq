using System.Globalization;

using Edvaniq.BuildingBlocks.Domain;
using Edvaniq.SharedKernel;

namespace Edvaniq.Services.Content.Domain;

// The pages a document took from its owner's daily quota, on the study day it was first processed. One per document, so
// a retry takes nothing twice, and it stays when the document is deleted, or deleting and uploading again would reset
// the quota. Only the deletion of the user takes it.
public sealed class PageCharge : IOwnedByUser
{
    private PageCharge(string ownerId, Guid documentId, DateOnly studyDay, int pages, DateTime chargedAt)
    {
        OwnerId = ownerId;
        DocumentId = documentId;
        StudyDay = studyDay;
        Pages = pages;
        ChargedAt = chargedAt;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string OwnerId { get; private set; }

    // Content's document, only a reference: no foreign key, the charge outlives it.
    public Guid DocumentId { get; private set; }

    public DateOnly StudyDay { get; private set; }

    public int Pages { get; private set; }

    // UTC.
    public DateTime ChargedAt { get; private set; }

    public static PageCharge For(Document document, int pages, DateTime now) =>
        new(document.OwnerId, document.Id, SharedKernel.StudyDay.Of(now), pages, now);
}

// How many pages a document counts, and what the user reads when they are too many (docs/service-internals.md).
public static class PageQuota
{
    // A text without pages of its own, pasted or from a TXT, Markdown or Word file, counts one page for every 3,000
    // characters begun: a well filled page of a book. So a huge text file is no way around the quota.
    public const int CharactersPerPage = 3_000;

    public const int DefaultPagesPerDay = 200;

    public static int PagesOfText(string text) => Math.Max(1, (text.Length + CharactersPerPage - 1) / CharactersPerPage);

    // A PDF counts its pages, everything else its length.
    public static int PagesOf(Document document, int filePages) =>
        document.FileFormat == FileFormat.Pdf ? Math.Max(1, filePages) : PagesOfText(document.Text);

    // When the quota of the study day of now starts again, as the user reads it: always 04:00 in Berlin.
    public static string ResetText(DateTime now) =>
        $"am {SharedKernel.StudyDay.Of(now).AddDays(1).ToString("dd.MM.", CultureInfo.InvariantCulture)} um 04:00 Uhr";

    public static string TooManyPages(int pages, int left, DateTime now) =>
        $"Dein Tageskontingent reicht für dieses Dokument nicht: {pages} {Pages(pages)}, frei {(left == 1 ? "ist" : "sind")} heute noch {left}. "
        + $"Es wird {ResetText(now)} zurückgesetzt.";

    public static string UsedUp(DateTime now) =>
        $"Dein Tageskontingent an Seiten ist aufgebraucht. Es wird {ResetText(now)} zurückgesetzt.";

    private static string Pages(int count) => count == 1 ? "Seite" : "Seiten";
}