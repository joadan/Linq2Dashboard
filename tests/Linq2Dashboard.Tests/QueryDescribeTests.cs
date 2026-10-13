namespace Linq2Dashboard.Tests;

public class QueryDescribeTests
{
    // Rows as in QueryTests: Country SE, NO, null, SE, DK, se, null, NO; Amount 0 to 2500.
    private static readonly Dashboard<Order> Shared = Dashboard.Create(TestData.Orders(), b =>
    {
        b.ValueFacet(x => x.Country);
        b.ValueFacet("customer", x => x.Id).Label(x => $"Order {x.Id}");
        b.MultiValueFacet(x => x.Tags);
        b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);
        b.DateFacet(x => x.OrderDate).Granularity(DateGranularity.Month).TimeZone(TestData.Stockholm).Presets(DatePreset.Last7Days);
        b.TextFacet("search", (x, text) => x.Status.Contains(text, StringComparison.OrdinalIgnoreCase));
        b.CountMetric("orders");
        b.SumMetric("revenue", x => x.Amount);
    });

    [Fact]
    public void Describe_gives_each_facet_what_helps_a_question()
    {
        var description = Shared.Describe();

        var country = description.Facets.Single(f => f.Key == "Country");
        Assert.Equal(4, country.ValueCount);
        Assert.False(country.HasLabels);
        Assert.Equal(["SE", "NO", null, "DK"], country.Values!.Select(v => v.Value));

        var customer = description.Facets.Single(f => f.Key == "customer");
        Assert.True(customer.HasLabels);
        Assert.Equal(new QueryValueCell(1, "Order 1"), customer.Values![0]);

        var amount = description.Facets.Single(f => f.Key == "Amount");
        Assert.Equal((4, 0.0, 2500.0), (amount.BucketCount, amount.Min, amount.Max));

        var date = description.Facets.Single(f => f.Key == "OrderDate");
        Assert.Equal((DateGranularity.Month, "Europe/Stockholm"), (date.Granularity, date.TimeZone));
        Assert.Equal([DatePreset.Last7Days], date.Presets);

        Assert.Equal(new FacetDescription("search", "search", FacetKind.Text), description.Facets.Single(f => f.Key == "search"));
    }

    [Fact]
    public void Describe_lists_values_only_up_to_the_limit()
    {
        var description = Shared.Describe(new DescribeOptions { ValueLimit = 5 });

        Assert.NotNull(description.Facets.Single(f => f.Key == "Country").Values);
        var customer = description.Facets.Single(f => f.Key == "customer");
        Assert.Null(customer.Values);
        Assert.Equal(8, customer.ValueCount);
    }

    [Fact]
    public void Describe_counts_the_values_of_a_scope()
    {
        var active = Shared.ScopeTo(x => x.IsActive).Describe();

        var country = active.Facets.Single(f => f.Key == "Country");
        Assert.Equal(2, country.ValueCount);
        Assert.Equal(["SE", "NO"], country.Values!.Select(v => v.Value));
    }

    [Fact]
    public void Describe_says_what_each_property_allows()
    {
        var properties = Shared.Describe().Properties.ToDictionary(p => p.Name);

        Assert.Equal([PropertyUse.GroupBy, PropertyUse.Aggregate, PropertyUse.Distinct, PropertyUse.Column], properties["Discount"].Uses);
        Assert.Equal(PropertyKind.Number, properties["Discount"].Kind);
        Assert.Equal([PropertyUse.GroupBy, PropertyUse.Period, PropertyUse.Distinct, PropertyUse.Column], properties["Shipped"].Uses);
        Assert.Equal(PropertyKind.Date, properties["Due"].Kind);
        Assert.Equal(PropertyKind.Text, properties["Kind"].Kind);
        Assert.Equal(["Web", "Store"], properties["Kind"].Values!);
        Assert.Equal(PropertyKind.Boolean, properties["IsActive"].Kind);
        Assert.Equal(PropertyKind.Collection, properties["Tags"].Kind);
        Assert.Equal([PropertyUse.GroupBy, PropertyUse.Distinct, PropertyUse.Column], properties["Tags"].Uses);
        Assert.Equal(PropertyKind.Other, properties["Address"].Kind);
        Assert.Equal([PropertyUse.GroupBy, PropertyUse.Distinct], properties["Address"].Uses);
        Assert.True(properties["Country"].BehindFacet);
        Assert.False(properties["Status"].BehindFacet);
        Assert.Equal(properties.Keys.Order(StringComparer.Ordinal), properties.Keys);
    }

    [Fact]
    public void Describe_carries_the_applications_descriptions_and_shows_only_what_is_allowed()
    {
        var description = Shared.Describe(new DescribeOptions
        {
            Allowed = ["Country", "revenue", "Discount"],
            Descriptions = new Dictionary<string, string>
            {
                ["Country"] = "ISO country code of the delivery address.",
                ["revenue"] = "Order value in SEK, excluding VAT.",
                ["Discount"] = "Discount in SEK.",
                ["customer"] = "Shown nowhere, since it is not allowed.",
            },
        });

        Assert.Equal(["Country"], description.Facets.Select(f => f.Key));
        Assert.Equal("ISO country code of the delivery address.", description.Facets[0].Description);
        Assert.Equal([("revenue", "Order value in SEK, excluding VAT.")], description.Metrics.Select(m => (m.Key, m.Description)));
        // The property behind the allowed facet's name is allowed too, under the same description.
        Assert.Equal([("Country", "ISO country code of the delivery address."), ("Discount", "Discount in SEK.")], description.Properties.Select(p => (p.Name, p.Description)));
    }

    [Fact]
    public void Describe_refuses_a_description_of_a_name_it_does_not_know()
    {
        var error = Assert.Throws<ArgumentException>(() => Shared.Describe(new DescribeOptions { Descriptions = new Dictionary<string, string> { ["Region"] = "?" } }));

        Assert.Contains("'Region'", error.Message);
    }

    [Fact]
    public void Description_json_leaves_out_what_has_no_value()
    {
        string json = Shared.Serializer.ToJson(Shared.Describe(new DescribeOptions
        {
            Descriptions = new Dictionary<string, string> { ["revenue"] = "Order value in SEK." },
        }));

        Assert.Contains("""{"key":"Country","name":"Country","kind":"value","valueCount":4,"values":["SE","NO",null,"DK"]}""", json);
        Assert.Contains("""{"key":"customer","name":"customer","kind":"value","valueCount":8,"labelled":true,"values":[{"value":1,"label":"Order 1"},""", json);
        Assert.Contains("""{"key":"Amount","name":"Amount","kind":"range","buckets":4,"min":0,"max":2500}""", json);
        Assert.Contains("""{"key":"OrderDate","name":"OrderDate","kind":"date","granularity":"month","timeZone":"Europe/Stockholm","presets":["last7Days"]}""", json);
        Assert.Contains("""{"key":"search","name":"search","kind":"text"}""", json);
        Assert.Contains("""{"key":"revenue","name":"revenue","aggregation":"sum","description":"Order value in SEK."}""", json);
        Assert.Contains("""{"name":"Country","kind":"text","uses":["groupBy","distinct","column"],"behindFacet":true}""", json);
        Assert.Contains("""{"name":"Kind","kind":"text","uses":["groupBy","distinct","column"],"values":["Web","Store"]}""", json);
        Assert.EndsWith("\"periods\":[\"year\",\"quarter\",\"month\",\"week\",\"day\"],\"limits\":{\"rows\":10000,\"defaultRows\":100,\"dimensions\":2}}", json);
    }
}
