namespace Linq2Dashboard.Tests;

/// <summary>Concept §7: a facet declared from a member is keyed by the member name, and <see cref="FacetKey.Of{T}"/> derives that same key from the selector.</summary>
public class FacetKeyTests
{
    [Fact]
    public void The_key_of_a_reference_member_is_its_name()
    {
        Assert.Equal("Country", FacetKey.Of<Order>(x => x.Country));
    }

    [Fact]
    public void A_value_type_member_arrives_boxed_and_still_gives_its_name()
    {
        Assert.Equal("Amount", FacetKey.Of<Order>(x => x.Amount));
        Assert.Equal("IsActive", FacetKey.Of<Order>(x => x.IsActive));
        Assert.Equal("OrderDate", FacetKey.Of<Order>(x => x.OrderDate));
    }

    [Fact]
    public void A_nested_member_is_keyed_by_the_last_name_as_in_the_builder()
    {
        var dashboard = Dashboard.Create(TestData.Orders().Where(x => x.Address is not null), b => b.ValueFacet(x => x.Address!.City));

        Assert.Equal(dashboard.Facets[0].Key, FacetKey.Of<Order>(x => x.Address!.City));
    }

    [Fact]
    public void Anything_but_a_member_access_has_no_derived_key()
    {
        var error = Assert.Throws<ArgumentException>(() => FacetKey.Of<Order>(x => x.Country + "!"));

        Assert.Contains("explicit key", error.Message);
    }

    [Fact]
    public void An_unknown_facet_key_names_the_known_keys_and_a_case_only_match()
    {
        var state = Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.RangeFacet(x => x.Amount).Buckets(100);
            b.CountMetric("orders");
        }).Calculate(Selections.Empty);

        var facetError = Assert.Throws<ArgumentException>(() => state.Facet("country"));
        Assert.Contains("Unknown facet key 'country'.", facetError.Message);
        Assert.Contains("Did you mean 'Country'? Keys are case-sensitive.", facetError.Message);
        Assert.Contains("Known facet keys: Country, Amount.", facetError.Message);

        var noMatch = Assert.Throws<ArgumentException>(() => state.Facet("Region"));
        Assert.DoesNotContain("Did you mean", noMatch.Message);
        Assert.Contains("Known facet keys: Country, Amount.", noMatch.Message);

        var metricError = Assert.Throws<ArgumentException>(() => state.Metric("Orders"));
        Assert.Contains("Did you mean 'orders'?", metricError.Message);
        Assert.Contains("Known metric keys: orders.", metricError.Message);
    }
}
