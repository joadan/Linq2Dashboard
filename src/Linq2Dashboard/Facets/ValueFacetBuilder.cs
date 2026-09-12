using Linq2Dashboard.Facets;

namespace Linq2Dashboard;

/// <summary>Fluent configuration of a value or boolean facet (design §2.1).</summary>
public sealed class ValueFacetBuilder<T, TProp>
{
    private readonly ValueFacetDefinition<T, TProp> _definition;
    private readonly Action _ensureMutable;

    internal ValueFacetBuilder(ValueFacetDefinition<T, TProp> definition, Action ensureMutable)
    {
        _definition = definition;
        _ensureMutable = ensureMutable;
    }

    public string Key => _definition.Key;

    /// <summary>Display name. Defaults to the key.</summary>
    public ValueFacetBuilder<T, TProp> Title(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        _ensureMutable();
        _definition.Title = title;
        return this;
    }

    /// <summary>Present only the top <paramref name="count"/> values plus "Other" (concept §6).</summary>
    public ValueFacetBuilder<T, TProp> Top(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        _ensureMutable();
        _definition.Top = count;
        return this;
    }

    /// <summary>How the top N are ranked (concept §6). Default is by filtered count.</summary>
    public ValueFacetBuilder<T, TProp> RankBy(RankMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        _ensureMutable();
        _definition.RankMode = mode;
        return this;
    }

    /// <summary>Expose a search over the facet's values (concept §4.5, §6).</summary>
    public ValueFacetBuilder<T, TProp> Searchable(bool searchable = true)
    {
        _ensureMutable();
        _definition.Searchable = searchable;
        return this;
    }

    /// <summary>
    /// Value equality for this facet. Defaults to ordinal ignore-case for strings and default
    /// equality otherwise (design §3.3).
    /// </summary>
    public ValueFacetBuilder<T, TProp> Comparer(IEqualityComparer<TProp> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        _ensureMutable();
        _definition.Comparer = comparer;
        return this;
    }
}
