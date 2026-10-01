using MySql.Data.MySqlClient;

namespace Edvaniq.BuildingBlocks.Infrastructure;

// Settings for every connection of a service to its own database (DbContext and health check).
public static class MySqlConnections
{
    // Without TLS: MySql.Data keeps its TLS state in static dictionaries that it changes without a common lock, so
    // connections that open at the same time corrupt them (IndexOutOfRangeException, first seen in CI). The database
    // is reachable only inside the network of the app, whose containers cannot read each other's traffic (cap_drop).
    // Without TLS the first login of a user with caching_sha2_password needs the server's RSA key, so the driver may
    // fetch it.
    public static MySqlConnectionStringBuilder For(string connectionString) => new(connectionString)
    {
        SslMode = MySqlSslMode.Disabled,
        AllowPublicKeyRetrieval = true,
    };
}
