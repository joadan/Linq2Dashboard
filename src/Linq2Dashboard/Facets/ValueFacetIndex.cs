using System.Globalization;
using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Facets;

/// <summary>Built value or boolean facet: the dictionary-encoded column plus presentation options.</summary>
internal sealed class ValueFacetIndex<TValue> : FacetIndex
{
    public ValueFacetIndex(string key, string title, FacetKind kind, ValueColumn<TValue> column, int? top, RankMode rankMode, bool searchable)
        : base(key, title, kind, column.RowCount)
    {
        Column = column;
        Top = top;
        RankMode = rankMode;
        Searchable = searchable;
    }

    public ValueColumn<TValue> Column { get; }

    public int? Top { get; }

    public RankMode RankMode { get; }

    public bool Searchable { get; }

    /// <summary>
    /// Rows having any of the selected values (concept §4.1). A null entry selects the null rows.
    /// A value that does not occur in the dataset matches nothing rather than failing, so a stale
    /// bookmark degrades to fewer rows (design §2.5). A value of the wrong type is an error.
    /// </summary>
    public override RowSet RowsMatching(Selection selection)
    {
        ValueSelection values = Expect<ValueSelection>(selection);
        var codes = new List<int>(values.Values.Count);
        foreach (object? value in values.Values)
        {
            if (value is null)
            {
                codes.Add(0);
            }
            else if (Column.TryGetCode(ConvertValue(value), out int code))
            {
                codes.Add(code);
            }
        }

        return Column.RowsWithCodes(codes.ToArray());
    }

    /// <summary>
    /// Brings a boxed selection value to <typeparamref name="TValue"/>. Exact matches pass through;
    /// primitives, decimals, strings and enums are converted so a value that arrived as a
    /// <see cref="long"/> or a <see cref="string"/> (JSON) still selects an <see cref="int"/> or enum facet value.
    /// </summary>
    internal TValue ConvertValue(object value)
    {
        if (value is TValue exact)
        {
            return exact;
        }

        Type target = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);
        try
        {
            if (target.IsEnum)
            {
                object converted = value is string text
                    ? Enum.Parse(target, text, ignoreCase: true)
                    : Enum.ToObject(target, Convert.ChangeType(value, Enum.GetUnderlyingType(target), CultureInfo.InvariantCulture));
                return (TValue)converted;
            }

            if (value is IConvertible && (target.IsPrimitive || target == typeof(decimal) || target == typeof(string)))
            {
                return (TValue)Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
            }
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            throw new ArgumentException(
                $"Facet '{Key}' holds {typeof(TValue).Name} values; '{value}' ({value.GetType().Name}) cannot be converted.", nameof(value), e);
        }

        throw new ArgumentException(
            $"Facet '{Key}' holds {typeof(TValue).Name} values; got a {value.GetType().Name}.", nameof(value));
    }
}
