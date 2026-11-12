using System.Globalization;

using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Content.Domain;

// One generation a topic took from its owner's daily quota: a job of the AI that makes learning content for the topic,
// today its question pool. One per topic, so a retry takes nothing twice. It stays when the document is deleted, only
// the deletion of the user takes it.
public sealed class GenerationCharge : IOwnedByUser
{
    private GenerationCharge(string ownerId, Guid documentId, Guid topicId, DateOnly studyDay, DateTime chargedAt)
    {
        OwnerId = ownerId;
        DocumentId = documentId;
        TopicId = topicId;
        StudyDay = studyDay;
        ChargedAt = chargedAt;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string OwnerId { get; private set; }

    // Content's document and Knowledge's topic, only references.
    public Guid DocumentId { get; private set; }

    public Guid TopicId { get; private set; }

    public DateOnly StudyDay { get; private set; }

    // UTC.
    public DateTime ChargedAt { get; private set; }

    public static GenerationCharge For(Document document, Guid topicId, DateTime now) =>
        new(document.OwnerId, document.Id, topicId, SharedKernel.StudyDay.Of(now), now);
}

// What the user reads when a document needs more generations than are left today (docs/service-internals.md).
public static class GenerationQuota
{
    public const int DefaultGenerationsPerDay = 20;

    // {0} topics, {1} "Thema" or "Themen", {2} "ist" or "sind", {3} generations left, {4} when the quota starts again.
    public const string TooManyReason =
        "Dein Tageskontingent an Generierungen reicht für dieses Dokument nicht: {0} {1}, frei {2} heute noch {3}. Es wird {4} zurückgesetzt.";

    // {0} when the quota starts again.
    public const string UsedUpReason = "Dein Tageskontingent an Generierungen ist aufgebraucht. Es wird {0} zurückgesetzt.";

    public static string TooMany(int topics, int left, DateTime now) =>
        string.Format(
            CultureInfo.InvariantCulture, TooManyReason, topics, topics == 1 ? "Thema" : "Themen", left == 1 ? "ist" : "sind", left, PageQuota.ResetText(now));

    public static string UsedUp(DateTime now) => string.Format(CultureInfo.InvariantCulture, UsedUpReason, PageQuota.ResetText(now));
}