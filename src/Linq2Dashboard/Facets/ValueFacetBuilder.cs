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

    /// <summary>The facet key, used in selections, state and the Blazor components.</summary>
    public string Key => definition.Key;

    /// <summary>Display name. Defaults to the key.</summary>
    public ValueFacetBuilder<T, TProp> Name(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ensureMutable();
        definition.Name = name;
        return this;
    }

    /// <summary>Present only the top <paramref name="count"/> values plus "Other" (concept §6).</summary>
    public ValueFacetBuilder<T, TProp> Top(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ensureMutable();
        if (definition.GroupBy is not null)
        {
            throw TopWithGroups();
        }

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
    /// A label per value, read from the row (concept §5). The value stays the identity: counting,
    /// selections and JSON use it, while the label is what the UI shows and what
    /// <see cref="ValueFacetState.Search"/> matches. Evaluated once per distinct value, on the first
    /// row that introduces it. A null label means the UI formats the value as usual.
    /// </summary>
    public ValueFacetBuilder<T, TProp> Label(Func<T, string?> label)
    {
        ArgumentNullException.ThrowIfNull(label);
        ensureMutable();
        definition.Label = label;
        return this;
    }

    /// <summary>
    /// A named group per value, read from the row (concept §6), such as <c>x =&gt; x.Market</c>, or
    /// <c>x =&gt; markets[x.Country]</c> for a lookup by the value. Evaluated once per distinct value, on
    /// the first row that introduces it; rows that disagree are not an error. A null group leaves the
    /// value ungrouped, and the null value is never grouped. Group names compare ignoring case. The
    /// state lists the groups with summed counts, and <see cref="Selections.ToggleGroup"/> selects a
    /// group's values. Cannot be combined with <see cref="Top"/>.
    /// </summary>
    public ValueFacetBuilder<T, TProp> GroupBy(Func<T, string?> group)
    {
        ArgumentNullException.ThrowIfNull(group);
        ensureMutable();
        if (definition.Top is not null)
        {
            throw TopWithGroups();
        }

        definition.GroupBy = group;
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

    private InvalidOperationException TopWithGroups() => new(
        $"Facet '{Key}' cannot have both GroupBy and Top: a group lists all its values, so a click on it selects what it shows (concept §6).");
}
