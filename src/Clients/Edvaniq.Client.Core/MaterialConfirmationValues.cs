namespace Edvaniq.Client.Core;

// What the user confirms before a text or a file goes up (#278). Content knows the version and the message as
// MaterialConfirmation, the client can't reference it, so MaterialConfirmationValuesTests in the architecture tests holds
// both sides together. New words get a new version, on both sides.
public static class MaterialConfirmationValues
{
    public const string Version = "material-1";

    public const string Text =
        "Ich lade nur eigenes Material hoch oder Material, das ich für mein Lernen verwenden darf. Es enthält keine Daten anderer Personen.";

    public const string Missing =
        "Bestätige, dass du nur eigenes oder erlaubtes Material ohne Daten anderer Personen hochlädst.";
}