namespace Edvaniq.Client.UI.Shell;

// What a page could not send because the login ran out: a pasted text, an answer not checked yet. Only in the memory of
// the running app, never in the browser's storage: it survives the way to the login and back, not a reload. A draft
// belongs to the account that wrote it. Another login, a logout or an unknown account throws all of them away, so on a
// shared device nobody sees what someone else typed. One per app, never registered in the web host.
public sealed class Drafts
{
    private readonly Dictionary<string, object> kept = [];
    private string? owner;

    // Called with each new login state, null when nobody is signed in.
    public void Claim(string? account)
    {
        if (account != owner || string.IsNullOrEmpty(account))
        {
            kept.Clear();
        }

        owner = account;
    }

    public void Keep(string key, object draft)
    {
        if (!string.IsNullOrEmpty(owner))
        {
            kept[key] = draft;
        }
    }

    // Each draft comes back once.
    public T? Take<T>(string key)
        where T : class =>
        kept.Remove(key, out var draft) ? draft as T : null;
}