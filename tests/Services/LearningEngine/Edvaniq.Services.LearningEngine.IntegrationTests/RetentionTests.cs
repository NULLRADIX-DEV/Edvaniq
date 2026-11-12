using System.Net;
using System.Net.Http.Headers;

using Edvaniq.BuildingBlocks.Infrastructure;
using Edvaniq.Services.LearningEngine.Domain;
using Edvaniq.Services.LearningEngine.Infrastructure;
using Edvaniq.Testing;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Edvaniq.Services.LearningEngine.IntegrationTests;

// #365: with the retention cut to one month, a run deletes the records older than that and the answers of /mastery,
// /mastery/{id} and /mastery/decayed stay exactly the same (AC2, AC3). Portions of two records, so one topic takes
// several. The clock stands still, so before and after are the same moment.
public sealed class RetentionTests(RetentionTests.ShortRetention service) : IClassFixture<RetentionTests.ShortRetention>
{
    private static DateTime Now => ShortRetention.Clock.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task ExpiredRecords_GoInPortions_AndMasteryStaysTheSame()
    {
        var (alice, bob) = (NewUser("alice"), NewUser("bob"));
        var goal = Guid.NewGuid();
        var (mixed, decayed) = (Guid.NewGuid(), Guid.NewGuid());
        var bobs = Guid.NewGuid();
        await SeedAsync(
            [Topic(alice, mixed, goal), Topic(alice, decayed, goal), Topic(bob, bobs, goal)],
            [
                Record(alice, mixed, Now.AddDays(-80), true, 1),
                Record(alice, mixed, Now.AddDays(-70), false, 2),
                Record(alice, mixed, Now.AddDays(-60), true, 3),
                Record(alice, mixed, Now.AddDays(-50), false, 2),
                Record(alice, mixed, Now.AddDays(-40), false, 1),
                Record(alice, mixed, Now.AddDays(-5), true, 2),
                Record(alice, mixed, Now.AddDays(-1), true, 3),
                .. Enumerable.Range(0, 4).Select(minute => Record(alice, decayed, Now.AddDays(-45).AddMinutes(minute), true, 2)),
                Record(bob, bobs, Now.AddDays(-90), false, 2),
                Record(bob, bobs, Now.AddDays(-2), true, 2),
            ]);
        var paths = new[] { $"/mastery?learningGoalId={goal}", $"/mastery/{mixed}", $"/mastery/{decayed}", $"/mastery/decayed?learningGoalId={goal}" };
        var before = await AnswersAsync(alice, paths);
        var bobBefore = await AnswersAsync(bob, [$"/mastery/{bobs}"]);
        Assert.Contains(decayed.ToString(), before[3], StringComparison.Ordinal);

        Retention().RunOnce(TestContext.Current.CancellationToken);

        Assert.Equal(before, await AnswersAsync(alice, paths));
        Assert.Equal(bobBefore, await AnswersAsync(bob, [$"/mastery/{bobs}"]));
        Assert.Equal(2, Query(db => Records(db, alice).Count()));
        Assert.All(Query(db => Records(db, alice).ToList()), record => Assert.True(record.AnsweredAt >= Now.AddMonths(-1)));
        Assert.Equal(1, Query(db => Records(db, bob).Count()));

        var carries = Query(db => Carries(db, alice).ToList()).OrderBy(carry => carry.TopicId).ToList();
        Assert.Equal(new[] { mixed, decayed }.Order(), carries.Select(carry => carry.TopicId));
        Assert.Equal(3, carries.Single(carry => carry.TopicId == mixed).Version);
        Assert.All(carries, carry => Assert.Equal(Mastery.RuleVersion, carry.RuleVersion));

        // A second run finds nothing more and leaves the carries as they are.
        Assert.Equal(0, Retention().RunOnce(TestContext.Current.CancellationToken));
        Assert.Equal(
            carries.Select(carry => carry.Version),
            Query(db => Carries(db, alice).ToList()).OrderBy(carry => carry.TopicId).Select(carry => carry.Version));
        Assert.Equal(before, await AnswersAsync(alice, paths));

        // Neither the run itself nor EF on its tables complains. The outbox's errors before the fixture migrates aren't its.
        Assert.DoesNotContain(
            service.Logs.Entries,
            entry => (entry.Category.Contains(nameof(LearningRecordRetention), StringComparison.Ordinal) && entry.Level >= LogLevel.Warning)
                || (entry.Category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                    && entry.Level >= LogLevel.Error
                    && entry.Text.Contains("MasteryCarries", StringComparison.Ordinal)));
    }

    // Nothing older than the retention: the run keeps every record and writes no carry.
    [Fact]
    public async Task NothingExpired_WritesNoCarry()
    {
        var carol = NewUser("carol");
        var topic = Guid.NewGuid();
        await SeedAsync(
            [Topic(carol, topic, Guid.NewGuid())],
            [Record(carol, topic, Now.AddDays(-20), true, 2), Record(carol, topic, Now.AddDays(-1), false, 2)]);

        Retention().RunOnce(TestContext.Current.CancellationToken);

        Assert.Equal(2, Query(db => Records(db, carol).Count()));
        Assert.Equal(0, Query(db => Carries(db, carol).Count()));
    }

    // Recent records, so no run of another test folds them meanwhile.
    // Two runs inserting the first carry of a topic at once: the second hits the carry's key, a clash.
    [Fact]
    public async Task SecondCarryOfATopic_IsAClash()
    {
        var dave = NewUser("dave");
        var record = Record(dave, Guid.NewGuid(), Now.AddDays(-1), true, 2);
        await SeedAsync([], [record]);
        Query(db =>
        {
            db.MasteryCarries.Add(MasteryCarry.Fold(null, dave, record.TopicId, [record], null));
            return db.SaveChanges();
        });

        var exception = Assert.Throws<DbUpdateException>(() => Query(db =>
        {
            db.MasteryCarries.Add(MasteryCarry.Fold(null, dave, record.TopicId, [record], null));
            return db.SaveChanges();
        }));

        Assert.True(LearningRecordRetention.IsClash(exception));
    }

    // Two runs folding into the same carry: the second fails on its token, a clash.
    [Fact]
    public async Task CarryChangedMeanwhile_IsAClash()
    {
        var erin = NewUser("erin");
        var topic = Guid.NewGuid();
        var (first, second) = (Record(erin, topic, Now.AddDays(-2), true, 2), Record(erin, topic, Now.AddDays(-1), false, 2));
        await SeedAsync([], [first, second]);
        Query(db =>
        {
            db.MasteryCarries.Add(MasteryCarry.Fold(null, erin, topic, [first], null));
            return db.SaveChanges();
        });

        using var scope = service.Services.CreateScope();
        var stale = scope.ServiceProvider.GetRequiredService<LearningEngineDbContext>();
        var carry = Carries(stale, erin).Single();
        Query(db =>
        {
            MasteryCarry.Fold(Carries(db, erin).Single(), erin, topic, [second], null);
            return db.SaveChanges();
        });
        MasteryCarry.Fold(carry, erin, topic, [second], null);

        Assert.True(LearningRecordRetention.IsClash(Assert.Throws<DbUpdateConcurrencyException>(() => stale.SaveChanges())));
    }

    // Any other failure on save, here a duplicate learning record, is no clash and goes up as a failure.
    [Fact]
    public async Task OtherSaveFailure_IsNoClash()
    {
        var frank = NewUser("frank");
        var record = Record(frank, Guid.NewGuid(), Now.AddDays(-1), true, 2);
        await SeedAsync([], [record]);

        var exception = Assert.Throws<DbUpdateException>(() => Query(db =>
        {
            db.LearningRecords.Add(LearningRecord.Create(
                record.AnswerId, frank, record.TopicId, Guid.NewGuid(), null, true, 2, 4200, null, Mode.Practice, Now));
            return db.SaveChanges();
        }));

        Assert.False(LearningRecordRetention.IsClash(exception));
    }

    // The fixture's run is a singleton, the hosted service is the same one.
    [Fact]
    public void TheRun_IsTheHostedService()
    {
        var hosted = service.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>();

        Assert.Contains(Retention(), hosted);
    }

    private LearningRecordRetention Retention() => service.Services.GetRequiredService<LearningRecordRetention>();

    private static string NewUser(string name) => $"{name}-{Guid.NewGuid():N}";

    private static KnownTopic Topic(string owner, Guid topicId, Guid learningGoalId) =>
        KnownTopic.Create(owner, topicId, learningGoalId, hidden: false);

    private static LearningRecord Record(string owner, Guid topicId, DateTime answeredAt, bool correct, int difficulty) =>
        LearningRecord.Create(Guid.NewGuid(), owner, topicId, Guid.NewGuid(), null, correct, difficulty, 4200, null, Mode.Practice, answeredAt);

    private static IQueryable<LearningRecord> Records(LearningEngineDbContext db, string owner) =>
        db.LearningRecords.IgnoreQueryFilters([ServiceDbContext.OwnerFilter]).Where(record => record.OwnerId == owner);

    private static IQueryable<MasteryCarry> Carries(LearningEngineDbContext db, string owner) =>
        db.MasteryCarries.IgnoreQueryFilters([ServiceDbContext.OwnerFilter]).Where(carry => carry.OwnerId == owner);

    private async Task SeedAsync(IEnumerable<KnownTopic> topics, IEnumerable<LearningRecord> records)
    {
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningEngineDbContext>();
        db.KnownTopics.AddRange(topics);
        db.LearningRecords.AddRange(records);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private T Query<T>(Func<LearningEngineDbContext, T> query)
    {
        using var scope = service.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<LearningEngineDbContext>());
    }

    // The bodies as the service sends them, so before and after compare byte for byte.
    private async Task<List<string>> AnswersAsync(string user, IEnumerable<string> paths)
    {
        using var client = service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
        var bodies = new List<string>();
        foreach (var path in paths)
        {
            using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            bodies.Add(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        return bodies;
    }

    // One month instead of twelve, portions of two, and a clock that stands still at the moment the fixture starts.
    public sealed class ShortRetention(MySqlTestServer mysql) : ServiceFactory(mysql)
    {
        public static readonly FixedClock Clock = new(DateTimeOffset.UtcNow);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder
                .UseSetting($"{LearningRecordRetentionOptions.Section}:Months", "1")
                .UseSetting($"{LearningRecordRetentionOptions.Section}:BatchSize", "2")
                .ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
        }
    }

    public sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}