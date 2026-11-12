using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.BuildingBlocks.Messaging;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure;
using Edvaniq.Services.Identity.Contracts;
using Edvaniq.Testing;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Content.IntegrationTests;

// UserDeleted removes all of the user's documents and nobody else's.
public sealed class UserDeletionTests(ServiceFactory service) : IClassFixture<ServiceFactory>
{
    private const string Consumer = $"{InfrastructureExtensions.ServiceKey}-UserDeletedHandler";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private InMemoryTransport Broker => service.Services.GetRequiredService<InMemoryTransport>();

    [Fact]
    public async Task DeletedUser_LosesAllTheirData_OthersKeepTheirs()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        await CreateItemAsync(alice, "Notiz eins");
        await CreateItemAsync(alice, "Notiz zwei");
        await CreateItemAsync(bob, "Notiz von Bob");

        var messageId = await DeleteAsync(alice);

        await WaitForAsync(() => InboxEntries(messageId) == 1, "The deletion was not handled.");
        Assert.Equal(0, ItemsOf(alice));
        Assert.Equal(1, ItemsOf(bob));
    }

    // Every table with an owner holds rows of both users. A new table without a seed here fails before the deletion.
    [Fact]
    public async Task DeletedUser_HasNoRowInAnyTable_OthersKeepTheirs()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        await SeedEveryTableAsync(alice);
        await SeedEveryTableAsync(bob);
        Assert.Empty(OwnedRows.Unseeded(Query(db => OwnedRows.Of(db, alice))));
        var ofBob = Query(db => OwnedRows.Of(db, bob));

        var messageId = await DeleteAsync(alice);

        await WaitForAsync(() => InboxEntries(messageId) == 1, "The deletion was not handled.");
        Assert.All(Query(db => OwnedRows.Of(db, alice)), table => Assert.Equal(0, table.Value));
        Assert.Equal(ofBob, Query(db => OwnedRows.Of(db, bob)));
    }

    private async Task SeedEveryTableAsync(string user)
    {
        await CreateItemAsync(user, "Notiz");
        var fileId = await SaveFileAsync(user);
        await AiUsageSeed.AddAsync(service.Services, user, AiUsageSeed.SomeDay(), (1, 0.25m));
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        var document = Document.Paste(user, Guid.NewGuid(), null, "Licht spaltet Wasser in der Lichtreaktion der Photosynthese.", DateTime.UtcNow);
        db.Documents.Add(document);
        db.Sections.AddRange(document.Split());
        db.QuestionPools.Add(QuestionPool.For(document, Guid.NewGuid(), DateTime.UtcNow));
        var fromFile = Document.Upload(user, Guid.NewGuid(), "Notizen.txt", fileId, FileFormat.Text, DateTime.UtcNow);
        db.Documents.Add(fromFile);
        db.DocumentPages.AddRange(fromFile.TakeText(["Licht spaltet Wasser in der Lichtreaktion."], DateTime.UtcNow));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    // The bytes in the file store go too, not only the rows.
    [Fact]
    public async Task DeletedUser_LosesTheirFiles_OthersKeepTheirs()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        var first = await SaveFileAsync(alice);
        var second = await SaveFileAsync(alice);
        var bobs = await SaveFileAsync(bob);

        var messageId = await DeleteAsync(alice);

        await WaitForAsync(() => InboxEntries(messageId) == 1, "The deletion was not handled.");
        Assert.False(File.Exists(FilePath(first)));
        Assert.False(File.Exists(FilePath(second)));
        Assert.Equal(0, Query(db => db.StoredFiles.IgnoreQueryFilters().Count(file => file.OwnerId == alice)));
        Assert.True(File.Exists(FilePath(bobs)));
        Assert.Equal(1, Query(db => db.StoredFiles.IgnoreQueryFilters().Count(file => file.OwnerId == bob)));
    }

    // #944: a request that checked before the deletion and saved after it leaves a document with a file. The sweep runs
    // the whole deletion again, so the bytes go too.
    [Fact]
    public async Task Sweep_RemovesADocumentAndFileStoredAfterTheDeletion()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        var bobs = await SaveFileAsync(bob);
        var messageId = await DeleteAsync(alice);
        await WaitForAsync(() => InboxEntries(messageId) == 1, "The deletion was not handled.");
        await CreateItemAsync(alice, "Notiz danach");
        var late = await SaveFileAsync(alice);
        var hash = DeletedUser.HashOf(alice);
        Query(db => db.Set<DeletedUser>().Where(user => user.Hash == hash)
            .ExecuteUpdate(set => set.SetProperty(user => user.DeletedAt, DateTime.UtcNow.AddMinutes(-10))));

        await service.Services.GetServices<IHostedService>().OfType<DeletedUserSweep<ContentDbContext>>().Single()
            .RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, ItemsOf(alice));
        Assert.False(File.Exists(FilePath(late)));
        Assert.Equal(0, Query(db => db.StoredFiles.IgnoreQueryFilters().Count(file => file.OwnerId == alice)));
        Assert.True(File.Exists(FilePath(bobs)));
        Assert.True(Query(db => db.Set<DeletedUser>().Single(user => user.Hash == hash).Swept));
    }

    // "Alice" and "alice" are two users, deleting one keeps the data of the other.
    [Fact]
    public async Task DeletedUser_KeepsTheDataOfTheSameNameInOtherCase()
    {
        var id = Guid.NewGuid().ToString("N");
        var lower = $"alice-{id}";
        var upper = $"Alice-{id}";
        await CreateItemAsync(lower, "Notiz klein");
        await CreateItemAsync(upper, "Notiz gross");

        var messageId = await DeleteAsync(upper);

        await WaitForAsync(() => InboxEntries(messageId) == 1, "The deletion was not handled.");
        Assert.Equal(0, ItemsOf(upper));
        Assert.Equal(1, ItemsOf(lower));
    }

    // A redelivery and a second deletion of the same user both do nothing.
    [Fact]
    public async Task DeletionDeliveredTwice_OrSentTwice_DoesNoHarm()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        await CreateItemAsync(alice, "Notiz");
        await CreateItemAsync(bob, "Notiz von Bob");
        Broker.DropsDuplicates = false;
        try
        {
            var messageId = Guid.NewGuid();
            await DeleteAsync(alice, messageId);
            await DeleteAsync(alice, messageId);
            var again = await DeleteAsync(alice);

            // One consumer, in order: once the second deletion is through, so is the duplicate.
            await WaitForAsync(() => InboxEntries(again) == 1, "The second deletion was not handled.");
            Assert.Equal(1, InboxEntries(messageId));
            Assert.Equal(0, ItemsOf(alice));
            Assert.Equal(1, ItemsOf(bob));
        }
        finally
        {
            Broker.DropsDuplicates = true;
        }
    }

    // Proof per service: the inbox entry and a log line after the commit with the deleting request's TraceId.
    [Fact]
    public async Task Deletion_IsTraceable_ByItsInboxEntryAndALogLineWithTheTraceIdOfTheRequest()
    {
        var alice = NewUser("alice");
        await CreateItemAsync(alice, "Notiz");
        var traceId = ActivityTraceId.CreateRandom().ToHexString();

        var messageId = await DeleteAsync(alice, traceParent: $"00-{traceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01");

        await WaitForAsync(
            () => service.Logs.Entries.Any(entry => entry.Message == $"Consumer {Consumer} has handled message {messageId}" && entry.TraceId == traceId),
            "No log line says with the TraceId of the request that the deletion was handled.");
        Assert.Equal(1, InboxEntries(messageId));
        Assert.Contains(service.Logs.Entries, entry => entry.Message == "Rows of a deleted user to remove: 1" && entry.TraceId == traceId);
    }

    // The AI costs of a deleted user stay only as daily sums without user or document: the day's total is the same
    // before and after, the user's attempts are gone, and nobody else's change.
    [Fact]
    public async Task DeletedUser_LeavesTheirAiCostsOnlyAsDailySums()
    {
        var alice = NewUser("alice");
        var bob = NewUser("bob");
        var day = AiUsageSeed.SomeDay();
        await AiUsageSeed.AddAsync(service.Services, alice, day, (1, 0.25m), (2, null));
        await AiUsageSeed.AddAsync(service.Services, alice, day.AddDays(1), (1, 0.5m));
        await AiUsageSeed.AddAsync(service.Services, bob, day, (1, 1m));
        var before = await PerDayAsync(day);

        var messageId = await DeleteAsync(alice);

        await WaitForAsync(() => InboxEntries(messageId) == 1, "The deletion was not handled.");
        Assert.Equal(before, await PerDayAsync(day));
        Assert.Equal(new AiUsageTotal(2, 3, 30, 9, 1.25m, 1, 4500), before[day]);
        Assert.Equal(0, Query(db => db.AiAttempts.IgnoreQueryFilters().Count(attempt => attempt.OwnerId == alice)));
        Assert.Equal(1, Query(db => db.AiAttempts.IgnoreQueryFilters().Count(attempt => attempt.OwnerId == bob)));
        var first = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var second = first.AddDays(1);
        Assert.Equal(
            [(first, 1, 2, 0.25m, 1), (second, 1, 1, 0.5m, 0)],
            Query(db => db.AiUsageDays.Where(sum => sum.Day == first || sum.Day == second).OrderBy(sum => sum.Day).ToList())
                .Select(sum => (sum.Day, sum.Calls, sum.Attempts, sum.Cost, sum.UnpricedAttempts)));
    }

    // Per day over both days, the attempts of existing users and the sums of deleted ones together.
    private async Task<Dictionary<DateOnly, AiUsageTotal>> PerDayAsync(DateOnly day)
    {
        using var scope = service.Services.CreateScope();
        var days = await scope.ServiceProvider.GetRequiredService<AiUsageReport>().PerDayAsync(day, day.AddDays(1), TestContext.Current.CancellationToken);
        return days.ToDictionary();
    }

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private async Task CreateItemAsync(string user, string text)
    {
        using var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));

        using var response = await client.PostAsJsonAsync("/documents", new { text = TestTexts.Note(text) }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // As the user's request would, through IFiles.
    private async Task<Guid> SaveFileAsync(string user)
    {
        using var scope = service.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user)], "test")),
        };
        var file = await scope.ServiceProvider.GetRequiredService<IFiles>()
            .SaveAsync(new MemoryStream([1, 2, 3]), TestContext.Current.CancellationToken);
        return file.Id;
    }

    private string FilePath(Guid id) => Path.Combine(service.FilesDirectory, id.ToString("N"));

    // Like Identity's dispatcher would.
    private async Task<Guid> DeleteAsync(string user, Guid? messageId = null, string? traceParent = null)
    {
        var id = messageId ?? Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new UserDeleted(user), JsonSerializerOptions.Web);
        await Broker.PublishAsync(
            new OutgoingMessage(EventRoute.For(typeof(UserDeleted)).Subject, id, payload, traceParent),
            TestContext.Current.CancellationToken);
        return id;
    }

    private int ItemsOf(string user) =>
        Query(db => db.Documents.IgnoreQueryFilters().Count(document => document.OwnerId == user));

    private int InboxEntries(Guid messageId) =>
        Query(db => db.Set<InboxMessage>().Count(entry => entry.MessageId == messageId && entry.Consumer == Consumer));

    private T Query<T>(Func<ContentDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ContentDbContext>());
    }

    // The consumer runs in the background.
    private static async Task WaitForAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, because);
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }
}