namespace Edvaniq.AppHost;

// Edvaniq.Testing compiles this file too, so the tests grant exactly what the AppHost grants.
internal static class ServiceDatabaseSql
{
    public static string Grant(string user, string database) =>
        $"GRANT ALL PRIVILEGES ON `{database}`.* TO '{user}'@'%'";
}