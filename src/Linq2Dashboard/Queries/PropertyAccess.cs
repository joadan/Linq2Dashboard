using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Linq2Dashboard.Facets;
using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Queries;

/// <summary>
/// The properties of <typeparamref name="T"/> a query can reach (design §10.5): public, readable instance
/// properties, inherited ones included, by case-sensitive name. A getter is compiled on first use and kept,
/// shared by a dashboard and its scopes; the values are read again by every query (design §10.3).
/// </summary>
internal sealed class PropertyCatalog<T>
{
    private readonly ConcurrentDictionary<string, PropertyAccessor<T>?> accessors = new(StringComparer.Ordinal);
    private readonly Lazy<string[]> names = new(() => Readable().Select(p => p.Name).Distinct(StringComparer.Ordinal).ToArray());

    /// <summary>Every reachable name, for error messages.</summary>
    public IReadOnlyList<string> Names => names.Value;

    public bool TryGet(string name, out PropertyAccessor<T> accessor)
    {
        accessor = accessors.GetOrAdd(name, Create)!;
        return accessor is not null;
    }

    private static PropertyAccessor<T>? Create(string name)
    {
        // A property hidden with `new` appears once per declaring type; the most derived one is what the row shows.
        PropertyInfo? property = Readable()
            .Where(p => p.Name == name)
            .OrderBy(p => Depth(p.DeclaringType))
            .LastOrDefault();
        if (property is null)
        {
            return null;
        }

        Type? item = ItemType(property.PropertyType);
        Type accessor = item is null
            ? typeof(ValueAccessor<,>).MakeGenericType(typeof(T), property.PropertyType)
            : typeof(ItemsAccessor<,>).MakeGenericType(typeof(T), item);
        return (PropertyAccessor<T>)Activator.CreateInstance(accessor, property)!;
    }

    private static IEnumerable<PropertyInfo> Readable() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0);

    private static int Depth(Type? type)
    {
        int depth = 0;
        for (; type is not null; type = type.BaseType)
        {
            depth++;
        }

        return depth;
    }

    /// <summary>The item type of a collection property, grouped like a multi-valued facet (concept §4.11); null for anything else, a string included.</summary>
    private static Type? ItemType(Type type)
    {
        if (type == typeof(string))
        {
            return null;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return type.GetGenericArguments()[0];
        }

        return type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?
            .GetGenericArguments()[0];
    }
}

/// <summary>One property of <typeparamref name="T"/> as a query reads it (design §10.5).</summary>
internal abstract class PropertyAccessor<T>(PropertyInfo property)
{
    public string Name => property.Name;

    public Type Type => property.PropertyType;

    protected PropertyInfo Property => property;

    /// <summary>The property as a dimension, by value or by period, over the dashboard's rows.</summary>
    public abstract GroupColumn GroupColumn(T[] items, DateGranularity? period);

    /// <summary>Numbers for sum, average, min and max, or null when the property is not numeric.</summary>
    public abstract NumberSource? Numbers(T[] items);

    /// <summary>The property as a column of a row query: simple types only, so an answer never holds an object graph (design §10.5).</summary>
    public abstract RowColumn RowColumn(T[] items, DateGranularity? period);

    /// <summary>
    /// A type a row query may return (design §10.5): the primitives, <see cref="string"/>, <see cref="decimal"/>,
    /// enums, <see cref="Guid"/> and the date and time types, nullable or not.
    /// </summary>
    protected static bool IsSimple(Type type)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive || underlying.IsEnum || NumericConversion.IsNumeric(underlying)
            || underlying == typeof(string) || underlying == typeof(decimal) || underlying == typeof(Guid)
            || underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset) || underlying == typeof(DateOnly)
            || underlying == typeof(TimeOnly) || underlying == typeof(TimeSpan);
    }

    protected DashboardQueryException NotSimple() =>
        Error("holds an object, and a row query returns simple values only: numbers, text, enums, dates and lists of them. Flatten what you want listed into the row, or define a facet.");

    /// <summary>Keys for a distinct count, with the value facet's equality.</summary>
    public abstract DistinctSource DistinctKeys(T[] items);

    protected Expression<Func<T, TResult>> Lambda<TResult>()
    {
        ParameterExpression row = Expression.Parameter(typeof(T), "row");
        Expression body = Expression.Property(row, property);
        if (body.Type != typeof(TResult))
        {
            body = Expression.Convert(body, typeof(TResult));
        }

        return Expression.Lambda<Func<T, TResult>>(body, row);
    }

    /// <summary>The property's type as C# writes it, for messages: <c>Int32?</c>, <c>IReadOnlyList&lt;String?&gt;?</c>.</summary>
    public string TypeName => Display(Type, new NullabilityInfoContext().Create(Property));

    protected DashboardQueryException Error(string message) => new($"Property '{Name}' ({TypeName}) {message}");

    private static string Display(Type type, NullabilityInfo nullability)
    {
        if (Nullable.GetUnderlyingType(type) is Type underlying)
        {
            return Display(underlying, nullability) + "?";
        }

        string name = type.Name;
        if (type.IsGenericType)
        {
            NullabilityInfo[] inner = nullability.GenericTypeArguments;
            IEnumerable<string> arguments = type.GetGenericArguments()
                .Select((argument, i) => i < inner.Length ? Display(argument, inner[i]) : argument.Name);
            name = $"{name[..name.IndexOf('`')]}<{string.Join(", ", arguments)}>";
        }

        return !type.IsValueType && nullability.ReadState == NullabilityState.Nullable ? name + "?" : name;
    }
}

/// <summary>A single-valued property: grouped by value with the value facet's equality, or by period when it is a date.</summary>
internal sealed class ValueAccessor<T, TProp> : PropertyAccessor<T>
{
    private readonly Lazy<Func<T, TProp>> getter;

    public ValueAccessor(PropertyInfo property)
        : base(property)
    {
        getter = new Lazy<Func<T, TProp>>(() => Lambda<TProp>().Compile());
    }

    public override GroupColumn GroupColumn(T[] items, DateGranularity? period)
    {
        if (period is not DateGranularity granularity)
        {
            return new PropertyGroupColumn<T, TProp>(items, getter.Value);
        }

        if (!DateConversion.IsSupported(typeof(TProp)))
        {
            throw Error($"is not a date, so it cannot be grouped by {granularity}.");
        }

        Func<TProp, TimeZoneInfo, DateTimeOffset?> convert = DateConversion.For<TProp>();
        Func<T, TProp> read = getter.Value;
        return new PeriodGroupColumn(row => convert(read(items[row]), TimeZoneInfo.Utc) is DateTimeOffset instant ? instant.UtcTicks : null, TimeZoneInfo.Utc, granularity);
    }

    public override NumberSource? Numbers(T[] items)
    {
        Type type = Nullable.GetUnderlyingType(typeof(TProp)) ?? typeof(TProp);
        if (!NumericConversion.IsNumeric(type))
        {
            return null;
        }

        Func<T, double?> read = NumericConversion.ToNullableDouble(Lambda<TProp>(), Name);
        return new PropertyNumbers<T>(this, items, read);
    }

    public override DistinctSource DistinctKeys(T[] items) => new PropertyKeys<T, TProp>(this, items, getter.Value);

    /// <summary>The value as it is, a <see cref="DateOnly"/> as a date, another date type as its instant in UTC, or a period when one is named.</summary>
    public override RowColumn RowColumn(T[] items, DateGranularity? period)
    {
        if (!IsSimple(typeof(TProp)))
        {
            throw NotSimple();
        }

        if (period is not null)
        {
            return new CodedRowColumn(GroupColumn(items, period));
        }

        if ((Nullable.GetUnderlyingType(typeof(TProp)) ?? typeof(TProp)) == typeof(DateOnly))
        {
            Func<T, DateOnly?> date = Lambda<DateOnly?>().Compile();
            return new DateRowColumn(row => date(items[row]));
        }

        if (DateConversion.IsSupported(typeof(TProp)))
        {
            Func<TProp, TimeZoneInfo, DateTimeOffset?> convert = DateConversion.For<TProp>();
            Func<T, TProp> read = getter.Value;
            return new InstantRowColumn(row => convert(read(items[row]), TimeZoneInfo.Utc) is DateTimeOffset instant ? instant.UtcTicks : null, TimeZoneInfo.Utc);
        }

        return new CodedRowColumn(GroupColumn(items, null));
    }
}

/// <summary>A collection property other than a string: a row counts under each distinct item, like a multi-valued facet (concept §5).</summary>
internal sealed class ItemsAccessor<T, TItem> : PropertyAccessor<T>
{
    private readonly Lazy<Func<T, IEnumerable<TItem>?>> getter;

    public ItemsAccessor(PropertyInfo property)
        : base(property)
    {
        getter = new Lazy<Func<T, IEnumerable<TItem>?>>(() => Lambda<IEnumerable<TItem>?>().Compile());
    }

    public override GroupColumn GroupColumn(T[] items, DateGranularity? period) =>
        period is DateGranularity granularity
            ? throw Error($"is a collection, so it cannot be grouped by {granularity}.")
            : new ItemsGroupColumn<T, TItem>(items, getter.Value);

    public override NumberSource? Numbers(T[] items) => null;

    public override DistinctSource DistinctKeys(T[] items) => new ItemKeys<T, TItem>(this, items, getter.Value);

    /// <summary>The row's distinct non-null items as a list, as a multi-valued facet holds them.</summary>
    public override RowColumn RowColumn(T[] items, DateGranularity? period)
    {
        if (!IsSimple(typeof(TItem)))
        {
            throw NotSimple();
        }

        if (period is DateGranularity granularity)
        {
            throw Error($"is a collection, so it cannot be grouped by {granularity}.");
        }

        Func<T, IEnumerable<TItem>?> read = getter.Value;
        return new ListRowColumn(row =>
        {
            var cells = new List<QueryValueCell>();
            var seen = new HashSet<ValueKey<TItem>>(ValueKeyComparer<TItem>.Default);
            foreach (TItem item in read(items[row]) ?? [])
            {
                if (item is not null && seen.Add(new ValueKey<TItem>(item)))
                {
                    cells.Add(new QueryValueCell(item));
                }
            }

            return cells;
        });
    }
}

/// <summary>
/// A dictionary key that is never null for <see cref="Dictionary{TKey, TValue}"/>, whatever the property type,
/// compared with the value facet's equality (design §3.3). Null values never become keys: they are code 0.
/// </summary>
internal readonly record struct ValueKey<TValue>(TValue Value);

internal sealed class ValueKeyComparer<TValue>(IEqualityComparer<TValue> inner) : IEqualityComparer<ValueKey<TValue>>
{
    public static readonly ValueKeyComparer<TValue> Default = new(ValueColumn.DefaultComparer<TValue>());

    public bool Equals(ValueKey<TValue> x, ValueKey<TValue> y) => inner.Equals(x.Value, y.Value);

    public int GetHashCode(ValueKey<TValue> obj) => obj.Value is null ? 0 : inner.GetHashCode(obj.Value);
}

/// <summary>Codes values per query in first-seen order: 0 for null, 1.. for values (design §10.3). Not thread-safe; one per query.</summary>
internal sealed class ValueCoder<TValue>
{
    private readonly Dictionary<ValueKey<TValue>, int> codes = new(ValueKeyComparer<TValue>.Default);
    private readonly List<TValue> values = [];

    public int Code(TValue value)
    {
        if (value is null)
        {
            return 0;
        }

        var key = new ValueKey<TValue>(value);
        if (!codes.TryGetValue(key, out int code))
        {
            values.Add(value);
            code = values.Count;
            codes.Add(key, code);
        }

        return code;
    }

    public TValue ValueOf(int code) => values[code - 1];
}

/// <summary>A plain property grouped by value. The number of codes is not known before the scan, so groups are found through a hash.</summary>
internal sealed class PropertyGroupColumn<T, TProp>(T[] items, Func<T, TProp> read) : GroupColumn(int.MaxValue, QueryColumnKind.Value)
{
    private readonly ValueCoder<TProp> coder = new();

    public override int CodeAt(int row) => coder.Code(read(items[row]));

    public override object Cell(int code) => new QueryValueCell(coder.ValueOf(code));

    protected override int CompareNonNull(int a, int b) => ValueCells.Compare(coder.ValueOf(a), coder.ValueOf(b), a, b);
}

/// <summary>A collection property grouped by item: a row joins one group per distinct item; no items is the null value.</summary>
internal sealed class ItemsGroupColumn<T, TItem>(T[] items, Func<T, IEnumerable<TItem>?> read) : GroupColumn(int.MaxValue, QueryColumnKind.Value)
{
    private readonly ValueCoder<TItem> coder = new();
    private readonly List<int> buffer = [];

    public override bool IsMultiValued => true;

    public override int CodeAt(int row) => throw new NotSupportedException();

    public override ReadOnlySpan<int> CodesAt(int row)
    {
        buffer.Clear();
        if (read(items[row]) is IEnumerable<TItem> values)
        {
            foreach (TItem item in values)
            {
                int code = coder.Code(item);
                if (code != 0 && !buffer.Contains(code))
                {
                    buffer.Add(code);
                }
            }
        }

        return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(buffer);
    }

    public override object Cell(int code) => new QueryValueCell(coder.ValueOf(code));

    protected override int CompareNonNull(int a, int b) => ValueCells.Compare(coder.ValueOf(a), coder.ValueOf(b), a, b);
}

/// <summary>
/// Instants grouped by calendar period in a zone (concept §4.11): a plain date property in UTC, or a date facet
/// grouped by another period than its own, in the facet's zone. Codes are period starts in first-seen order.
/// </summary>
internal sealed class PeriodGroupColumn(Func<int, long?> utcTicksAt, TimeZoneInfo zone, DateGranularity granularity)
    : GroupColumn(int.MaxValue, QueryColumnKind.Period)
{
    private readonly Dictionary<long, int> codes = [];
    private readonly List<long> starts = [];

    public override DateGranularity? Granularity => granularity;

    public override int CodeAt(int row)
    {
        if (utcTicksAt(row) is not long ticks)
        {
            return 0;
        }

        DateTime local = zone == TimeZoneInfo.Utc
            ? new DateTime(ticks, DateTimeKind.Unspecified)
            : TimeZoneInfo.ConvertTime(new DateTimeOffset(ticks, TimeSpan.Zero), zone).DateTime;
        long start = DateColumn.PeriodStart(local, granularity).Ticks;
        if (!codes.TryGetValue(start, out int code))
        {
            starts.Add(start);
            code = starts.Count;
            codes.Add(start, code);
        }

        return code;
    }

    public override object Cell(int code)
    {
        var start = new DateTime(starts[code - 1], DateTimeKind.Unspecified);
        DateTime next = DateColumn.NextPeriodStart(start, granularity);
        return new QueryPeriod(
            DateInterval.Between(DateColumn.ToInstant(start, zone), DateColumn.ToInstant(next, zone)),
            start,
            granularity);
    }

    protected override int CompareNonNull(int a, int b) => starts[a - 1].CompareTo(starts[b - 1]);
}

internal sealed class PropertyNumbers<T>(object identity, T[] items, Func<T, double?> read) : NumberSource
{
    public override object Identity => identity;

    public override double ValueAt(int row) => read(items[row]) ?? double.NaN;
}

internal sealed class PropertyKeys<T, TProp>(object identity, T[] items, Func<T, TProp> read) : DistinctSource
{
    private readonly ValueCoder<TProp> coder = new();

    public override object Identity => identity;

    public override void AddTo(int row, int group, HashSet<(int Group, long Key)> seen)
    {
        int code = coder.Code(read(items[row]));
        if (code != 0)
        {
            seen.Add((group, code));
        }
    }
}

internal sealed class ItemKeys<T, TItem>(object identity, T[] items, Func<T, IEnumerable<TItem>?> read) : DistinctSource
{
    private readonly ValueCoder<TItem> coder = new();

    public override object Identity => identity;

    public override void AddTo(int row, int group, HashSet<(int Group, long Key)> seen)
    {
        if (read(items[row]) is IEnumerable<TItem> values)
        {
            foreach (TItem item in values)
            {
                int code = coder.Code(item);
                if (code != 0)
                {
                    seen.Add((group, code));
                }
            }
        }
    }
}
