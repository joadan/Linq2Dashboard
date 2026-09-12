using Linq2Dashboard.Facets;
using Linq2Dashboard.Serialization;

namespace Linq2Dashboard;

/// <summary>Fluent configuration of a value or boolean facet (design §2.1).</summary>
public sealed class ValueFacetBuilder<T, TProp>
{
    private readonly ValueFacetDefinition<T, TProp> definition;
    private readonly Action ensureMutable;

    internal ValueFacetBuilder(ValueFacetDefinition<T, TProp> definition, Action ensureMutable)
    {
        this.definition = definition;
        this.ensureMutable = ensureMutable;
    }

    public string Key => definition.Key;

    /// <summary>Display name. Defaults to the key.</summary>
    public ValueFacetBuilder<T, TProp> Title(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ensureMutable();
        definition.Title = title;
        return this;
    }

    /// <summary>Present only the top <paramref name="count"/> values plus "Other" (concept §6).</summary>
    public ValueFacetBuilder<T, TProp> Top(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ensureMutable();
        definition.Top = count;
        return this;
    }

    /// <summary>How the top N are ranked (concept §6). Default is by filtered count.</summary>
    public ValueFacetBuilder<T, TProp> RankBy(RankMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        ensureMutable();
        definition.RankMode = mode;
        return this;
    }

    /// <summary>Expose a search over the facet's values (concept §4.5, §6).</summary>
    public ValueFacetBuilder<T, TProp> Searchable(bool searchable = true)
    {
        ensureMutable();
        definition.Searchable = searchable;
        return this;
    }

    /// <summary>
    /// Value equality for this facet. Defaults to ordinal ignore-case for strings and default
    /// equality otherwise (design §3.3).
    /// </summary>
    public ValueFacetBuilder<T, TProp> Comparer(IEqualityComparer<TProp> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        ensureMutable();
        definition.Comparer = comparer;
        return this;
    }

    /// <summary>
    /// How this facet's values are written to and read from JSON selections (design §2.5), as
    /// strings. The default handles primitives, strings, enums, <see cref="Guid"/> and the date and
    /// time types; supply this for anything else, or for a different text form.
    /// </summary>
    public ValueFacetBuilder<T, TProp> Serialize(Func<TProp, string> format, Func<string, TProp> parse)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(parse);
        ensureMutable();
        definition.Formatter = new ValueFormatter<TProp>(format, parse);
        return this;
    }
}
