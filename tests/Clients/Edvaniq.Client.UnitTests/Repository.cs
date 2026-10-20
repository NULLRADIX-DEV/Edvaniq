namespace Edvaniq.Client.UnitTests;

// The root of the repository, found by walking up from the test's output folder to Edvaniq.slnx.
internal static class Repository
{
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Edvaniq.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Edvaniq.slnx not found above " + AppContext.BaseDirectory);
    }
}