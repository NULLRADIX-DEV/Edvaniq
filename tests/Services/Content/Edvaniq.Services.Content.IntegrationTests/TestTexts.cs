namespace Edvaniq.Services.Content.IntegrationTests;

// A text with enough words to get past Document.CheckText, for tests that only need some document.
internal static class TestTexts
{
    public const string Lesson = "Licht spaltet in den Blättern Wasser, dabei entsteht Sauerstoff für die Luft.";

    // A note to tell documents apart, as the first line and so the title, with a lesson below.
    public static string Note(string note) => $"{note}\n{Lesson}";
}