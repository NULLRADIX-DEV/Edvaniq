using System.Globalization;

namespace Edvaniq.Client.UnitTests;

// Runs a test under a culture of the browser, as the browser app does. "" is the invariant culture.
internal static class Cultures
{
    public static IDisposable Use(string name)
    {
        var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        return new Restore(before.CurrentCulture, before.CurrentUICulture);
    }

    private sealed class Restore(CultureInfo culture, CultureInfo uiCulture) : IDisposable
    {
        public void Dispose()
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }
}