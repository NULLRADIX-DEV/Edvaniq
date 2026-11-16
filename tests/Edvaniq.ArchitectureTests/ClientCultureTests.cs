namespace Edvaniq.ArchitectureTests;

// #1070: dates in the clients are German whatever the browser's language, without a German culture.
public class ClientCultureTests
{
    [Fact]
    public void Clients_NeverFormatDatesWithACulture() =>
        Rules.AssertNone(ClientCultureRule.Violations(Repository.Root), "German dates without a culture (GermanDates)");

    [Fact]
    public void CultureAndNameFormats_AreReported()
    {
        string[] lines =
        [
            "var german = CultureInfo.GetCultureInfo(\"de-DE\");",
            "var date = day.ToString(\"dddd, d. MMMM\", German);",
            "new System.Globalization.CultureInfo(\"de\")",
            "var text = $\"{day:MMM}\";",
            "GermanDates.Long(date); day.ToString(\"dd.MM.yyyy\"); id.ToString(\"D\"); GetCultureInfo(\"ja-JP\");",
            "\"Donnerstag\", \"Mittwoch\"",
        ];

        Assert.Equal(
            [
                "Pages/View.razor:1 asks for GetCultureInfo(\"de-DE\"",
                "Pages/View.razor:2 formats dddd",
                "Pages/View.razor:2 formats MMMM",
                "Pages/View.razor:3 asks for CultureInfo(\"de\"",
                "Pages/View.razor:4 formats MMM",
            ],
            ClientCultureRule.Check("Pages/View.razor", lines));
    }
}