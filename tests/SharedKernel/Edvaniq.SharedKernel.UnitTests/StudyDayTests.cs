namespace Edvaniq.SharedKernel.UnitTests;

// All times are fixed in UTC, so the tests do not depend on the zone of the machine. Berlin is UTC+1 in winter and
// UTC+2 in summer, so the day starts at 03:00 UTC in winter and at 02:00 UTC in summer.
public class StudyDayTests
{
    [Theory]
    [InlineData("2027-07-15T01:59:00Z", "2027-07-14")]
    [InlineData("2027-07-15T02:00:00Z", "2027-07-15")]
    [InlineData("2027-01-15T02:59:00Z", "2027-01-14")]
    [InlineData("2027-01-15T03:00:00Z", "2027-01-15")]
    [InlineData("2027-07-15T21:59:59Z", "2027-07-15")]
    [InlineData("2027-07-15T22:00:00Z", "2027-07-15")]
    public void At0359_ItIsTheDayBefore_At0400_TheNewDay(string utc, string day) =>
        Assert.Equal(DateOnly.Parse(day), StudyDay.Of(Utc(utc)));

    // 28.03.2027: 02:00 CET becomes 03:00 CEST at 01:00 UTC. The day 27.03. starts at 03:00 UTC and ends at 02:00 UTC.
    [Theory]
    [InlineData("2027-03-27T02:59:00Z", "2027-03-26")]
    [InlineData("2027-03-27T03:00:00Z", "2027-03-27")]
    [InlineData("2027-03-28T00:59:00Z", "2027-03-27")]
    [InlineData("2027-03-28T01:00:00Z", "2027-03-27")]
    [InlineData("2027-03-28T01:59:00Z", "2027-03-27")]
    [InlineData("2027-03-28T02:00:00Z", "2027-03-28")]
    public void SpringForward_EndsTheDayAt0400SummerTime(string utc, string day) =>
        Assert.Equal(DateOnly.Parse(day), StudyDay.Of(Utc(utc)));

    // 31.10.2027: 03:00 CEST becomes 02:00 CET at 01:00 UTC, so 02:30 local happens twice. The day 30.10. starts at
    // 02:00 UTC and ends at 03:00 UTC.
    [Theory]
    [InlineData("2027-10-30T01:59:00Z", "2027-10-29")]
    [InlineData("2027-10-30T02:00:00Z", "2027-10-30")]
    [InlineData("2027-10-31T00:30:00Z", "2027-10-30")]
    [InlineData("2027-10-31T01:30:00Z", "2027-10-30")]
    [InlineData("2027-10-31T02:59:00Z", "2027-10-30")]
    [InlineData("2027-10-31T03:00:00Z", "2027-10-31")]
    public void FallBack_EndsTheDayAt0400WinterTime(string utc, string day) =>
        Assert.Equal(DateOnly.Parse(day), StudyDay.Of(Utc(utc)));

    [Theory]
    [InlineData("2027-03-27", 23)]
    [InlineData("2027-03-28", 24)]
    [InlineData("2027-10-30", 25)]
    [InlineData("2027-10-31", 24)]
    public void DaysOfTheClockChange_HaveTheirLength(string day, int hours)
    {
        var start = Utc("2027-03-25T00:00:00Z");
        var minutes = Enumerable.Range(0, 230 * 24 * 60)
            .Count(minute => StudyDay.Of(start.AddMinutes(minute)) == DateOnly.Parse(day));

        Assert.Equal(hours * 60, minutes);
    }

    [Theory]
    [InlineData("2027-01-01T02:59:00Z", "2026-12-31")]
    [InlineData("2027-01-01T03:00:00Z", "2027-01-01")]
    public void NewYear_StartsAt0400(string utc, string day) =>
        Assert.Equal(DateOnly.Parse(day), StudyDay.Of(Utc(utc)));

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void TimeNotInUtc_Throws(DateTimeKind kind) =>
        Assert.Throws<ArgumentException>(() => StudyDay.Of(new DateTime(2027, 7, 15, 12, 0, 0, kind)));

    [Fact]
    public void ZoneData_IsAvailable() => StudyDay.EnsureAvailable();

    // #301: the next 04:00 in Berlin, 02:00 UTC in summer and 03:00 UTC in winter, also across a clock change.
    [Theory]
    [InlineData("2027-07-15T01:59:00Z", "2027-07-15T02:00:00Z")]
    [InlineData("2027-07-15T02:00:00Z", "2027-07-16T02:00:00Z")]
    [InlineData("2027-01-15T02:59:00Z", "2027-01-15T03:00:00Z")]
    [InlineData("2027-01-15T03:00:00Z", "2027-01-16T03:00:00Z")]
    [InlineData("2027-03-27T03:00:00Z", "2027-03-28T02:00:00Z")]
    [InlineData("2027-10-30T02:00:00Z", "2027-10-31T03:00:00Z")]
    [InlineData("2027-12-31T23:00:00Z", "2028-01-01T03:00:00Z")]
    public void NextStart_IsTheNext0400InBerlin(string utc, string next)
    {
        var start = StudyDay.NextStartUtc(Utc(utc));

        Assert.Equal((Utc(next), DateTimeKind.Utc), (start, start.Kind));
        Assert.Equal(StudyDay.Of(Utc(utc)).AddDays(1), StudyDay.Of(start));
    }

    private static DateTime Utc(string value) =>
        DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);
}