using System.Collections;
using System.Collections.Immutable;

namespace Linq2Dashboard;

/// <summary>
/// The user's current selections: an immutable map from facet key to <see cref="Selection"/>
/// (design §2.2). This is the only thing the UI sends back to the engine. Compared by value so it
/// can key the state cache (design §5). A facet with no entry has no constraint (concept §4.1).
/// </summary>
public sealed class Selections : IEquatable<Selections>, IEnumerable<KeyValuePair<string, Selection>>
{
    private readonly ImmutableSortedDictionary<string, Selection> map;

    private Selections(ImmutableSortedDictionary<string, Selection> map)
    {
        this.map = map;
    }

    /// <summary>No selections at all: every facet unconstrained. The starting point for every UI.</summary>
    public static Selections Empty { get; } = new(ImmutableSortedDictionary.Create<string, Selection>(StringComparer.Ordinal));

    /// <summary>Number of facets with a selection.</summary>
    public int Count => map.Count;

    /// <summary>True when no facet has a selection.</summary>
    public bool IsEmpty => map.IsEmpty;

    /// <summary>Keys of the facets with a selection, in ordinal order.</summary>
    public IEnumerable<string> Keys => map.Keys;

    /// <summary>The selection for <paramref name="key"/>, or null when the facet is unconstrained.</summary>
    public Selection? this[string key] => map.TryGetValue(key, out Selection? selection) ? selection : null;

    /// <summary>Gets the selection for <paramref name="key"/>; false when the facet is unconstrained.</summary>
    public bool TryGet(string key, out Selection selection) => map.TryGetValue(key, out selection!);

    /// <summary>True when the facet with <paramref name="key"/> has a selection.</summary>
    public bool Contains(string key) => map.ContainsKey(key);

    /// <summary>Replaces the selection for <paramref name="key"/>. An empty value or text selection clears it instead.</summary>
    public Selections With(string key, Selection selection)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(selection);

        if (selection is ValueSelection { IsEmpty: true } or TextSelection { IsEmpty: true })
        {
            return Clear(key);
        }

        return new Selections(map.SetItem(key, selection));
    }

    /// <summary>Removes the selection for <paramref name="key"/>, if any.</summary>
    public Selections Clear(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return map.ContainsKey(key) ? new Selections(map.Remove(key)) : this;
    }

    /// <summary>Removes every selection. The same as <see cref="Empty"/>.</summary>
    public Selections ClearAll() => Empty;

    /// <summary>
    /// Adds <paramref name="value"/> to the value selection for <paramref name="key"/>, or removes it
    /// if already present. The click case for value and boolean facets (design §2.2). Values are
    /// compared with default equality here; the facet's comparer applies when values become codes.
    /// </summary>
    public Selections Toggle(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        ValueSelection current = this[key] switch
        {
            null => ValueSelection.Of(),
            ValueSelection values => values,
            Selection other => throw new InvalidOperationException(
                $"Facet '{key}' has a {other.GetType().Name}; Toggle applies to value selections only."),
        };

        return With(key, current.Contains(value) ? current.Remove(value) : current.Add(value));
    }

    /// <summary>Value equality: the same keys with equal selections.</summary>
    public bool Equals(Selections? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (map.Count != other.map.Count)
        {
            return false;
        }

        foreach ((string key, Selection selection) in map)
        {
            if (!other.map.TryGetValue(key, out Selection? theirs) || !selection.Equals(theirs))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Selections);

    /// <summary>Value equality, the same as <see cref="Equals(Selections)"/>, so <c>a == b</c> never silently compares references.</summary>
    public static bool operator ==(Selections? left, Selections? right) => left is null ? right is null : left.Equals(right);

    /// <summary>The negation of <see cref="op_Equality"/>.</summary>
    public static bool operator !=(Selections? left, Selections? right) => !(left == right);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach ((string key, Selection selection) in map)
        {
            hash.Add(key, StringComparer.Ordinal);
            hash.Add(selection);
        }

        return hash.ToHashCode();
    }

    /// <summary>Enumerates the facet keys and their selections in ordinal key order.</summary>
    public IEnumerator<KeyValuePair<string, Selection>> GetEnumerator() => map.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public override string ToString() => IsEmpty
        ? "Selections(empty)"
        : $"Selections({string.Join("; ", map.Select(p => $"{p.Key}={p.Value}"))})";
}
