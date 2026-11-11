using System.Security.Claims;

using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Services.Content.Infrastructure.Files;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Content.IntegrationTests;

// The uploaded files: stored under their own id, only for their owner, still there after a restart, and nothing stays
// that no row points at.
public sealed class FileStorageTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Owner_GetsTheSameBytesBack()
    {
        var alice = NewUser("alice");
        var bytes = SomeBytes();

        var file = await SaveAsync(service, alice, bytes);

        Assert.Equal(bytes.Length, file.Length);
        Assert.Equal(bytes, await ReadAsync(service, alice, file.Id));
    }

    // A foreign file looks exactly like one that doesn't exist, and stays where it is.
    [Fact]
    public async Task OtherUser_CanNeitherOpenNorDeleteAForeignFile()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        var bytes = SomeBytes();
        var file = await SaveAsync(service, alice, bytes);

        Assert.Null(await ReadAsync(service, bob, file.Id));
        Assert.False(await AsUser(service, bob, files => files.DeleteAsync(file.Id, Cancel)));
        Assert.Null(await ReadAsync(service, bob, Guid.NewGuid()));

        Assert.Equal(bytes, await ReadAsync(service, alice, file.Id));
    }

    // "Alice" and "alice" are two users, the id is compared byte for byte.
    [Fact]
    public async Task OtherUser_WithTheSameNameInOtherCase_CannotOpenTheFile()
    {
        var id = Guid.NewGuid().ToString("N");
        var file = await SaveAsync(service, $"alice-{id}", SomeBytes());

        Assert.Null(await ReadAsync(service, $"Alice-{id}", file.Id));
    }

    [Fact]
    public async Task Owner_DeletesTheFile_BytesAndRowAreGone()
    {
        var alice = NewUser("alice");
        var file = await SaveAsync(service, alice, SomeBytes());

        Assert.True(await AsUser(service, alice, files => files.DeleteAsync(file.Id, Cancel)));

        Assert.Null(await ReadAsync(service, alice, file.Id));
        Assert.False(File.Exists(PathOf(file.Id)));
        Assert.Equal(0, Query(db => db.StoredFiles.IgnoreQueryFilters().Count(item => item.Id == file.Id)));
    }

    // The file lies under the id Content gave it, nothing else names it.
    [Fact]
    public async Task File_IsStoredUnderItsIdOnly()
    {
        var file = await SaveAsync(service, NewUser("alice"), SomeBytes());

        Assert.Equal(file.Length, new FileInfo(PathOf(file.Id)).Length);
        Assert.False(File.Exists(PathOf(file.Id) + ".part"));
    }

    // A new host on the same database and directory, as after a restart or a deployment.
    [Fact]
    public async Task File_SurvivesARestart()
    {
        var alice = NewUser("alice");
        var bytes = SomeBytes();
        Guid id;
        await using (var before = service.WithWebHostBuilder(_ => { }))
        {
            id = (await SaveAsync(before, alice, bytes)).Id;
        }

        await using var after = service.WithWebHostBuilder(_ => { });

        Assert.Equal(bytes, await ReadAsync(after, alice, id));
    }

    // Only old files without a row and old leftovers of an upload go. Young ones may still get their row, and what isn't
    // named like an id isn't the store's.
    [Fact]
    public async Task Sweep_RemovesOnlyOldFilesWithoutRow()
    {
        var kept = await SaveAsync(service, NewUser("alice"), SomeBytes());
        var old = DateTime.UtcNow - FileSweeper.MinAge - TimeSpan.FromMinutes(5);
        File.SetLastWriteTimeUtc(PathOf(kept.Id), old);
        var orphan = WriteRaw(Guid.NewGuid().ToString("N"), old);
        var part = WriteRaw(Guid.NewGuid().ToString("N") + ".part", old);
        var young = WriteRaw(Guid.NewGuid().ToString("N"), DateTime.UtcNow);
        var foreign = WriteRaw("notes.txt", old);
        var upper = WriteRaw(Guid.NewGuid().ToString("N").ToUpperInvariant(), old);

        await service.Services.GetServices<IHostedService>().OfType<FileSweeper>().Single().SweepAsync(Cancel);

        Assert.False(File.Exists(orphan));
        Assert.False(File.Exists(part));
        Assert.True(File.Exists(young));
        Assert.True(File.Exists(foreign));
        Assert.True(File.Exists(upper));
        Assert.True(File.Exists(PathOf(kept.Id)));
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private static byte[] SomeBytes() => [.. Guid.NewGuid().ToByteArray(), .. "%PDF-1.7 not really"u8.ToArray()];

    private string PathOf(Guid id) => Path.Combine(service.FilesDirectory, id.ToString("N"));

    private string WriteRaw(string name, DateTime writtenAt)
    {
        var path = Path.Combine(service.FilesDirectory, name);
        File.WriteAllBytes(path, SomeBytes());
        File.SetLastWriteTimeUtc(path, writtenAt);
        return path;
    }

    private static Task<Domain.StoredFile> SaveAsync(WebApplicationFactory<Program> host, string user, byte[] bytes) =>
        AsUser(host, user, files => files.SaveAsync(new MemoryStream(bytes), Cancel));

    private static Task<byte[]?> ReadAsync(WebApplicationFactory<Program> host, string user, Guid id) =>
        AsUser(host, user, async files =>
        {
            await using var stream = await files.OpenAsync(id, Cancel);
            if (stream is null)
            {
                return null;
            }

            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, Cancel);
            return copy.ToArray();
        });

    // IFiles of a request by the user, as if their token had come in.
    private static async Task<T> AsUser<T>(WebApplicationFactory<Program> host, string user, Func<IFiles, Task<T>> act)
    {
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user)], "test")),
        };
        return await act(scope.ServiceProvider.GetRequiredService<IFiles>());
    }

    private T Query<T>(Func<ContentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ContentDbContext>());
    }
}