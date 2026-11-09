namespace Edvaniq.Client.UI.Shell;

// Where the login leads back to. Only a path of this site: "//host" and "/\host" would take a browser to another site,
// so would anything with a scheme. Everything else leads to the start page.
public static class ReturnUrl
{
    public static string Target(string? returnUrl) => IsLocal(returnUrl) ? returnUrl! : "/";

    public static bool IsLocal(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl)
        && returnUrl[0] == '/'
        && (returnUrl.Length == 1 || (returnUrl[1] != '/' && returnUrl[1] != '\\'))
        && !returnUrl.Any(char.IsControl);

    // The login page with the way back to where the user is now.
    public static string LoginFor(string currentPath) => "login?returnUrl=" + Uri.EscapeDataString(currentPath);
}