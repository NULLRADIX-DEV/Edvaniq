using MySqlConnector;

namespace Edvaniq.AppHost;

// A service's own database together with the connection string of its own user.
internal sealed record ServiceDatabase(IResourceBuilder<MySqlDatabaseResource> Database, ReferenceExpression ConnectionString);

internal static class ServiceDatabaseExtensions
{
    // Database <service>db and user <service>, who has all rights on it and none on anything else. The password is
    // generated once and kept in the AppHost's user secrets. The user is created or updated on every start, in the
    // ready event of the database, and WaitFor waits for that event.
    public static ServiceDatabase AddServiceDatabase(this IResourceBuilder<MySqlServerResource> server, string service)
    {
        var builder = server.ApplicationBuilder;
        var database = server.AddDatabase($"{service}db");
        var password = builder.AddParameter(
            $"{service}db-password", new GenerateParameterDefault { MinLength = 24, Special = false }, secret: true, persist: true);

        builder.Eventing.Subscribe<ResourceReadyEvent>(database.Resource, async (_, cancellationToken) =>
        {
            await using var connection = new MySqlConnection(
                await server.Resource.ConnectionStringExpression.GetValueAsync(cancellationToken));
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE USER IF NOT EXISTS '{service}'@'%' IDENTIFIED BY @password;
                ALTER USER '{service}'@'%' IDENTIFIED BY @password;
                GRANT ALL PRIVILEGES ON `{database.Resource.DatabaseName}`.* TO '{service}'@'%';
                """;
            command.Parameters.AddWithValue("@password", await password.Resource.GetValueAsync(cancellationToken));
            await command.ExecuteNonQueryAsync(cancellationToken);
        });

        var endpoint = server.Resource.PrimaryEndpoint;
        return new ServiceDatabase(database, ReferenceExpression.Create(
            $"Server={endpoint.Property(EndpointProperty.Host)};Port={endpoint.Property(EndpointProperty.Port)};User ID={service};Password={password.Resource};Database={database.Resource.DatabaseName}"));
    }

    // The service gets only its own database, under the name it reads (ConnectionStrings:<service>db).
    public static IResourceBuilder<ProjectResource> WithDatabase(this IResourceBuilder<ProjectResource> project, ServiceDatabase database) =>
        project
            .WithEnvironment($"ConnectionStrings__{database.Database.Resource.Name}", database.ConnectionString)
            .WaitFor(database.Database);

    // The migration step of a service from the template: its Api with the argument migrate, which applies the
    // migrations and exits. Without a launch profile, so it does not compete with the Api for its ports.
    public static IResourceBuilder<ProjectResource> AddMigration<TProject>(
        this IDistributedApplicationBuilder builder, string name, ServiceDatabase database)
        where TProject : IProjectMetadata, new() =>
        builder.AddProject<TProject>(name, launchProfileName: null)
            .WithArgs("migrate")
            .WithDatabase(database);
}
