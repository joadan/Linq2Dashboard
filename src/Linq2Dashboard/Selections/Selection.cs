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
/// null rows (concept §4.8). The set is kept in canonical form: sorted, disjoint, with overlapping and
/// adjacent intervals joined, so two neighbouring bars make one interval. A bucket click toggles coverage of
/// its interval: added and joined when not fully covered, carved out otherwise. A slider replaces the set with
/// one interval. With no interval and <see cref="IncludeNull"/> false the selection <see cref="IsEmpty"/> and
/// clears the facet when applied.
/// </summary>
public sealed record RangeSelection : Selection
{
    private readonly RangeInterval[] intervals;

    /// <summary>Creates a selection of <paramref name="intervals"/> in canonical form (sorted, overlapping and adjacent ones joined), plus the null rows when <paramref name="includeNull"/>.</summary>
    public RangeSelection(IEnumerable<RangeInterval> intervals, bool includeNull = false)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        RangeInterval[] given = intervals.Distinct().ToArray();
        if (Array.IndexOf(given, null) >= 0)
        {
            throw new ArgumentException("An interval must not be null.", nameof(intervals));
        }

        this.intervals = Normalise(given);
        IncludeNull = includeNull;
    }

    /// <summary>Creates a selection of one interval. Throws when a bound is NaN or <paramref name="from"/> is after <paramref name="to"/>.</summary>
    public RangeSelection(double? from, double? to, bool fromInclusive = true, bool toInclusive = true, bool includeNull = false)
        : this([new RangeInterval(from, to, fromInclusive, toInclusive)], includeNull)
    {
    }

    /// <summary>The intervals in canonical form: ascending, disjoint, none adjacent to the next.</summary>
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

    /// <summary>True when the selected intervals cover every point of <paramref name="interval"/>; a bar is selected on the same terms (concept §5).</summary>
    public bool Contains(RangeInterval interval)
    {
        ArgumentNullException.ThrowIfNull(interval);
        return Array.Exists(intervals, own => StartsNoLater(own, interval) && EndsNoEarlier(own, interval));
    }

    /// <summary>A selection with <paramref name="interval"/> added and joined to what it overlaps or touches; this instance when it is already covered.</summary>
    public RangeSelection Add(RangeInterval interval)
    {
        ArgumentNullException.ThrowIfNull(interval);
        return Contains(interval) ? this : new(intervals.Append(interval), IncludeNull);
    }

    /// <summary>A selection with every point of <paramref name="interval"/> removed, splitting an interval it sits inside; this instance when nothing overlaps it.</summary>
    public RangeSelection Remove(RangeInterval interval)
    {
        ArgumentNullException.ThrowIfNull(interval);
        var kept = new List<RangeInterval>(intervals.Length + 1);
        bool changed = false;
        foreach (RangeInterval own in intervals)
        {
            if (EndsBefore(own, interval) || EndsBefore(interval, own))
            {
                kept.Add(own);
                continue;
            }

            changed = true;
            if (interval.From is double start && !StartsNoLater(interval, own))
            {
                kept.Add(new RangeInterval(own.From, start, own.FromInclusive, !interval.FromInclusive));
            }

            if (interval.To is double end && !EndsNoEarlier(interval, own))
            {
                kept.Add(new RangeInterval(end, own.To, !interval.ToInclusive, own.ToInclusive));
            }
        }

        return changed ? new(kept, IncludeNull) : this;
    }

    /// <summary>The click on a bucket: <paramref name="interval"/> carved out when the selection covers it, added and joined otherwise (concept §5).</summary>
    public RangeSelection Toggle(RangeInterval interval) => Contains(interval) ? Remove(interval) : Add(interval);

    /// <summary>The click on the null value: the null rows added if absent, removed if present.</summary>
    public RangeSelection ToggleNull() => this with { IncludeNull = !IncludeNull };

    /// <summary>Value equality: the same canonical intervals and the same null flag.</summary>
    public bool Equals(RangeSelection? other) =>
        other is not null
        && (ReferenceEquals(this, other) || (IncludeNull == other.IncludeNull && intervals.AsSpan().SequenceEqual(other.intervals)));

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(IncludeNull);
        foreach (RangeInterval interval in intervals)
        {
            hash.Add(interval);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"RangeSelection({string.Join(", ", intervals.Select(i => i.ToString()).Concat(IncludeNull ? ["null"] : []))})";

    /// <summary>Sorts by start and joins every interval that overlaps or touches the next, so the result is ascending and disjoint.</summary>
    private static RangeInterval[] Normalise(RangeInterval[] given)
    {
        if (given.Length < 2)
        {
            return given;
        }

        Array.Sort(given, CompareStarts);
        var result = new List<RangeInterval>(given.Length);
        RangeInterval current = given[0];
        for (int i = 1; i < given.Length; i++)
        {
            RangeInterval next = given[i];
            if (Touches(current, next))
            {
                current = Join(current, next);
            }
            else
            {
                result.Add(current);
                current = next;
            }
        }

        result.Add(current);
        return result.ToArray();
    }

    /// <summary>Unbounded starts first, then by value; at the same value an inclusive start comes before an exclusive one.</summary>
    private static int CompareStarts(RangeInterval a, RangeInterval b)
    {
        if (a.From is null || b.From is null)
        {
            return (a.From is null ? 0 : 1) - (b.From is null ? 0 : 1);
        }

        int byValue = a.From.Value.CompareTo(b.From.Value);
        return byValue != 0 ? byValue : (a.FromInclusive ? 0 : 1) - (b.FromInclusive ? 0 : 1);
    }

    /// <summary>Whether <paramref name="next"/>, which starts no earlier than <paramref name="current"/>, overlaps or abuts it so the two are one interval.</summary>
    private static bool Touches(RangeInterval current, RangeInterval next) =>
        current.To is null
        || next.From is null
        || next.From < current.To
        || (next.From == current.To && (next.FromInclusive || current.ToInclusive));

    /// <summary>The interval from <paramref name="current"/>'s start to the later of the two ends.</summary>
    private static RangeInterval Join(RangeInterval current, RangeInterval next)
    {
        bool nextEndsLater = current.To is not null
            && (next.To is null || next.To > current.To || (next.To == current.To && next.ToInclusive));
        return nextEndsLater ? new RangeInterval(current.From, next.To, current.FromInclusive, next.ToInclusive) : current;
    }

    /// <summary>Whether every point of <paramref name="inner"/> lies at or after the start of <paramref name="outer"/>.</summary>
    private static bool StartsNoLater(RangeInterval outer, RangeInterval inner) =>
        outer.From is null
        || (inner.From is double start && (outer.From < start || (outer.From == start && (outer.FromInclusive || !inner.FromInclusive))));

    /// <summary>Whether every point of <paramref name="inner"/> lies at or before the end of <paramref name="outer"/>.</summary>
    private static bool EndsNoEarlier(RangeInterval outer, RangeInterval inner) =>
        outer.To is null
        || (inner.To is double end && (outer.To > end || (outer.To == end && (outer.ToInclusive || !inner.ToInclusive))));

    /// <summary>Whether <paramref name="a"/> ends before <paramref name="b"/> starts, sharing no point.</summary>
    private static bool EndsBefore(RangeInterval a, RangeInterval b) =>
        a.To is double end && b.From is double start && (end < start || (end == start && !(a.ToInclusive && b.FromInclusive)));
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
/// (concept §4.8). Each <see cref="DateInterval"/> is an absolute interval or a relative preset. The absolute
/// intervals are kept in canonical form: sorted, disjoint, with overlapping and adjacent ones joined, so two
/// neighbouring bars make one interval; presets are parts of their own and never merge, because they resolve
/// only at calculation time. A bucket click toggles coverage of its interval, a preset click toggles the preset.
/// With no part and <see cref="IncludeNull"/> false the selection <see cref="IsEmpty"/> and clears the facet when applied.
/// </summary>
public sealed record DateSelection : Selection
{
    private readonly DateInterval[] intervals;

    /// <summary>Creates a selection of <paramref name="intervals"/> in canonical form (absolute intervals sorted, overlapping and adjacent ones joined, presets distinct after them), plus the null rows when <paramref name="includeNull"/>.</summary>
    public DateSelection(IEnumerable<DateInterval> intervals, bool includeNull = false)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        DateInterval[] given = intervals.Distinct().ToArray();
        if (Array.IndexOf(given, null) >= 0)
        {
            throw new ArgumentException("An interval must not be null.", nameof(intervals));
        }

        this.intervals = Normalise(given);
        IncludeNull = includeNull;
    }

    /// <summary>The parts in canonical form: absolute intervals ascending and disjoint, then the presets.</summary>
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

    /// <summary>True when <paramref name="interval"/> is a selected preset, or an absolute interval every instant of which a selected absolute interval covers (concept §5).</summary>
    public bool Contains(DateInterval interval)
    {
        ArgumentNullException.ThrowIfNull(interval);
        return interval.IsRelative
            ? Array.IndexOf(intervals, interval) >= 0
            : Array.Exists(intervals, own => !own.IsRelative && Covers(own, interval));
    }

    /// <summary>A selection with <paramref name="interval"/> added, an absolute one joined to what it overlaps or touches; this instance when it is already covered.</summary>
    public DateSelection Add(DateInterval interval)
    {
        ArgumentNullException.ThrowIfNull(interval);
        return Contains(interval) ? this : new(intervals.Append(interval), IncludeNull);
    }

    /// <summary>A selection without <paramref name="interval"/>: a preset removed by value, an absolute interval's instants carved out of the absolute parts, splitting one they sit inside; this instance when nothing changes.</summary>
    public DateSelection Remove(DateInterval interval)
    {
        ArgumentNullException.ThrowIfNull(interval);
        if (interval.IsRelative)
        {
            return Array.IndexOf(intervals, interval) >= 0 ? new(intervals.Where(i => !i.Equals(interval)), IncludeNull) : this;
        }

        var kept = new List<DateInterval>(intervals.Length + 1);
        bool changed = false;
        foreach (DateInterval own in intervals)
        {
            if (own.IsRelative || EndsBefore(own, interval) || EndsBefore(interval, own))
            {
                kept.Add(own);
                continue;
            }

            changed = true;
            if (interval.From is DateTimeOffset start && (own.From is null || own.From < start))
            {
                kept.Add(DateInterval.Between(own.From, start));
            }

            if (interval.To is DateTimeOffset end && (own.To is null || own.To > end))
            {
                kept.Add(DateInterval.Between(end, own.To));
            }
        }

        return changed ? new(kept, IncludeNull) : this;
    }

    /// <summary>The click on a bucket or preset: <paramref name="interval"/> carved out or removed when the selection contains it, added otherwise (concept §5).</summary>
    public DateSelection Toggle(DateInterval interval) => Contains(interval) ? Remove(interval) : Add(interval);

    /// <summary>The click on the null value: the null rows added if absent, removed if present.</summary>
    public DateSelection ToggleNull() => this with { IncludeNull = !IncludeNull };

    /// <summary>Value equality: the same canonical parts and the same null flag.</summary>
    public bool Equals(DateSelection? other) =>
        other is not null
        && (ReferenceEquals(this, other) || (IncludeNull == other.IncludeNull && intervals.AsSpan().SequenceEqual(other.intervals)));

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(IncludeNull);
        foreach (DateInterval interval in intervals)
        {
            hash.Add(interval);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"DateSelection({string.Join(", ", intervals.Select(i => i.ToString()).Concat(IncludeNull ? ["null"] : []))})";

    /// <summary>Absolute intervals sorted by start and joined where they overlap or abut, then the presets in declaration order.</summary>
    private static DateInterval[] Normalise(DateInterval[] given)
    {
        var result = new List<DateInterval>(given.Length);
        DateInterval? current = null;
        foreach (DateInterval next in given.Where(p => !p.IsRelative).OrderBy(p => p.From ?? DateTimeOffset.MinValue))
        {
            if (current is null)
            {
                current = next;
            }
            else if (current.To is null || next.From is null || next.From <= current.To)
            {
                bool nextEndsLater = current.To is not null && (next.To is null || next.To > current.To);
                current = nextEndsLater ? DateInterval.Between(current.From, next.To) : current;
            }
            else
            {
                result.Add(current);
                current = next;
            }
        }

        if (current is not null)
        {
            result.Add(current);
        }

        result.AddRange(given.Where(p => p.IsRelative).OrderBy(p => p.Preset));
        return result.ToArray();
    }

    /// <summary>Whether the absolute <paramref name="outer"/> holds every instant of the absolute <paramref name="inner"/>.</summary>
    private static bool Covers(DateInterval outer, DateInterval inner) =>
        (outer.From is null || (inner.From is DateTimeOffset start && outer.From <= start))
        && (outer.To is null || (inner.To is DateTimeOffset end && outer.To >= end));

    /// <summary>Whether the absolute <paramref name="a"/> ends at or before the absolute <paramref name="b"/> starts, sharing no instant.</summary>
    private static bool EndsBefore(DateInterval a, DateInterval b) =>
        a.To is DateTimeOffset end && b.From is DateTimeOffset start && end <= start;
}
