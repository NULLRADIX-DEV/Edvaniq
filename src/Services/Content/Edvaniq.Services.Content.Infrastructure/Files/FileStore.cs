using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Content.Infrastructure.Files;

public sealed class FileStorageOptions
{
    public const string Section = "FileStorage";

    // Where the files live. The API writes there, the worker only reads.
    public string Directory { get; set; } = "";
}

// The bytes of the stored files, one file per id, flat in one directory. Knows nothing about owners: the rows in the
// database decide who gets which file. A path is only ever built from a Guid, and checked to stay in the directory.
public sealed class FileStore
{
    private const string PartSuffix = ".part";

    public FileStore(IOptions<FileStorageOptions> options)
    {
        Root = Path.GetFullPath(options.Value.Directory);
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string PathOf(Guid id) => PathIn(Root, id.ToString("N"));

    // The full path of the name in the root, or an exception if it would end up anywhere else.
    public static string PathIn(string root, string name)
    {
        var full = Path.GetFullPath(Path.Combine(root, name));
        if (!string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), StringComparison.Ordinal))
        {
            throw new ArgumentException("The name leads out of the file store.", nameof(name));
        }

        return full;
    }

    // Writes to a .part file first and renames it when complete, so a file under its id is always whole. Returns the
    // length in bytes. Never overwrites a file.
    public async Task<long> WriteAsync(Guid id, Stream content, CancellationToken cancellationToken)
    {
        var path = PathOf(id);
        var part = path + PartSuffix;
        try
        {
            long length;
            await using (var file = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await content.CopyToAsync(file, cancellationToken);
                await file.FlushAsync(cancellationToken);
                file.Flush(flushToDisk: true);
                length = file.Length;
            }

            File.Move(part, path, overwrite: false);
            return length;
        }
        catch
        {
            File.Delete(part);
            throw;
        }
    }

    // Null if there is no file with this id.
    public Stream? OpenRead(Guid id)
    {
        try
        {
            return new FileStream(PathOf(id), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    // A missing file is fine, deleting twice does no harm.
    public void Delete(Guid id)
    {
        var path = PathOf(id);
        File.Delete(path);
        File.Delete(path + PartSuffix);
    }

    // Every file whose name is an id, complete or still a .part, with when it was last written. Anything else in the
    // directory isn't the store's and stays out of it.
    public IEnumerable<StoredBytes> List()
    {
        foreach (var path in Directory.EnumerateFiles(Root))
        {
            var name = Path.GetFileName(path);
            var partial = name.EndsWith(PartSuffix, StringComparison.Ordinal);
            var stem = partial ? name[..^PartSuffix.Length] : name;

            // Exactly as PathOf writes it, an upper case twin would be another file.
            if (Guid.TryParseExact(stem, "N", out var id) && stem == id.ToString("N"))
            {
                yield return new StoredBytes(id, partial, File.GetLastWriteTimeUtc(path));
            }
        }
    }

    // Deletes only the .part file of the id.
    public void DeletePart(Guid id) => File.Delete(PathOf(id) + PartSuffix);
}

public sealed record StoredBytes(Guid Id, bool Partial, DateTime WrittenAt);