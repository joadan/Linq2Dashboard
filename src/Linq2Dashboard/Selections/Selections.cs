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
    private readonly ImmutableSortedDictionary<string, Selection> _map;

    private Selections(ImmutableSortedDictionary<string, Selection> map)
    {
        _map = map;
    }

    public static Selections Empty { get; } = new(ImmutableSortedDictionary.Create<string, Selection>(StringComparer.Ordinal));

    public int Count => _map.Count;

    public bool IsEmpty => _map.IsEmpty;

    public IEnumerable<string> Keys => _map.Keys;

    public Selection? this[string key] => _map.TryGetValue(key, out Selection? selection) ? selection : null;

    public bool TryGet(string key, out Selection selection) => _map.TryGetValue(key, out selection!);

    public bool Contains(string key) => _map.ContainsKey(key);

    /// <summary>Replaces the selection for <paramref name="key"/>. An empty value selection clears it instead.</summary>
    public Selections With(string key, Selection selection)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(selection);

        if (selection is ValueSelection { IsEmpty: true })
        {
            return Clear(key);
        }

        return new Selections(_map.SetItem(key, selection));
    }

    /// <summary>Removes the selection for <paramref name="key"/>, if any.</summary>
    public Selections Clear(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return _map.ContainsKey(key) ? new Selections(_map.Remove(key)) : this;
    }

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

        if (_map.Count != other._map.Count)
        {
            return false;
        }

        foreach ((string key, Selection selection) in _map)
        {
            if (!other._map.TryGetValue(key, out Selection? theirs) || !selection.Equals(theirs))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as Selections);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach ((string key, Selection selection) in _map)
        {
            hash.Add(key, StringComparer.Ordinal);
            hash.Add(selection);
        }

        return hash.ToHashCode();
    }

    public IEnumerator<KeyValuePair<string, Selection>> GetEnumerator() => _map.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => IsEmpty
        ? "Selections(empty)"
        : $"Selections({string.Join("; ", _map.Select(p => $"{p.Key}={p.Value}"))})";
}
