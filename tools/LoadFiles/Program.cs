using System.Text;

using Edvaniq.Services.Content.IntegrationTests;
using Edvaniq.Services.Content.UnitTests;

// The files for measuring the worker at its limits (#321), all of them just below what Content takes: a PDF with as many
// pages as a document may have, a text as long as a pasted one may be, and a Word file close to 20 MB. Nothing in them is
// real material, the analysis runs on the simulated AI.
//
//   dotnet run --project tools/LoadFiles -- [folder]
//
// Without a folder they go to a temp folder, never into the repo.
const int MaxPages = 100;
const int MaxText = 200_000;
const long MaxFile = 20L * 1024 * 1024;

var folder = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "edvaniq-load-files");
Directory.CreateDirectory(folder);

Write("limit.pdf", TestPdfs.AtTheLimit(MaxPages));
Write("text-200k.txt", Encoding.UTF8.GetBytes(Text(MaxText - 1_000)));

// A picture nobody reads makes up the size, the text stays below the limit after extraction.
var paragraphs = Chapters(MaxText - 50_000).Select(part => part.Heading
    ? TestDocx.P(part.Line, "berschrift1")
    : TestDocx.P(part.Line));
var body = string.Concat(paragraphs);
var bare = TestDocx.Of(body).LongLength;
var picture = new byte[MaxFile - bare - (256 * 1024)];
new Random(321).NextBytes(picture);
Write("limit.docx", TestDocx.Of(body, parts: new Dictionary<string, byte[]> { ["word/media/image1.png"] = picture }));

void Write(string name, byte[] content)
{
    var path = Path.Combine(folder, name);
    File.WriteAllBytes(path, content);
    Console.WriteLine($"{path}: {content.LongLength:N0} bytes");
}

static string Text(int length) =>
    string.Join("\n", Chapters(length).Select(part => part.Heading ? $"\n# {part.Line}" : part.Line));

// Chapters of a few paragraphs each until the text has about length characters.
static IEnumerable<(bool Heading, string Line)> Chapters(int length)
{
    const string Paragraph =
        "Licht spaltet in den Blättern Wasser, dabei entsteht Sauerstoff. Die Energie wird als ATP gespeichert und "
        + "im Calvin-Zyklus genutzt, um aus Kohlenstoffdioxid Zucker aufzubauen. Ohne Licht kommt dieser Kreislauf "
        + "nach kurzer Zeit zum Stillstand.";
    var written = 0;
    for (var chapter = 1; written < length; chapter++)
    {
        var heading = $"Kapitel {chapter}";
        written += heading.Length + 3;
        yield return (true, heading);
        for (var number = 1; number <= 4 && written < length; number++)
        {
            var line = $"{Paragraph} (Abschnitt {chapter}.{number})";
            written += line.Length + 1;
            yield return (false, line);
        }
    }
}