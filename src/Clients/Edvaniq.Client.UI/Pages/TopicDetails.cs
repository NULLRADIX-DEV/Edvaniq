using System.Globalization;

using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;

namespace Edvaniq.Client.UI.Pages;

// The key terms of a topic and where its material stands, by document.
public sealed record TopicDetails(
    CallResult Result,
    IReadOnlyList<KeyTermInfo> Terms,
    IReadOnlyList<SourceGroup> Sources,
    string? TraceId = null)
{
    public static TopicDetails Failed(string? traceId) => new(CallResult.Failed, [], [], traceId);

    public static readonly TopicDetails SignedOut = new(CallResult.SignedOut, [], []);
}

// The sections of one document, in their order: pages for a file, the section's number for a pasted text.
public sealed record SourceGroup(string DocumentTitle, IReadOnlyList<string> Places);

// Loads the details of a topic when it opens. Knowledge names the sections, Content where they stand. Each document is
// asked once while the page lives, a new visit asks anew. A section of a document that is gone meanwhile is left out.
public sealed class TopicDetailsLoader(ITopicDirectory directory, IReadOnlyList<DocumentInfo> documents)
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private readonly Dictionary<Guid, Task<CallOutcome<IReadOnlyList<DocumentSectionInfo>>>> sections = [];

    public async Task<TopicDetails> LoadAsync(Guid topicId, CancellationToken cancellationToken)
    {
        var termsCall = directory.TermsAsync(topicId, cancellationToken);
        var sectionsCall = directory.TopicSectionsAsync(topicId, cancellationToken);
        var terms = await termsCall;
        var topicSections = await sectionsCall;

        if (terms.Result == CallResult.SignedOut || topicSections.Result == CallResult.SignedOut)
        {
            return TopicDetails.SignedOut;
        }

        if (terms.Result != CallResult.Ok)
        {
            return TopicDetails.Failed(terms.TraceId);
        }

        if (topicSections.Result != CallResult.Ok)
        {
            return TopicDetails.Failed(topicSections.TraceId);
        }

        var groups = new List<SourceGroup>();
        foreach (var document in documents)
        {
            var wanted = topicSections.Value!.Where(section => section.DocumentId == document.Id).Select(section => section.SectionId).ToHashSet();
            if (wanted.Count == 0)
            {
                continue;
            }

            var where = await SectionsOfAsync(document.Id, cancellationToken);
            switch (where.Result)
            {
                case CallResult.Ok:
                    var places = where.Value!
                        .Where(section => wanted.Contains(section.SectionId))
                        .OrderBy(section => section.Position)
                        .Select(Place)
                        .ToList();
                    if (places.Count > 0)
                    {
                        groups.Add(new SourceGroup(document.Title, places));
                    }

                    break;
                case CallResult.NotFound:
                    break;
                case CallResult.SignedOut:
                    return TopicDetails.SignedOut;
                default:
                    return TopicDetails.Failed(where.TraceId);
            }
        }

        return new TopicDetails(CallResult.Ok, terms.Value!, groups);
    }

    public static string Place(DocumentSectionInfo section) => section switch
    {
        { PageFrom: { } from, PageTo: { } to } when to > from => string.Format(German, FeedbackTexts.TopicDirectory.Pages, from, to),
        { PageFrom: { } page } => string.Format(German, FeedbackTexts.TopicDirectory.Page, page),
        _ => string.Format(German, FeedbackTexts.TopicDirectory.Section, section.Position + 1),
    };

    // Only an answer that came through is kept, a failed one is asked again with the next topic or retry.
    private async Task<CallOutcome<IReadOnlyList<DocumentSectionInfo>>> SectionsOfAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (!sections.TryGetValue(documentId, out var call))
        {
            call = directory.DocumentSectionsAsync(documentId, cancellationToken);
            sections[documentId] = call;
        }

        var outcome = await call;
        if (outcome.Result is not (CallResult.Ok or CallResult.NotFound))
        {
            sections.Remove(documentId);
        }

        return outcome;
    }
}