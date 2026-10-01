using Microsoft.EntityFrameworkCore;

namespace Edvaniq.Services.ServiceName.Infrastructure;

// The service's own database. It has no tables yet, they come with the domain. Every change to the model needs a
// migration (docs/service-template.md#datenbank-und-migrationen).
public sealed class ServiceNameDbContext(DbContextOptions<ServiceNameDbContext> options) : DbContext(options);
