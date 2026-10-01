using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Edvaniq.Services.ServiceName.Infrastructure;

// Only for dotnet ef and the migration test. Adding a migration needs no database, so a placeholder is enough.
public sealed class ServiceNameDbContextFactory : IDesignTimeDbContextFactory<ServiceNameDbContext>
{
    public ServiceNameDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ServiceNameDbContext>()
            .UseMySQL($"Server=localhost;Database={InfrastructureExtensions.DatabaseName}")
            .Options);
}
