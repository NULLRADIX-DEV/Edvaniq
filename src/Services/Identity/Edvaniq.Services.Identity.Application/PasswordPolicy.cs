namespace Edvaniq.Services.Identity.Application;

// The rules for a new password and the sentences a user reads when it breaks one. A registration and a change of
// password say the same.
public static class PasswordPolicy
{
    public const string Code = "password_rules";

    // Why the password can not be used, or null if it can.
    public static string? Violation(string? password) =>
        password is null || password.Length < PasswordHasher.MinimumLength
            ? $"Dein Passwort braucht mindestens {PasswordHasher.MinimumLength} Zeichen."
            : password.Length > PasswordHasher.MaximumLength
                ? $"Dein Passwort darf höchstens {PasswordHasher.MaximumLength} Zeichen haben."
                : null;
}
