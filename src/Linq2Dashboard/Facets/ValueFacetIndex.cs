using System.Globalization;
using System.Text.Json.Nodes;
using Linq2Dashboard.Indexing;
using Linq2Dashboard.Serialization;

namespace Linq2Dashboard.Facets;

/// <summary>
/// Built value, boolean or multi-valued facet: the dictionary-encoded column plus presentation
/// options. The kinds differ only in the column behind <see cref="IValueColumn{TValue}"/> and in
/// "Other", which a multi-valued facet never has (concept §5, §6).
/// </summary>
internal sealed class ValueFacetIndex<TValue> : FacetIndex
{
    private readonly Lazy<string[]> searchLabels;
    private readonly ValueFormatter<TValue>? formatter;
    private readonly string?[]? labels;
    private readonly int[] totals;
    private readonly int valueCount;

    public ValueFacetIndex(
        string key, string name, FacetKind kind, IValueColumn<TValue> column,
        int? top, RankMode rankMode, bool searchable, ValueFormatter<TValue>? formatter, string?[]? labels = null)
        : this(key, name, kind, column, column.TotalCountsArray, column.RowCount, top, rankMode, searchable, formatter, Validate(column, labels), null)
    {
    }

    private ValueFacetIndex(
        string key, string name, FacetKind kind, IValueColumn<TValue> column, int[] totals, int rowCount,
        int? top, RankMode rankMode, bool searchable, ValueFormatter<TValue>? formatter, string?[]? labels, Lazy<string[]>? searchLabels)
        : base(key, name, kind, rowCount)
    {
        Column = column;
        this.totals = totals;
        Top = top;
        RankMode = rankMode;
        Searchable = searchable;
        this.formatter = formatter;
        this.labels = labels;
        this.searchLabels = searchLabels ?? new Lazy<string[]>(BuildSearchLabels, LazyThreadSafetyMode.ExecutionAndPublication);
        foreach (int total in totals)
        {
            if (total > 0)
            {
                valueCount++;
            }
        }
    }

    /// <summary>Whether the builder defined a label selector for this facet.</summary>
    public bool HasLabels => labels is not null;

    /// <summary>
    /// The application-defined label of a value (concept §5), or null when the facet has no label
    /// selector, the value is null, the value does not occur, or the selector returned null for it.
    /// Accepts the same boxed forms as a selection does.
    /// </summary>
    public string? LabelOf(object? value)
    {
        if (labels is null || value is null)
        {
            return null;
        }

        return Column.TryGetCode(ConvertValue(value), out int code) ? labels[code - 1] : null;
    }

    public IValueColumn<TValue> Column { get; }

    public int? Top { get; }

    public RankMode RankMode { get; }

    public bool Searchable { get; }

    /// <summary>Number of facet values: distinct non-null values in the dataset plus one when it has nulls (concept §4.8). In a scope, only values some row in the scope has (concept §4.10).</summary>
    public int ValueCount => valueCount;

    /// <summary>
    /// Rows having any of the selected values (concept §4.1). A null entry selects the null rows.
    /// A value that does not occur in the dataset matches nothing rather than failing, so a stale
    /// bookmark degrades to fewer rows (design §2.5). A value of the wrong type is an error.
    /// </summary>
    public override RowSet RowsMatching(Selection selection) =>
        Column.RowsWithCodes(SelectedCodes(Expect<ValueSelection>(selection)).ToArray());

    /// <inheritdoc />
    public override FacetIndex Scope(RowSet scope)
    {
        var scoped = new int[Column.DistinctCount + 1];
        Column.CountInto(scope, scoped);
        return new ValueFacetIndex<TValue>(Key, Name, Kind, Column, scoped, scope.Count, Top, RankMode, Searchable, formatter, labels, searchLabels);
    }

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
            presented = selected.Where(code => totals[code] > 0).ToList();
            int remaining = Math.Max(0, top - presented.Count);
            var heap = new PriorityQueue<int, RankKey>(WorstFirst);
            for (int code = 0; code <= Column.DistinctCount; code++)
            {
                if (totals[code] == 0 || selected.Contains(code))
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

            // A multi-valued facet has no "Other": rows overlap, so a remainder cannot be computed by subtraction (concept §6).
            if (presented.Count < valueCount && Kind != FacetKind.MultiValue)
            {
                int presentedTotal = 0;
                int presentedFiltered = 0;
                foreach (int code in presented)
                {
                    presentedTotal += totals[code];
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
                if (totals[code] > 0)
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
            Key, Name, Kind, selection, context.Count,
            facetValues, other, valueCount, Searchable,
            (text, max) => Search(text, max, counts, selected), LabelOf);
    }

    /// <summary>Design §2.5: <c>{ "values": [ ... ] }</c>, null written as JSON null.</summary>
    public override JsonObject Serialize(Selection selection)
    {
        ValueSelection values = Expect<ValueSelection>(selection);
        var array = new JsonArray();
        foreach (object? value in values.Values)
        {
            array.Add(FormatValue(value));
        }

        return new JsonObject { ["values"] = array };
    }

    /// <summary>Reads <c>{ "values": [ ... ] }</c>; entries that cannot be read are dropped, and a wrong shape yields null.</summary>
    public override Selection? Deserialize(JsonObject json)
    {
        if (!json.TryGetPropertyValue("values", out JsonNode? node) || node is not JsonArray array)
        {
            return null;
        }

        var values = new List<object?>();
        foreach (JsonNode? element in array)
        {
            if (TryParseValue(element, out object? value))
            {
                values.Add(value);
            }
        }

        return values.Count == 0 ? null : new ValueSelection(values);
    }

    /// <summary>Writes <c>v1,v2,null</c>: each value in its JSON text form, escaped for the list (design §2.5).</summary>
    public override string SerializeQuery(Selection selection)
    {
        ValueSelection values = Expect<ValueSelection>(selection);
        return string.Join(',', values.Values.Select(v => v is null ? QueryValues.NullToken : QueryValues.EscapeToken(FormatValueText(v))));
    }

    /// <summary>Reads a comma-separated list; tokens that cannot be read are dropped, and nothing readable yields null.</summary>
    public override Selection? DeserializeQuery(string value)
    {
        var values = new List<object?>();
        foreach (string? token in QueryValues.SplitTokens(value))
        {
            if (TryParseValue(token is null ? null : JsonValue.Create(token), out object? parsed))
            {
                values.Add(parsed);
            }
        }

        return values.Count == 0 ? null : new ValueSelection(values);
    }

    /// <summary>A facet value as the text a query string carries: the JSON string itself, or the JSON text of a number or boolean.</summary>
    private string FormatValueText(object value)
    {
        JsonNode? node = FormatValue(value);
        return node is JsonValue json && json.GetValueKind() == System.Text.Json.JsonValueKind.String
            ? json.GetValue<string>()
            : node?.ToJsonString() ?? QueryValues.NullToken;
    }

    /// <summary>A facet value as JSON: through the application's formatter when given, otherwise the defaults.</summary>
    internal JsonNode? FormatValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (formatter is not null)
        {
            return JsonValue.Create(formatter.Format(ConvertValue(value)));
        }

        return JsonValues.Format(ConvertValue(value));
    }

    /// <summary>A JSON node as a facet value of <typeparamref name="TValue"/>; false when it cannot be read.</summary>
    internal bool TryParseValue(JsonNode? node, out object? value)
    {
        value = null;
        if (node is null)
        {
            return true;
        }

        if (!JsonValues.TryToPrimitive(node, out object? primitive) || primitive is null)
        {
            return false;
        }

        try
        {
            if (formatter is not null)
            {
                if (primitive is not string text)
                {
                    return false;
                }

                value = formatter.Parse(text);
                return true;
            }

            value = ConvertValue(primitive);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or FormatException or OverflowException or InvalidCastException)
        {
            return false;
        }
    }

    /// <summary>Design §4.4 search: values whose label contains the text, ranked, limited. Never the null value.</summary>
    private IReadOnlyList<FacetValue> Search(string text, int max, int[] counts, HashSet<int> selected)
    {
        string[] all = searchLabels.Value;
        var heap = new PriorityQueue<int, RankKey>(WorstFirst);
        for (int code = 1; code <= Column.DistinctCount; code++)
        {
            if (totals[code] > 0 && all[code - 1].Contains(text, StringComparison.OrdinalIgnoreCase))
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
        new(code == 0 ? null : Column.ValueOf(code), totals[code], counts[code], selected.Contains(code),
            code == 0 || labels is null ? null : labels[code - 1]);

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
        int total = totals[code];
        int order = code == 0 ? int.MaxValue : code;
        return RankMode == RankMode.TotalCount
            ? new RankKey(total, filtered, order)
            : new RankKey(filtered, total, order);
    }

    /// <summary>What search matches: the application's label when one is defined for the value, otherwise the value's invariant text (design §3.3).</summary>
    private string[] BuildSearchLabels()
    {
        var result = new string[Column.DistinctCount];
        for (int code = 1; code <= Column.DistinctCount; code++)
        {
            result[code - 1] = labels?[code - 1] ?? Convert.ToString(Column.ValueOf(code), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return result;
    }

    /// <summary>
    /// Brings a boxed selection value to <typeparamref name="TValue"/>. Exact matches pass through;
    /// primitives, decimals, strings, enums, <see cref="Guid"/> and the date and time types are
    /// converted, so a value that arrived as a <see cref="long"/> or a <see cref="string"/> (JSON)
    /// still selects an <see cref="int"/>, enum or date facet value.
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

            if (value is string s)
            {
                object? parsed = ParseText(s, target);
                if (parsed is not null)
                {
                    return (TValue)parsed;
                }
            }

            // Booleans and numbers do not convert into each other: JSON true for an int facet is a mistake, not a 1.
            bool boolInvolved = value is bool || target == typeof(bool);
            bool boolMismatch = boolInvolved && !(value is bool && target == typeof(bool)) && value is not string && target != typeof(string);
            if (!boolMismatch && value is IConvertible && (target.IsPrimitive || target == typeof(decimal) || target == typeof(string)))
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

    /// <summary>Text forms of the non-primitive value types the default formatter writes.</summary>
    private static object? ParseText(string text, Type target)
    {
        var culture = CultureInfo.InvariantCulture;
        const DateTimeStyles styles = DateTimeStyles.RoundtripKind;
        if (target == typeof(Guid))
        {
            return Guid.Parse(text);
        }

        if (target == typeof(DateTimeOffset))
        {
            return DateTimeOffset.Parse(text, culture, styles);
        }

        if (target == typeof(DateTime))
        {
            return DateTime.Parse(text, culture, styles);
        }

        if (target == typeof(DateOnly))
        {
            return DateOnly.Parse(text, culture);
        }

        if (target == typeof(TimeOnly))
        {
            return TimeOnly.Parse(text, culture);
        }

        return null;
    }

    private static string?[]? Validate(IValueColumn<TValue> column, string?[]? labels)
    {
        if (labels is not null && labels.Length != column.DistinctCount)
        {
            throw new ArgumentException("One label per distinct value is required.", nameof(labels));
        }

        return labels;
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
