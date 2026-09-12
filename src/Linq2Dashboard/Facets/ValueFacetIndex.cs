using System.Globalization;
using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Facets;

/// <summary>Built value or boolean facet: the dictionary-encoded column plus presentation options.</summary>
internal sealed class ValueFacetIndex<TValue> : FacetIndex
{
    private readonly Lazy<string[]> _labels;

    public ValueFacetIndex(string key, string title, FacetKind kind, ValueColumn<TValue> column, int? top, RankMode rankMode, bool searchable)
        : base(key, title, kind, column.RowCount)
    {
        Column = column;
        Top = top;
        RankMode = rankMode;
        Searchable = searchable;
        _labels = new Lazy<string[]>(BuildLabels, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public ValueColumn<TValue> Column { get; }

    public int? Top { get; }

    public RankMode RankMode { get; }

    public bool Searchable { get; }

    /// <summary>Number of facet values: distinct non-null values plus one when the dataset has nulls (concept §4.8).</summary>
    public int ValueCount => Column.DistinctCount + (Column.HasNulls ? 1 : 0);

    /// <summary>
    /// Rows having any of the selected values (concept §4.1). A null entry selects the null rows.
    /// A value that does not occur in the dataset matches nothing rather than failing, so a stale
    /// bookmark degrades to fewer rows (design §2.5). A value of the wrong type is an error.
    /// </summary>
    public override RowSet RowsMatching(Selection selection) =>
        Column.RowsWithCodes(SelectedCodes(Expect<ValueSelection>(selection)).ToArray());

    /// <summary>Design §4.3 and §4.4: count, rank, pin selected values, fill to Top N, compute Other.</summary>
    public override FacetState Present(RowSet context, Selection? selection)
    {
        ValueSelection? values = ExpectOrNull<ValueSelection>(selection);
        var counts = new int[Column.DistinctCount + 1];
        Column.CountInto(context, counts);
        HashSet<int> selected = values is null ? [] : SelectedCodes(values);

        List<int> presented;
        FacetCount? other = null;
        int valueCount = ValueCount;
        if (Top is int top && valueCount > top)
        {
            presented = selected.Where(code => Column.TotalCounts[code] > 0).ToList();
            int remaining = Math.Max(0, top - presented.Count);
            var heap = new PriorityQueue<int, RankKey>(WorstFirst);
            for (int code = 0; code <= Column.DistinctCount; code++)
            {
                if (Column.TotalCounts[code] == 0 || selected.Contains(code))
                {
                    continue;
                }

                Offer(heap, code, RankOf(code, counts), remaining);
            }

            while (heap.Count > 0)
            {
                presented.Add(heap.Dequeue());
            }

            presented.Sort((a, b) => BestFirst.Compare(RankOf(a, counts), RankOf(b, counts)));

            if (presented.Count < valueCount)
            {
                int presentedTotal = 0;
                int presentedFiltered = 0;
                foreach (int code in presented)
                {
                    presentedTotal += Column.TotalCounts[code];
                    presentedFiltered += counts[code];
                }

                other = new FacetCount(RowCount - presentedTotal, context.Count - presentedFiltered);
            }
        }
        else
        {
            presented = new List<int>(valueCount);
            for (int code = 0; code <= Column.DistinctCount; code++)
            {
                if (Column.TotalCounts[code] > 0)
                {
                    presented.Add(code);
                }
            }

            presented.Sort((a, b) => BestFirst.Compare(RankOf(a, counts), RankOf(b, counts)));
        }

        var facetValues = new FacetValue[presented.Count];
        for (int i = 0; i < facetValues.Length; i++)
        {
            facetValues[i] = ToFacetValue(presented[i], counts, selected);
        }

        return new ValueFacetState(
            Key, Title, Kind, selection, context.Count,
            facetValues, other, valueCount, Searchable,
            (text, max) => Search(text, max, counts, selected));
    }

    /// <summary>Design §4.4 search: values whose label contains the text, ranked, limited. Never the null value.</summary>
    private IReadOnlyList<FacetValue> Search(string text, int max, int[] counts, HashSet<int> selected)
    {
        string[] labels = _labels.Value;
        var heap = new PriorityQueue<int, RankKey>(WorstFirst);
        for (int code = 1; code <= Column.DistinctCount; code++)
        {
            if (labels[code - 1].Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                Offer(heap, code, RankOf(code, counts), max);
            }
        }

        var found = new List<int>(heap.Count);
        while (heap.Count > 0)
        {
            found.Add(heap.Dequeue());
        }

        found.Sort((a, b) => BestFirst.Compare(RankOf(a, counts), RankOf(b, counts)));
        return found.Select(code => ToFacetValue(code, counts, selected)).ToArray();
    }

    private FacetValue ToFacetValue(int code, int[] counts, HashSet<int> selected) =>
        new(code == 0 ? null : Column.ValueOf(code), Column.TotalCounts[code], counts[code], selected.Contains(code));

    private HashSet<int> SelectedCodes(ValueSelection values)
    {
        var codes = new HashSet<int>();
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

        return codes;
    }

    private static void Offer(PriorityQueue<int, RankKey> heap, int code, RankKey rank, int capacity)
    {
        if (capacity == 0)
        {
            return;
        }

        if (heap.Count < capacity)
        {
            heap.Enqueue(code, rank);
        }
        else
        {
            heap.EnqueueDequeue(code, rank);
        }
    }

    /// <summary>Rank per concept §6: primary count per RankMode, the other count as tiebreaker, then dictionary order with null last.</summary>
    private RankKey RankOf(int code, int[] counts)
    {
        int filtered = counts[code];
        int total = Column.TotalCounts[code];
        int order = code == 0 ? int.MaxValue : code;
        return RankMode == RankMode.TotalCount
            ? new RankKey(total, filtered, order)
            : new RankKey(filtered, total, order);
    }

    private string[] BuildLabels()
    {
        var labels = new string[Column.DistinctCount];
        for (int code = 1; code <= Column.DistinctCount; code++)
        {
            labels[code - 1] = Convert.ToString(Column.ValueOf(code), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return labels;
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

    private readonly record struct RankKey(int Primary, int Secondary, int Order);

    private static readonly IComparer<RankKey> BestFirst = Comparer<RankKey>.Create((a, b) =>
    {
        int result = b.Primary.CompareTo(a.Primary);
        if (result == 0)
        {
            result = b.Secondary.CompareTo(a.Secondary);
        }

        return result == 0 ? a.Order.CompareTo(b.Order) : result;
    });

    /// <summary>Min-heap priority: the worst-ranked entry dequeues first.</summary>
    private static readonly IComparer<RankKey> WorstFirst = Comparer<RankKey>.Create((a, b) => BestFirst.Compare(b, a));
}
