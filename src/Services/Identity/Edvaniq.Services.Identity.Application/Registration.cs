using Edvaniq.Services.Identity.Domain;

namespace Edvaniq.Services.Identity.Application;

// One unit of work across several statements, for steps that must succeed or fail together.
public interface IIdentityTransactions
{
    Task<IIdentityTransaction> BeginAsync(CancellationToken cancellationToken);
}

public interface IIdentityTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public sealed record RegistrationRequest(string? Email, string? Password, string? InviteCode, string? Pseudonym, int? BirthYear);

// Accepted, or refused with a code the client tells apart and a German sentence for the user.
public sealed record RegistrationResult(string? Refusal, string? Detail, bool MailDelayed)
{
    public bool Accepted => Refusal is null;
}

// Registration with an invitation code (#249, #255). Everything is checked before the code is redeemed, so a typo costs
// no code. Redeeming and creating the account go in one transaction. The answer never tells whether the address has an
// account already: then the code is redeemed all the same, so a second try with it says nothing either, and the address
// gets a mail that it has an account. An unconfirmed account with the address is replaced. A new account gets the link
// to confirm its address (#250) and can't log in before.
public sealed class Registration(
    IAccounts accounts, InviteService invites, SystemMails mails, Confirmation confirmation, IIdentityTransactions transactions, TimeProvider time)
{
    public const string InviteMissing = "invite_missing";
    public const string InviteInvalid = "invite_invalid";
    public const string InviteRedeemed = "invite_redeemed";
    public const string InviteRevoked = "invite_revoked";
    public const string EmailInvalid = "email_invalid";
    public const string PasswordRules = "password_rules";
    public const string PseudonymInvalid = "pseudonym_invalid";
    public const string BirthYearInvalid = "birth_year_invalid";

    public async Task<RegistrationResult> RegisterAsync(RegistrationRequest request, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        if (Check(request, now.Year) is { } refused)
        {
            return refused;
        }

        var email = AccountEmail.Normalize(request.Email)!;

        // Hashed in every case, before anything looks for the address: a known address must take as long as a new one.
        var passwordHash = PasswordHasher.Hash(request.Password!);
        await using (var transaction = await transactions.BeginAsync(cancellationToken))
        {
            switch (await invites.RedeemAsync(request.InviteCode, cancellationToken))
            {
                case RedeemResult.Unknown:
                    return Refuse(InviteInvalid, "Diesen Einladungscode gibt es nicht. Prüf, ob du ihn richtig abgetippt hast.");
                case RedeemResult.AlreadyRedeemed:
                    return Refuse(InviteRedeemed, "Dieser Einladungscode wurde schon benutzt. Jeder Code gilt nur für ein Konto.");
                case RedeemResult.Revoked:
                    return Refuse(InviteRevoked, "Dieser Einladungscode gilt nicht mehr.");
            }

            var existing = await accounts.FindByEmailAsync(email, cancellationToken);
            if (existing is { Status: not AccountStatus.Unconfirmed })
            {
                mails.AlreadyRegistered(existing);
            }
            else
            {
                // An unconfirmed account doesn't hold the address: a new registration replaces it, with its links and
                // mails, so nobody can take someone else's address by registering it first.
                if (existing is not null)
                {
                    accounts.Remove(existing);
                    await accounts.SaveAsync(cancellationToken);
                }

                var account = Account.CreateUser(
                    email, AccountProfile.NormalizePseudonym(request.Pseudonym)!, request.BirthYear!.Value, passwordHash, now);
                accounts.Add(account);
                confirmation.Send(account);
            }

            // Two registrations of one address at once: the second finds it taken on saving and answers like for any
            // address with an account. Its code stays open, nothing of it is kept.
            if (await accounts.SaveUnlessTakenAsync(cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }

        return new RegistrationResult(null, null, await mails.DelayedAsync(cancellationToken));
    }

    private static RegistrationResult? Check(RegistrationRequest request, int currentYear)
    {
        if (string.IsNullOrWhiteSpace(request.InviteCode))
        {
            return Refuse(InviteMissing, "Ohne Einladungscode geht es noch nicht. Frag die Person, die dich eingeladen hat.");
        }

        if (InviteCode.Normalize(request.InviteCode) is null)
        {
            return Refuse(InviteInvalid, "Diesen Einladungscode gibt es nicht. Prüf, ob du ihn richtig abgetippt hast.");
        }

        if (AccountEmail.Normalize(request.Email) is null)
        {
            return Refuse(EmailInvalid, "Das ist keine gültige E-Mail-Adresse.");
        }

        if (request.Password is not { } password || password.Length < PasswordHasher.MinimumLength)
        {
            return Refuse(PasswordRules, $"Dein Passwort braucht mindestens {PasswordHasher.MinimumLength} Zeichen.");
        }

        if (password.Length > PasswordHasher.MaximumLength)
        {
            return Refuse(PasswordRules, $"Dein Passwort darf höchstens {PasswordHasher.MaximumLength} Zeichen haben.");
        }

        if (AccountProfile.NormalizePseudonym(request.Pseudonym) is null)
        {
            return Refuse(
                PseudonymInvalid,
                $"Dein Spitzname braucht {AccountProfile.PseudonymMinLength} bis {AccountProfile.PseudonymMaxLength} Zeichen: Buchstaben, Ziffern, Leerzeichen, Punkt, Binde- oder Unterstrich. Nimm nicht deinen echten Namen.");
        }

        return request.BirthYear is { } year && AccountProfile.IsPlausibleBirthYear(year, currentYear)
            ? null
            : Refuse(BirthYearInvalid, "Dieses Geburtsjahr kann nicht stimmen.");
    }

    private static RegistrationResult Refuse(string code, string detail) => new(code, detail, false);
}