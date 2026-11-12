using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Edvaniq.BuildingBlocks.Infrastructure;

// Oracle's provider writes a DateOnly but reads a date column back as DateTime and throws (#992). Every DateOnly goes
// through this, the column stays a date.
public sealed class DateOnlyConverter() : ValueConverter<DateOnly, DateTime>(
    value => value.ToDateTime(TimeOnly.MinValue),
    value => DateOnly.FromDateTime(value));