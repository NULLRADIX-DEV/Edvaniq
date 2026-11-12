namespace Edvaniq.SharedKernel;

// The study day of a point in time (docs/spec/glossary.md#lerntag): the day starts at 04:00 in Berlin, so 03:59 still
// counts for the day before. Every service gets it from here and never from another service. The zone comes from the
// system's time zone data. If it is missing, this throws instead of falling back to UTC.
public static class StudyDay
{
    public const string TimeZoneId = "Europe/Berlin";

    public static readonly TimeSpan Start = TimeSpan.FromHours(4);

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    public static DateOnly Of(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException($"A study day needs a time in UTC, got {utc.Kind}.", nameof(utc));
        }

        // 04:00 local never falls into a clock change in Berlin, so shifting the wall clock back by 4 h is exact.
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Zone) - Start);
    }

    // When the study day after the one of utc begins, in UTC: 04:00 in Berlin, an hour apart in summer and winter. What
    // counts per study day starts again then.
    public static DateTime NextStartUtc(DateTime utc)
    {
        var next = Of(utc).AddDays(1);
        return TimeZoneInfo.ConvertTimeToUtc(next.ToDateTime(TimeOnly.FromTimeSpan(Start), DateTimeKind.Unspecified), Zone);
    }

    // Throws if the zone data is missing or does not know summer and winter time in Berlin.
    public static void EnsureAvailable()
    {
        if (Of(new DateTime(2026, 7, 1, 2, 0, 0, DateTimeKind.Utc)) != new DateOnly(2026, 7, 1)
            || Of(new DateTime(2026, 1, 1, 2, 59, 0, DateTimeKind.Utc)) != new DateOnly(2025, 12, 31))
        {
            throw new InvalidOperationException($"The time zone data for {TimeZoneId} gives wrong study days.");
        }
    }
}