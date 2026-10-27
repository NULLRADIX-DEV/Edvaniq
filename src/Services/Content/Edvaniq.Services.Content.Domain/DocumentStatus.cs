namespace Edvaniq.Services.Content.Domain;

// Where a document stands in its analysis (docs/spec/glossary.md#dokument). Stored as text, so keep the names.
public enum DocumentStatus
{
    Uploaded,
    Analyzing,
    Ready,
    Failed,
}
