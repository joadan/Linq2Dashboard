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
    private readonly Func<T, TDate> _selector;
    private readonly Func<TDate, TimeZoneInfo, DateTimeOffset?> _convert;

    public DateFacetDefinition(string key, Expression<Func<T, TDate>> selector)
        : base(key, FacetKind.Date)
    {
        _convert = DateConversion.For<TDate>();
        _selector = selector.Compile();
    }

    public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Utc;

    public DateGranularity Granularity { get; set; } = DateGranularity.Month;

    public DatePreset[] Presets { get; set; } = [];

    public override FacetIndex Build(T[] items, TimeProvider timeProvider)
    {
        Func<T, TDate> selector = _selector;
        Func<TDate, TimeZoneInfo, DateTimeOffset?> convert = _convert;
        TimeZoneInfo zone = Zone;
        var column = DateColumn.Build(items.Length, (int row, out DateTimeOffset value) =>
        {
            DateTimeOffset? read = convert(selector(items[row]), zone);
            value = read.GetValueOrDefault();
            return read.HasValue;
        }, zone, Granularity);

        return new DateFacetIndex(Key, Title, column, Presets, timeProvider);
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

    private static DateTimeOffset FromDateTime(DateTime value, TimeZoneInfo zone) =>
        value.Kind == DateTimeKind.Utc
            ? new DateTimeOffset(value)
            : DateColumn.ToInstant(value, zone);

    private static DateTimeOffset FromDateOnly(DateOnly value, TimeZoneInfo zone) =>
        DateColumn.ToInstant(value.ToDateTime(TimeOnly.MinValue), zone);
}
