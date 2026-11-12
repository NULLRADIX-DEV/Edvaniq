using Edvaniq.Services.Content.Domain;

namespace Edvaniq.Services.Content.Infrastructure.Files;

// The reading process: the worker started once more with "read-file <path> <format>", before any host is built. It
// knows no database, no broker and no secret, opens only the one file, read only, and answers on its standard output
// (FileReadProtocol). Its memory is capped by the worker that starts it, a file that wants more ends here and not in the
// worker.
public static class FileReadCommand
{
    public const string Name = "read-file";

    // Always 0 once an answer is written, the answer tells how it went.
    public static int Run(string path, string format, Stream output)
    {
        if (!Enum.TryParse<FileFormat>(format, ignoreCase: false, out var fileFormat))
        {
            FileReadProtocol.WriteRefusal(output, FileReadOutcome.Unreadable);
            return 0;
        }

        try
        {
            IReadOnlyList<string> pages;
            using (var content = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                pages = FileText.PagesOf(content, fileFormat, CancellationToken.None);
            }

            FileReadProtocol.WritePages(output, pages);
        }
        catch (FileRefusedException refused)
        {
            FileReadProtocol.WriteRefusal(output, FileReadProtocol.OutcomeOf(refused.Reason));
        }
        catch (OutOfMemoryException)
        {
            // The pages are gone with the stack, there's room again for the few bytes of the answer.
            FileReadProtocol.WriteRefusal(output, FileReadOutcome.TooLarge);
        }
        catch (Exception)
        {
            // Nothing of the exception goes out, its message could quote the file.
            FileReadProtocol.WriteRefusal(output, FileReadOutcome.Unreadable);
        }

        return 0;
    }
}