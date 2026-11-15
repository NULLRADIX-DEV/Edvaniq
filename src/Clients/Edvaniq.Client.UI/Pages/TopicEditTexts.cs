using Edvaniq.Client.Core;
using Edvaniq.Client.UI.Feedback;

namespace Edvaniq.Client.UI.Pages;

public enum TopicEditKind
{
    Renamed,
    SubtopicAdded,
}

// A change TopicTools made, for the directory to load anew and to say.
public sealed record TopicEdited(TopicEditKind Kind, EditedTopic Topic);

public static class TopicEditTexts
{
    // What a refusal of Knowledge means for the user. A name that is taken stands at the field instead.
    public static string ForConflict(TopicConflict? conflict) => conflict switch
    {
        TopicConflict.NameTaken => FeedbackTexts.TopicEditing.NameTaken,
        TopicConflict.ParentHidden => FeedbackTexts.TopicEditing.ParentHidden,
        _ => FeedbackTexts.TopicEditing.Busy,
    };

    // {0} is the topic. Its subtopics go along, the message counts them.
    public static string Hidden(string name, int subtopics) => subtopics switch
    {
        0 => string.Format(System.Globalization.CultureInfo.InvariantCulture, FeedbackTexts.TopicEditing.HiddenOne, name),
        1 => string.Format(System.Globalization.CultureInfo.InvariantCulture, FeedbackTexts.TopicEditing.HiddenWithOneSubtopic, name),
        _ => string.Format(System.Globalization.CultureInfo.InvariantCulture, FeedbackTexts.TopicEditing.HiddenWithSubtopics, name, subtopics),
    };

    public static string With(string text, string name) => string.Format(System.Globalization.CultureInfo.InvariantCulture, text, name);
}