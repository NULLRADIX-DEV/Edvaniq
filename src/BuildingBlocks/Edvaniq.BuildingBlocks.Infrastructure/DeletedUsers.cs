using Edvaniq.BuildingBlocks.Application;
using Edvaniq.BuildingBlocks.Messaging;

using Microsoft.EntityFrameworkCore;

namespace Edvaniq.BuildingBlocks.Infrastructure;

// One lookup by primary key per request. Sync on purpose, MySql.Data's OpenAsync can hang (MySqlHealthCheck). A database
// error is thrown, never read as "deleted".
internal sealed class DeletedUsers<TContext>(TContext db) : IDeletedUsers
    where TContext : ServiceDbContext
{
    public bool IsDeleted(string userId)
    {
        var hash = DeletedUser.HashOf(userId);
        return db.Set<DeletedUser>().AsNoTracking().Any(user => user.Hash == hash);
    }
}