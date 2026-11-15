using System.Globalization;

using Edvaniq.Client.UI.Feedback;

namespace Edvaniq.Client.UnitTests;

// Dates in German whatever culture the browser app runs with: without its culture data, de-DE could give English names.
public sealed class GermanDatesTests
{
    [Theory]
    [InlineData("")]
    [InlineData("ja-JP")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void Dates_AreGerman_InEveryCulture(string name)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        try
        {
            Assert.Equal("1. März 2027", GermanDates.Long(new DateOnly(2027, 3, 1)));
            Assert.Equal("11. November", GermanDates.DayAndMonth(new DateOnly(2026, 11, 11)));
            Assert.Equal("Mittwoch", GermanDates.Weekday(new DateOnly(2026, 11, 11)));
            Assert.Equal("Sonntag", GermanDates.Weekday(new DateOnly(2026, 11, 15)));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    // The same names as de-DE, where the runtime has its data.
    [Fact]
    public void Names_AreThoseOfGerman()
    {
        var german = CultureInfo.GetCultureInfo("de-DE");
        for (var month = 1; month <= 12; month++)
        {
            var date = new DateOnly(2027, month, 1);
            Assert.Equal(date.ToString("MMMM", german), GermanDates.MonthOf(date));
        }

        for (var day = 0; day < 7; day++)
        {
            var date = new DateOnly(2026, 11, 15).AddDays(day);
            Assert.Equal(date.ToString("dddd", german), GermanDates.Weekday(date));
        }
    }
}