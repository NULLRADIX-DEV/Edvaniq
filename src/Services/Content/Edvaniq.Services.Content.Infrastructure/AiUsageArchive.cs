using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Domain;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.Content.Infrastructure;

// Sync like the remover that runs after it, MySql.Data's OpenAsync can hang (MySqlHealthCheck).
internal sealed class AiUsageArchive(ContentDbContext db) : IAiUsageArchive
{
    public void Archive(string userId)
    {
        var attempts = db.AiAttempts.IgnoreQueryFilters()
            .Where(attempt => attempt.OwnerId == userId)
            .ToList();
        if (attempts.Count == 0)
        {
            return;
        }

        var days = attempts.Select(AiUsageDay.DayOf).Distinct().ToList();
        var sums = db.AiUsageDays.Where(sum => days.Contains(sum.Day)).ToList();
        foreach (var attempt in attempts)
        {
            var sum = sums.FirstOrDefault(candidate => candidate.Holds(attempt));
            if (sum is null)
            {
                sum = AiUsageDay.For(attempt);
                sums.Add(sum);
                db.AiUsageDays.Add(sum);
            }

            sum.Add(attempt);
        }
    }
}