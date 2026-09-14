namespace Linq2Dashboard;

/// <summary>
/// What the user has chosen within one facet (concept §3). Small, immutable, serialisable, and
/// compared by value so it can key a cache (design §2.2, §5).
/// </summary>
public abstract record Selection
{
    private protected Selection()
    {
    }
}

/// <summary>
/// The text typed into a text facet (concept §5). Trimmed on construction; whitespace-only text is
/// <see cref="IsEmpty"/> and clears the facet when applied, like an empty value set. The core
/// never interprets the text: the facet's function decides what matches.
/// </summary>
public sealed record TextSelection : Selection
{
    /// <summary>Creates a selection for <paramref name="text"/>, trimmed. Whitespace-only text gives an empty selection.</summary>
    public TextSelection(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text.Trim();
    }

    /// <summary>The trimmed text.</summary>
    public string Text { get; }

    /// <summary>True when the trimmed text is empty; applying it clears the facet.</summary>
    public bool IsEmpty => Text.Length == 0;

    /// <inheritdoc />
    public override string ToString() => $"TextSelection(\"{Text}\")";
}

/// <summary>
/// A set of chosen values for a value or boolean facet. A <c>null</c> entry selects the null facet
/// value (concept §4.8). Values combine as OR (concept §4.1). Order is irrelevant to equality.
/// </summary>
public sealed record ValueSelection : Selection
{
    private readonly object?[] values;

    /// <summary>Creates a selection of <paramref name="values"/>; duplicates are dropped. A null entry selects the null facet value (concept §4.8).</summary>
    public ValueSelection(IEnumerable<object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        this.values = values.Distinct().ToArray();
    }

    /// <summary>A selection of the given values. A single <c>null</c> argument selects the null facet value.</summary>
    public static ValueSelection Of(params object?[]? values) => new(values ?? [null]);

    /// <summary>The selected values, distinct, in no particular order.</summary>
    public IReadOnlyList<object?> Values => values;

    /// <summary>True when no value is selected; applying it clears the facet.</summary>
    public bool IsEmpty => values.Length == 0;

    /// <summary>True when <paramref name="value"/> is selected, by default equality.</summary>
    public bool Contains(object? value) => Array.IndexOf(values, value) >= 0;

    /// <summary>A selection with <paramref name="value"/> added; this instance when it is already present.</summary>
    public ValueSelection Add(object? value) => Contains(value) ? this : new(values.Append(value));

    /// <summary>A selection with <paramref name="value"/> removed; this instance when it is absent.</summary>
    public ValueSelection Remove(object? value) => Contains(value) ? new(values.Where(v => !Equals(v, value))) : this;

    /// <summary>Order-independent value equality: the same set of values.</summary>
    public bool Equals(ValueSelection? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (values.Length != other.values.Length)
        {
            return false;
        }

        foreach (object? value in values)
        {
            if (!other.Contains(value))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        // Order-independent: XOR of element hashes, plus the count.
        int hash = values.Length;
        foreach (object? value in values)
        {
            hash ^= value?.GetHashCode() ?? 0x5bd1e995;
        }

        return hash;
    }

    /// <inheritdoc />
    public override string ToString() => $"ValueSelection({string.Join(", ", values.Select(v => v?.ToString() ?? "null"))})";
}

/// <summary>
/// A numeric interval for a range facet. Null bounds are unbounded. Both ends are inclusive by
/// default; a bucket click sets <see cref="ToInclusive"/> false so adjacent buckets never both claim
/// their shared edge (design §2.2). Null rows are excluded unless <see cref="IncludeNull"/>.
/// </summary>
public sealed record RangeSelection : Selection
{
    /// <summary>Creates an interval. Throws when a bound is NaN or <paramref name="from"/> is after <paramref name="to"/>.</summary>
    public RangeSelection(double? from, double? to, bool fromInclusive = true, bool toInclusive = true, bool includeNull = false)
    {
        if (from is double f && double.IsNaN(f))
        {
            throw new ArgumentException("Bound must not be NaN.", nameof(from));
        }

        if (to is double t && double.IsNaN(t))
        {
            throw new ArgumentException("Bound must not be NaN.", nameof(to));
        }

        if (from is double lo && to is double hi && lo > hi)
        {
            throw new ArgumentException($"Interval start {lo} is after its end {hi}.", nameof(from));
        }

        From = from;
        To = to;
        FromInclusive = fromInclusive;
        ToInclusive = toInclusive;
        IncludeNull = includeNull;
    }

    /// <summary>Lower bound; null means unbounded below.</summary>
    public double? From { get; init; }

    /// <summary>Upper bound; null means unbounded above.</summary>
    public double? To { get; init; }

    /// <summary>Whether a row equal to <see cref="From"/> matches. Default true.</summary>
    public bool FromInclusive { get; init; }

    /// <summary>Whether a row equal to <see cref="To"/> matches. Default true; a bucket click sets it false.</summary>
    public bool ToInclusive { get; init; }

    /// <summary>Whether rows without a value match in addition to the interval (concept §4.8).</summary>
    public bool IncludeNull { get; init; }

    /// <summary>Closed interval <c>[from, to]</c>.</summary>
    public static RangeSelection Between(double from, double to) => new(from, to);

    /// <summary>Interval <c>[from, ∞)</c>.</summary>
    public static RangeSelection AtLeast(double from) => new(from, null);

    /// <summary>Interval <c>(-∞, to]</c>.</summary>
    public static RangeSelection AtMost(double to) => new(null, to);

    /// <summary>Selects only the null rows.</summary>
    public static RangeSelection OnlyNull { get; } = new(null, null, includeNull: true) { OnlyNulls = true };

    /// <summary>True when the selection is the null rows alone, with no interval.</summary>
    public bool OnlyNulls { get; private init; }
}

/// <summary>
/// A date interval for a date facet: either an absolute half-open instant interval
/// <c>[From, To)</c> or a named relative preset resolved at calculation time (concept §5,
/// design §2.2). Null rows are excluded unless <see cref="IncludeNull"/>.
/// </summary>
public sealed record DateSelection : Selection
{
    private DateSelection()
    {
    }

    /// <summary>Inclusive start instant; null means unbounded.</summary>
    public DateTimeOffset? From { get; private init; }

    /// <summary>Exclusive end instant; null means unbounded.</summary>
    public DateTimeOffset? To { get; private init; }

    /// <summary>The relative preset, when this selection is relative rather than absolute.</summary>
    public DatePreset? Preset { get; private init; }

    /// <summary>Whether rows without a value match in addition to the interval (concept §4.8).</summary>
    public bool IncludeNull { get; init; }

    /// <summary>True when the selection is the null rows alone, with no interval.</summary>
    public bool OnlyNulls { get; private init; }

    /// <summary>True when this is a preset resolved at each calculation rather than a fixed interval.</summary>
    public bool IsRelative => Preset is not null;

    /// <summary>Absolute interval <c>[from, to)</c>. Either bound may be null for unbounded.</summary>
    public static DateSelection Between(DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from is DateTimeOffset f && to is DateTimeOffset t && f > t)
        {
            throw new ArgumentException($"Interval start {f:O} is after its end {t:O}.", nameof(from));
        }

        return new DateSelection { From = from, To = to };
    }

    /// <summary>A relative preset such as "last 30 days", resolved against the clock at each calculation (concept §5).</summary>
    public static DateSelection Relative(DatePreset preset)
    {
        if (!Enum.IsDefined(preset))
        {
            throw new ArgumentOutOfRangeException(nameof(preset));
        }

        return new DateSelection { Preset = preset };
    }

    /// <summary>Selects only the null rows.</summary>
    public static DateSelection OnlyNull { get; } = new() { OnlyNulls = true, IncludeNull = true };
}
