using System.Globalization;

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
/// One numeric interval of a <see cref="RangeSelection"/>. Null bounds are unbounded. Both ends are
/// inclusive by default; a bucket click sets <see cref="ToInclusive"/> false so adjacent buckets never
/// both claim their shared edge (design §2.2).
/// </summary>
public sealed record RangeInterval
{
    /// <summary>Creates an interval. Throws when a bound is NaN or <paramref name="from"/> is after <paramref name="to"/>.</summary>
    public RangeInterval(double? from, double? to, bool fromInclusive = true, bool toInclusive = true)
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
    }

    /// <summary>Lower bound; null means unbounded below.</summary>
    public double? From { get; init; }

    /// <summary>Upper bound; null means unbounded above.</summary>
    public double? To { get; init; }

    /// <summary>Whether a row equal to <see cref="From"/> matches. Default true.</summary>
    public bool FromInclusive { get; init; }

    /// <summary>Whether a row equal to <see cref="To"/> matches. Default true; a bucket click sets it false.</summary>
    public bool ToInclusive { get; init; }

    /// <summary>Closed interval <c>[from, to]</c>.</summary>
    public static RangeInterval Between(double from, double to) => new(from, to);

    /// <summary>Interval <c>[from, ∞)</c>.</summary>
    public static RangeInterval AtLeast(double from) => new(from, null);

    /// <summary>Interval <c>(-∞, to]</c>.</summary>
    public static RangeInterval AtMost(double to) => new(null, to);

    /// <inheritdoc />
    public override string ToString() =>
        (FromInclusive ? "[" : "(")
        + (From?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
        + ".."
        + (To?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
        + (ToInclusive ? "]" : ")");
}

/// <summary>
/// A set of numeric intervals for a range facet, combined as OR (concept §4.1, §5), with or without the
/// null rows (concept §4.8). A bucket click toggles one <see cref="RangeInterval"/>; a slider replaces the
/// set with one. Order is irrelevant to equality. With no interval and <see cref="IncludeNull"/> false the
/// selection <see cref="IsEmpty"/> and clears the facet when applied.
/// </summary>
public sealed record RangeSelection : Selection
{
    private readonly RangeInterval[] intervals;

    /// <summary>Creates a selection of <paramref name="intervals"/>, duplicates dropped, plus the null rows when <paramref name="includeNull"/>.</summary>
    public RangeSelection(IEnumerable<RangeInterval> intervals, bool includeNull = false)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        this.intervals = intervals.Distinct().ToArray();
        if (Array.IndexOf(this.intervals, null) >= 0)
        {
            throw new ArgumentException("An interval must not be null.", nameof(intervals));
        }

        IncludeNull = includeNull;
    }

    /// <summary>Creates a selection of one interval. Throws when a bound is NaN or <paramref name="from"/> is after <paramref name="to"/>.</summary>
    public RangeSelection(double? from, double? to, bool fromInclusive = true, bool toInclusive = true, bool includeNull = false)
        : this([new RangeInterval(from, to, fromInclusive, toInclusive)], includeNull)
    {
    }

    /// <summary>The intervals, distinct, in no particular order.</summary>
    public IReadOnlyList<RangeInterval> Intervals => intervals;

    /// <summary>Whether rows without a value match in addition to the intervals (concept §4.8).</summary>
    public bool IncludeNull { get; init; }

    /// <summary>True when nothing is selected, neither an interval nor the null rows; applying it clears the facet.</summary>
    public bool IsEmpty => intervals.Length == 0 && !IncludeNull;

    /// <summary>True when the selection is the null rows alone, with no interval.</summary>
    public bool OnlyNulls => intervals.Length == 0 && IncludeNull;

    /// <summary>No interval and no null rows: the starting point for toggling.</summary>
    public static RangeSelection Empty { get; } = new([]);

    /// <summary>Closed interval <c>[from, to]</c>.</summary>
    public static RangeSelection Between(double from, double to) => new(from, to);

    /// <summary>Interval <c>[from, ∞)</c>.</summary>
    public static RangeSelection AtLeast(double from) => new(from, null);

    /// <summary>Interval <c>(-∞, to]</c>.</summary>
    public static RangeSelection AtMost(double to) => new(null, to);

    /// <summary>Selects only the null rows.</summary>
    public static RangeSelection OnlyNull { get; } = new([], includeNull: true);

    /// <summary>True when <paramref name="interval"/> is one of the selected intervals, by value.</summary>
    public bool Contains(RangeInterval interval) => Array.IndexOf(intervals, interval) >= 0;

    /// <summary>A selection with <paramref name="interval"/> added; this instance when it is already present.</summary>
    public RangeSelection Add(RangeInterval interval)
    {
        ArgumentNullException.ThrowIfNull(interval);
        return Contains(interval) ? this : new(intervals.Append(interval), IncludeNull);
    }

    /// <summary>A selection with <paramref name="interval"/> removed; this instance when it is absent.</summary>
    public RangeSelection Remove(RangeInterval interval) =>
        Contains(interval) ? new(intervals.Where(i => !i.Equals(interval)), IncludeNull) : this;

    /// <summary>The click on a bucket: <paramref name="interval"/> added if absent, removed if present.</summary>
    public RangeSelection Toggle(RangeInterval interval) => Contains(interval) ? Remove(interval) : Add(interval);

    /// <summary>The click on the null value: the null rows added if absent, removed if present.</summary>
    public RangeSelection ToggleNull() => this with { IncludeNull = !IncludeNull };

    /// <summary>Order-independent value equality: the same intervals and the same null flag.</summary>
    public bool Equals(RangeSelection? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (IncludeNull != other.IncludeNull || intervals.Length != other.intervals.Length)
        {
            return false;
        }

        foreach (RangeInterval interval in intervals)
        {
            if (!other.Contains(interval))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        // Order-independent: XOR of element hashes, plus the count and the null flag.
        int hash = intervals.Length * 2 + (IncludeNull ? 1 : 0);
        foreach (RangeInterval interval in intervals)
        {
            hash ^= interval.GetHashCode();
        }

        return hash;
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"RangeSelection({string.Join(", ", intervals.Select(i => i.ToString()).Concat(IncludeNull ? ["null"] : []))})";
}

/// <summary>
/// One part of a <see cref="DateSelection"/>: either an absolute half-open instant interval <c>[From, To)</c>
/// or a named relative preset resolved at calculation time (concept §5, design §2.2).
/// </summary>
public sealed record DateInterval
{
    private DateInterval()
    {
    }

    /// <summary>Inclusive start instant; null means unbounded.</summary>
    public DateTimeOffset? From { get; private init; }

    /// <summary>Exclusive end instant; null means unbounded.</summary>
    public DateTimeOffset? To { get; private init; }

    /// <summary>The relative preset, when this part is relative rather than absolute.</summary>
    public DatePreset? Preset { get; private init; }

    /// <summary>True when this is a preset resolved at each calculation rather than a fixed interval.</summary>
    public bool IsRelative => Preset is not null;

    /// <summary>Absolute interval <c>[from, to)</c>. Either bound may be null for unbounded.</summary>
    public static DateInterval Between(DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from is DateTimeOffset f && to is DateTimeOffset t && f > t)
        {
            throw new ArgumentException($"Interval start {f:O} is after its end {t:O}.", nameof(from));
        }

        return new DateInterval { From = from, To = to };
    }

    /// <summary>A relative preset such as "last 30 days", resolved against the clock at each calculation (concept §5).</summary>
    public static DateInterval Relative(DatePreset preset)
    {
        if (!Enum.IsDefined(preset))
        {
            throw new ArgumentOutOfRangeException(nameof(preset));
        }

        return new DateInterval { Preset = preset };
    }

    /// <inheritdoc />
    public override string ToString() => Preset is DatePreset preset
        ? preset.ToString()
        : $"[{From?.ToString("O", CultureInfo.InvariantCulture)}..{To?.ToString("O", CultureInfo.InvariantCulture)})";
}

/// <summary>
/// A set of date parts for a date facet, combined as OR (concept §4.1, §5), with or without the null rows
/// (concept §4.8). Each <see cref="DateInterval"/> is an absolute interval or a relative preset; a bucket or
/// preset click toggles one. Order is irrelevant to equality. With no part and <see cref="IncludeNull"/>
/// false the selection <see cref="IsEmpty"/> and clears the facet when applied.
/// </summary>
public sealed record DateSelection : Selection
{
    private readonly DateInterval[] intervals;

    /// <summary>Creates a selection of <paramref name="intervals"/>, duplicates dropped, plus the null rows when <paramref name="includeNull"/>.</summary>
    public DateSelection(IEnumerable<DateInterval> intervals, bool includeNull = false)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        this.intervals = intervals.Distinct().ToArray();
        if (Array.IndexOf(this.intervals, null) >= 0)
        {
            throw new ArgumentException("An interval must not be null.", nameof(intervals));
        }

        IncludeNull = includeNull;
    }

    /// <summary>The parts, distinct, in no particular order.</summary>
    public IReadOnlyList<DateInterval> Intervals => intervals;

    /// <summary>Whether rows without a value match in addition to the parts (concept §4.8).</summary>
    public bool IncludeNull { get; init; }

    /// <summary>True when nothing is selected, neither a part nor the null rows; applying it clears the facet.</summary>
    public bool IsEmpty => intervals.Length == 0 && !IncludeNull;

    /// <summary>True when the selection is the null rows alone, with no part.</summary>
    public bool OnlyNulls => intervals.Length == 0 && IncludeNull;

    /// <summary>No part and no null rows: the starting point for toggling.</summary>
    public static DateSelection Empty { get; } = new([]);

    /// <summary>One absolute interval <c>[from, to)</c>. Either bound may be null for unbounded.</summary>
    public static DateSelection Between(DateTimeOffset? from, DateTimeOffset? to) => new([DateInterval.Between(from, to)]);

    /// <summary>One relative preset such as "last 30 days", resolved against the clock at each calculation (concept §5).</summary>
    public static DateSelection Relative(DatePreset preset) => new([DateInterval.Relative(preset)]);

    /// <summary>Selects only the null rows.</summary>
    public static DateSelection OnlyNull { get; } = new([], includeNull: true);

    /// <summary>True when <paramref name="interval"/> is one of the selected parts, by value.</summary>
    public bool Contains(DateInterval interval) => Array.IndexOf(intervals, interval) >= 0;

    /// <summary>A selection with <paramref name="interval"/> added; this instance when it is already present.</summary>
    public DateSelection Add(DateInterval interval)
    {
        ArgumentNullException.ThrowIfNull(interval);
        return Contains(interval) ? this : new(intervals.Append(interval), IncludeNull);
    }

    /// <summary>A selection with <paramref name="interval"/> removed; this instance when it is absent.</summary>
    public DateSelection Remove(DateInterval interval) =>
        Contains(interval) ? new(intervals.Where(i => !i.Equals(interval)), IncludeNull) : this;

    /// <summary>The click on a bucket or preset: <paramref name="interval"/> added if absent, removed if present.</summary>
    public DateSelection Toggle(DateInterval interval) => Contains(interval) ? Remove(interval) : Add(interval);

    /// <summary>The click on the null value: the null rows added if absent, removed if present.</summary>
    public DateSelection ToggleNull() => this with { IncludeNull = !IncludeNull };

    /// <summary>Order-independent value equality: the same parts and the same null flag.</summary>
    public bool Equals(DateSelection? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (IncludeNull != other.IncludeNull || intervals.Length != other.intervals.Length)
        {
            return false;
        }

        foreach (DateInterval interval in intervals)
        {
            if (!other.Contains(interval))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        // Order-independent: XOR of element hashes, plus the count and the null flag.
        int hash = intervals.Length * 2 + (IncludeNull ? 1 : 0);
        foreach (DateInterval interval in intervals)
        {
            hash ^= interval.GetHashCode();
        }

        return hash;
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"DateSelection({string.Join(", ", intervals.Select(i => i.ToString()).Concat(IncludeNull ? ["null"] : []))})";
}
