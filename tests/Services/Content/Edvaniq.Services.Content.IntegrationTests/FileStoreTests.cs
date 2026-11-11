using Edvaniq.Services.Content.Infrastructure.Files;

using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Content.IntegrationTests;

// The file store alone, without a database: whatever name comes in, no path leaves the directory.
public sealed class FileStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "edvaniq-file-store", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("../escape")]
    [InlineData("..")]
    [InlineData("sub/file")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("")]
    public void PathIn_RefusesANameThatLeadsOutOfTheRoot(string name) =>
        Assert.Throws<ArgumentException>(() => FileStore.PathIn(root, name));

    [Fact]
    public void PathOf_StaysInTheRoot_WithOrWithoutATrailingSeparator()
    {
        var id = Guid.NewGuid();

        foreach (var directory in new[] { root, root + Path.DirectorySeparatorChar })
        {
            var store = new FileStore(Options.Create(new FileStorageOptions { Directory = directory }));
            Assert.Equal(Path.Combine(Path.GetFullPath(root), id.ToString("N")), store.PathOf(id));
        }
    }

    [Fact]
    public async Task Write_NeverOverwritesAFile()
    {
        var store = new FileStore(Options.Create(new FileStorageOptions { Directory = root }));
        var id = Guid.NewGuid();
        await store.WriteAsync(id, new MemoryStream([1, 2, 3]), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IOException>(() => store.WriteAsync(id, new MemoryStream([4]), TestContext.Current.CancellationToken));

        Assert.Equal([1, 2, 3], File.ReadAllBytes(store.PathOf(id)));
    }

    [Fact]
    public void OpenRead_ReturnsNullForAMissingFile()
    {
        var store = new FileStore(Options.Create(new FileStorageOptions { Directory = root }));

        Assert.Null(store.OpenRead(Guid.NewGuid()));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}