using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Edvaniq.BuildingBlocks.Infrastructure;

// Every DateTime column holds UTC. MySQL keeps no kind, so a value read back is marked as UTC, and an API that returns
// it writes the "Z". A value is stored as it is, so nothing already in a table changes. Local time is refused: it
// would end up in the column as a UTC time hours off.
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => ToStore(value),
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
{
    public static DateTime ToStore(DateTime value) => value.Kind == DateTimeKind.Local
        ? throw new ArgumentException("A time is stored in UTC, got a local time.", nameof(value))
        : value;
}