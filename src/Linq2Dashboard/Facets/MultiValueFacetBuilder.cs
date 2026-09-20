using Linq2Dashboard.Facets;
using Linq2Dashboard.Serialization;

namespace Linq2Dashboard;

/// <summary>
/// Fluent configuration of a multi-valued facet (concept §5, design §2.1). The same options as a
/// value facet, except that <see cref="Label"/> reads the value rather than the row, since a row
/// has several values. A multi-valued facet never has "Other" (concept §6).
/// </summary>
public sealed class MultiValueFacetBuilder<T, TItem>
{
    private readonly MultiValueFacetDefinition<T, TItem> definition;
    private readonly Action ensureMutable;

    internal MultiValueFacetBuilder(MultiValueFacetDefinition<T, TItem> definition, Action ensureMutable)
    {
        this.definition = definition;
        this.ensureMutable = ensureMutable;
    }

    /// <summary>The facet key, used in selections, state and the Blazor components.</summary>
    public string Key => definition.Key;

    /// <summary>Display name. Defaults to the key.</summary>
    public MultiValueFacetBuilder<T, TItem> Name(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ensureMutable();
        definition.Name = name;
        return this;
    }

    /// <summary>Present only the top <paramref name="count"/> values (concept §6). Selected values are always presented; there is no "Other" row.</summary>
    public MultiValueFacetBuilder<T, TItem> Top(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ensureMutable();
        definition.Top = count;
        return this;
    }

    /// <summary>How the top N are ranked (concept §6). Default is by filtered count.</summary>
    public MultiValueFacetBuilder<T, TItem> RankBy(RankMode mode)
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
    public MultiValueFacetBuilder<T, TItem> Searchable(bool searchable = true)
    {
        ensureMutable();
        definition.Searchable = searchable;
        return this;
    }

    /// <summary>
    /// A label per value, read from the value (concept §5). The value stays the identity: counting,
    /// selections and JSON use it, while the label is what the UI shows and what
    /// <see cref="ValueFacetState.Search"/> matches. Evaluated once per distinct value at build.
    /// A null label means the UI formats the value as usual.
    /// </summary>
    public MultiValueFacetBuilder<T, TItem> Label(Func<TItem, string?> label)
    {
        ArgumentNullException.ThrowIfNull(label);
        ensureMutable();
        definition.Label = label;
        return this;
    }

    /// <summary>
    /// Value equality for this facet, also used to drop a row's repeats of a value. Defaults to
    /// ordinal ignore-case for strings and default equality otherwise (design §3.3).
    /// </summary>
    public MultiValueFacetBuilder<T, TItem> Comparer(IEqualityComparer<TItem> comparer)
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
    public MultiValueFacetBuilder<T, TItem> Serialize(Func<TItem, string> format, Func<string, TItem> parse)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(parse);
        ensureMutable();
        definition.Formatter = new ValueFormatter<TItem>(format, parse);
        return this;
    }
}
