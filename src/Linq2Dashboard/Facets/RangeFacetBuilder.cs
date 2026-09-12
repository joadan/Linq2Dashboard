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

    public string Key => definition.Key;

    /// <summary>Display name. Defaults to the key.</summary>
    public RangeFacetBuilder<T> Title(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ensureMutable();
        definition.Title = title;
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

    /// <summary><paramref name="count"/> equal-width buckets between the dataset's min and max. Default is 10.</summary>
    public RangeFacetBuilder<T> AutoBuckets(int count)
    {
        ensureMutable();
        definition.Bucketing = RangeBucketing.Auto(count);
        return this;
    }
}
