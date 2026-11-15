using System.Globalization;

namespace Edvaniq.Client.UI.Feedback;

// Dates in German whatever the browser's language. The browser app only loads the culture data of the browser's language,
// so formatting with de-DE could give English names there. Only the date is written, the day itself comes from the
// server (the study day), nothing is worked out here.
public static class GermanDates
{
    private static readonly string[] Months =
    [
        "Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember",
    ];

    private static readonly string[] Weekdays = ["Sonntag", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag"];

    // "1. März 2027"
    public static string Long(DateOnly date) =>
        string.Create(CultureInfo.InvariantCulture, $"{date.Day}. {MonthOf(date)} {date.Year}");

    // "11. November"
    public static string DayAndMonth(DateOnly date) => string.Create(CultureInfo.InvariantCulture, $"{date.Day}. {MonthOf(date)}");

    // "Mittwoch"
    public static string Weekday(DateOnly date) => Weekdays[(int)date.DayOfWeek];

    public static string MonthOf(DateOnly date) => Months[date.Month - 1];
}