using Edvaniq.BuildingBlocks.Domain;

namespace Edvaniq.Services.Content.Domain;

// A usable answer of the AI, kept so the same request of the same user is answered from here instead of paying again
// (#306): a retry, a document waiting after an outage, or the same material a second time. Key covers everything the
// answer depends on, so other material, another level, language, prompt or model never meets an old answer. The answer
// holds what the AI made of the material, so it goes with the document that asked for it, and with the user.
public sealed class AiAnswer : IOwnedByUser
{
    public const int KeyLength = 64;

    private AiAnswer(
        string ownerId, Guid documentId, string key, string purpose, string promptVersion, string model, string answer, DateTime createdAt)
    {
        OwnerId = ownerId;
        DocumentId = documentId;
        Key = key;
        Purpose = purpose;
        PromptVersion = promptVersion;
        Model = model;
        Answer = answer;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string OwnerId { get; private set; }

    // The document that asked first.
    public Guid DocumentId { get; private set; }

    // SHA-256 in hex over purpose, prompt version, model and the messages of the request.
    public string Key { get; private set; }

    public string Purpose { get; private set; }

    public string PromptVersion { get; private set; }

    // The model that answered.
    public string Model { get; private set; }

    // The text of the answer as it came, read again on every use.
    public string Answer { get; private set; }

    // UTC.
    public DateTime CreatedAt { get; private set; }

    public static AiAnswer Of(Document document, string key, string purpose, string promptVersion, string model, string answer, DateTime createdAt) =>
        new(document.OwnerId, document.Id, key, Cut(purpose, AiAttempt.PurposeMaxLength), Cut(promptVersion, AiAttempt.PromptVersionMaxLength),
            Cut(model, AiAttempt.ModelMaxLength), answer, createdAt);

    private static string Cut(string value, int length) => value.Length <= length ? value : value[..length];
}