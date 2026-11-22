namespace Edvaniq.Client.Core;

// The signed-in user sets a new password (#254). The account is the one of the session, the call names no other.
public interface IPasswordChange
{
    // Ok: the password is changed, the session stays. Rejected names the sentence of Identity at the field it belongs to
    // (FieldErrors, keyed by PasswordChangeFields). Limited: too many wrong passwords or too many calls, Messages says
    // how long to wait if the service did.
    Task<CallOutcome<bool>> ChangeAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default);
}

// The keys of FieldErrors, the names Identity's request has.
public static class PasswordChangeFields
{
    public const string Current = "currentPassword";

    public const string New = "newPassword";
}
