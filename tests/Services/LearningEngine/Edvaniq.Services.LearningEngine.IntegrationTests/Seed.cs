using Edvaniq.Services.LearningEngine.Domain;
using Edvaniq.Services.LearningEngine.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

namespace Edvaniq.Services.LearningEngine.IntegrationTests;

// Learning records only come from events, the API has no way to add one. The tests put them into the database.
internal static class Seed
{
    public static readonly DateTime Now = new(2026, 11, 9, 9, 0, 0, DateTimeKind.Utc);

    public static LearningRecord Record(string owner, Guid? answerId = null) =>
        LearningRecord.Create(
            answerId ?? Guid.NewGuid(), owner, Guid.NewGuid(), Guid.NewGuid(), null, true, 2, 4200, null, Mode.Practice, Now);

    public static async Task<LearningRecord> AddRecordAsync(this ServiceFactory service, string owner, Guid? answerId = null)
    {
        var record = Record(owner, answerId);
        using var scope = service.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningEngineDbContext>();
        db.LearningRecords.Add(record);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return record;
    }
}