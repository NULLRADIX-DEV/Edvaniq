using Edvaniq.Services.Content.Infrastructure.Files;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Content.IntegrationTests;

// The reading process as the worker starts it: the real worker, built next to the tests (the project references it).
internal static class ReadingProcess
{
    public static string Worker => Path.Combine(AppContext.BaseDirectory, "Edvaniq.Services.Content.Worker.dll");

    public static FileReader Reader(FileStore store, TimeSpan? killAfter = null) =>
        new(
            store,
            Options.Create(new FileReadingOptions
            {
                Command = "dotnet",
                Assembly = Worker,
                KillAfter = killAfter ?? new FileReadingOptions().KillAfter,
            }),
            NullLogger<FileReader>.Instance);
}