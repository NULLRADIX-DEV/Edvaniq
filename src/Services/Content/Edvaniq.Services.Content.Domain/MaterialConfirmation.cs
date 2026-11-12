namespace Edvaniq.Services.Content.Domain;

// What the user confirms with every text and file (#278): only material of their own or that they may use, nothing about
// other people. The wording lives in the client, Content knows its version. A new wording gets a new version, and once
// there are versioned terms of use the version points at them.
public static class MaterialConfirmation
{
    public const string Version = "material-1";

    public const int VersionMaxLength = 32;

    public const string Missing =
        "Bestätige, dass du nur eigenes oder erlaubtes Material ohne Daten anderer Personen hochlädst.";

    // Why the confirmation can't be taken, or null if it is the current one.
    public static string? Check(string? confirmation) => confirmation == Version ? null : Missing;
}