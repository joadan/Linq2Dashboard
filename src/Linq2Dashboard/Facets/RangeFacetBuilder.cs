using Linq2Dashboard.Facets;
using Linq2Dashboard.Indexing;

namespace Linq2Dashboard;

/// <summary>Fluent configuration of a range facet (design §2.1).</summary>
public sealed class RangeFacetBuilder<T>
{
    private readonly RangeFacetDefinition<T> definition;
    private readonly Action ensureMutable;

    internal RangeFacetBuilder(RangeFacetDefinition<T> definition, Action ensureMutable)
    {
        this.definition = definition;
        this.ensureMutable = ensureMutable;
    }

    /// <summary>The facet key, used in selections, state and the Blazor components.</summary>
    public string Key => definition.Key;

    /// <summary>Display name. Defaults to the key.</summary>
    public RangeFacetBuilder<T> Name(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ensureMutable();
        definition.Name = name;
        return this;
    }

    /// <summary>
    /// Explicit cut points. <c>Buckets(100, 500, 1000)</c> gives "below 100", "100 to 500",
    /// "500 to 1000" and "1000 and above". Must be strictly ascending.
    /// </summary>
    public RangeFacetBuilder<T> Buckets(params double[] cuts)
    {
        ensureMutable();
        definition.Bucketing = RangeBucketing.Explicit(cuts);
        return this;
    }

    /// <summary>
    /// About <paramref name="count"/> buckets of equal width on round boundaries (0, 100, 200) over the body
    /// of the distribution, with an open bucket at each end for the values outside it. Default is 10 (concept §5).
    /// </summary>
    public RangeFacetBuilder<T> AutoBuckets(int count)
    {
        ensureMutable();
        definition.Bucketing = RangeBucketing.Auto(count);
        return this;
    }
}
