using System.Linq.Expressions;
using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Facets;

/// <summary>
/// Configuration of a date facet over a property of type <typeparamref name="TDate"/>, one of
/// <see cref="DateTime"/>, <see cref="DateTimeOffset"/>, <see cref="DateOnly"/> or their nullable
/// forms. Conversion into instants happens at build with the configured zone (concept §5).
/// </summary>
internal sealed class DateFacetDefinition<T, TDate> : FacetDefinition<T>
{
    private readonly Func<T, TDate> selector;
    private readonly Func<TDate, TimeZoneInfo, DateTimeOffset?> convert;

    public DateFacetDefinition(string key, Expression<Func<T, TDate>> selector)
        : base(key, FacetKind.Date)
    {
        convert = DateConversion.For<TDate>();
        this.selector = selector.Compile();
    }

    public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>The period named in the definition, or null to derive one from the data (concept §5).</summary>
    public DateGranularity? Granularity { get; set; }

    /// <summary>The most periods a derived granularity should lay over the body of the data.</summary>
    public int MaxPeriods { get; set; } = DateGranularities.DefaultMaxPeriods;

    public DatePreset[] Presets { get; set; } = [];

    public bool SkipEmptyPresets { get; set; }

    public override FacetIndex Build(T[] items, TimeProvider timeProvider, bool parallel)
    {
        TimeZoneInfo zone = Zone;
        var column = DateColumn.Build(items.Length, (int row, out DateTimeOffset value) =>
        {
            DateTimeOffset? read = convert(selector(items[row]), zone);
            value = read.GetValueOrDefault();
            return read.HasValue;
        }, zone, Granularity, MaxPeriods);

        return new DateFacetIndex(Key, Name, column, Presets, SkipEmptyPresets, timeProvider);
    }
}

/// <summary>
/// The starting position on date types from concept §5: <see cref="DateTimeOffset"/> is converted;
/// <see cref="DateTime"/> with kind UTC is converted, with kind Local or Unspecified it is taken as
/// already being in the facet's zone; <see cref="DateOnly"/> is midnight in the facet's zone.
/// </summary>
internal static class DateConversion
{
    public static bool IsSupported(Type type)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset) || underlying == typeof(DateOnly);
    }

    public static Func<TDate, TimeZoneInfo, DateTimeOffset?> For<TDate>()
    {
        object converter = typeof(TDate) switch
        {
            Type t when t == typeof(DateTimeOffset) => new Func<DateTimeOffset, TimeZoneInfo, DateTimeOffset?>((d, _) => d),
            Type t when t == typeof(DateTimeOffset?) => new Func<DateTimeOffset?, TimeZoneInfo, DateTimeOffset?>((d, _) => d),
            Type t when t == typeof(DateTime) => new Func<DateTime, TimeZoneInfo, DateTimeOffset?>((d, z) => FromDateTime(d, z)),
            Type t when t == typeof(DateTime?) => new Func<DateTime?, TimeZoneInfo, DateTimeOffset?>((d, z) => d is DateTime v ? FromDateTime(v, z) : null),
            Type t when t == typeof(DateOnly) => new Func<DateOnly, TimeZoneInfo, DateTimeOffset?>((d, z) => FromDateOnly(d, z)),
            Type t when t == typeof(DateOnly?) => new Func<DateOnly?, TimeZoneInfo, DateTimeOffset?>((d, z) => d is DateOnly v ? FromDateOnly(v, z) : null),
            _ => throw new ArgumentException(
                $"'{typeof(TDate)}' is not a supported date type. Use DateTime, DateTimeOffset or DateOnly, or their nullable forms."),
        };

        return (Func<TDate, TimeZoneInfo, DateTimeOffset?>)converter;
    }

    /// <summary>
    /// The UTC ticks of a date in UTC, without a zone lookup: what <see cref="For{TDate}"/> gives for
    /// <see cref="TimeZoneInfo.Utc"/>, for a query reading a plain date property per row (design §10.3). A
    /// <see cref="DateTime"/> of any kind keeps its ticks, since Local and Unspecified are taken as being in the zone.
    /// </summary>
    public static Func<TDate, long?> UtcTicks<TDate>()
    {
        object ticks = typeof(TDate) switch
        {
            Type t when t == typeof(DateTimeOffset) => new Func<DateTimeOffset, long?>(d => d.UtcTicks),
            Type t when t == typeof(DateTimeOffset?) => new Func<DateTimeOffset?, long?>(d => d?.UtcTicks),
            Type t when t == typeof(DateTime) => new Func<DateTime, long?>(d => d.Ticks),
            Type t when t == typeof(DateTime?) => new Func<DateTime?, long?>(d => d?.Ticks),
            Type t when t == typeof(DateOnly) => new Func<DateOnly, long?>(d => d.DayNumber * TimeSpan.TicksPerDay),
            Type t when t == typeof(DateOnly?) => new Func<DateOnly?, long?>(d => d is DateOnly v ? v.DayNumber * TimeSpan.TicksPerDay : null),
            _ => throw new ArgumentException($"'{typeof(TDate)}' is not a supported date type."),
        };

        return (Func<TDate, long?>)ticks;
    }

    private static DateTimeOffset FromDateTime(DateTime value, TimeZoneInfo zone) =>
        value.Kind == DateTimeKind.Utc
            ? new DateTimeOffset(value)
            : DateColumn.ToInstant(value, zone);

    private static DateTimeOffset FromDateOnly(DateOnly value, TimeZoneInfo zone) =>
        DateColumn.ToInstant(value.ToDateTime(TimeOnly.MinValue), zone);
}
