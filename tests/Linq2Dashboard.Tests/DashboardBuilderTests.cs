using Linq2Dashboard.Facets;

namespace Linq2Dashboard.Tests;

public class DashboardBuilderTests
{
    private static Dashboard<Order> Create(Action<DashboardBuilder<Order>> configure) =>
        Dashboard.Create(TestData.Orders(), configure);

    [Fact]
    public void Keys_are_derived_from_member_selectors()
    {
        var dashboard = Create(b =>
        {
            b.Where(x => x.Address is not null); // the nested selector below is the application's responsibility
            b.ValueFacet(x => x.Country);
            b.ValueFacet(x => x.Address!.City);
            b.RangeFacet(x => x.Amount);
            b.DateFacet(x => x.OrderDate);
            b.BooleanFacet(x => x.IsActive);
        });

        Assert.Equal(["Country", "City", "Amount", "OrderDate", "IsActive"], dashboard.Facets.Select(f => f.Key));
    }

    [Fact]
    public void Explicit_keys_and_titles_are_used_when_given()
    {
        var dashboard = Create(b =>
        {
            b.ValueFacet("status", x => x.Status).Title("Order status");
            b.ValueFacet(x => x.Country);
        });

        Assert.Equal(new FacetInfo("status", "Order status", FacetKind.Value), dashboard.Facets[0]);
        Assert.Equal(new FacetInfo("Country", "Country", FacetKind.Value), dashboard.Facets[1]);
    }

    [Fact]
    public void Non_member_selectors_need_an_explicit_key()
    {
        var error = Assert.Throws<ArgumentException>(() => Create(b => b.ValueFacet(x => x.Country + "!")));
        Assert.Contains("explicit key", error.Message);

        var dashboard = Create(b => b.ValueFacet("shout", x => x.Country + "!"));
        Assert.Equal("shout", dashboard.Facets[0].Key);
    }

    [Fact]
    public void Duplicate_keys_are_rejected_and_are_case_sensitive()
    {
        Assert.Throws<ArgumentException>(() => Create(b =>
        {
            b.ValueFacet(x => x.Country);
            b.ValueFacet("Country", x => x.Status);
        }));

        Assert.Throws<ArgumentException>(() => Create(b =>
        {
            b.Count("orders");
            b.Sum("orders", x => x.Amount);
        }));

        var dashboard = Create(b =>
        {
            b.ValueFacet(x => x.Country);
            b.ValueFacet("country", x => x.Status);
            b.Count("Country"); // metric keys are a separate namespace
        });
        Assert.Equal(2, dashboard.Facets.Count);
        Assert.Single(dashboard.Metrics);
    }

    [Fact]
    public void Blank_keys_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Create(b => b.ValueFacet(" ", x => x.Country)));
        Assert.Throws<ArgumentException>(() => Create(b => b.Count("")));
    }

    [Fact]
    public void Fixed_filters_define_the_dataset()
    {
        var all = Create(_ => { });
        var open = Create(b => b.Where(x => x.Status == "Open"));
        var openActive = Create(b => b.Where(x => x.Status == "Open").Where(x => x.Verified == true));

        Assert.Equal(8, all.TotalCount);
        Assert.Equal(4, open.TotalCount);
        Assert.Equal(2, openActive.TotalCount);
        Assert.Equal([1, 8], openActive.Items.ToArray().Select(o => o.Id));
    }

    [Fact]
    public void Source_is_enumerated_exactly_once()
    {
        var source = new CountingEnumerable<Order>(TestData.Orders());

        Dashboard.Create(source, b =>
        {
            b.Where(x => x.Amount > 0);
            b.ValueFacet(x => x.Country);
            b.RangeFacet(x => x.Amount);
            b.DateFacet(x => x.OrderDate);
            b.Sum("revenue", x => x.Amount);
            b.OrderBy(x => x.Id);
        });

        Assert.Equal(1, source.Enumerations);
    }

    [Fact]
    public void Source_without_filters_is_enumerated_exactly_once_too()
    {
        var source = new CountingEnumerable<Order>(TestData.Orders());

        Dashboard.Create(source, b => b.ValueFacet(x => x.Country));

        Assert.Equal(1, source.Enumerations);
    }

    [Fact]
    public void Empty_source_builds()
    {
        var dashboard = Dashboard.Create(Array.Empty<Order>(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.RangeFacet(x => x.Amount);
            b.DateFacet(x => x.OrderDate);
            b.Count("orders");
            b.OrderBy(x => x.Id);
        });

        Assert.Equal(0, dashboard.TotalCount);
        Assert.Equal(3, dashboard.Facets.Count);
        Assert.Empty(dashboard.SortedRows!);
    }

    [Fact]
    public void Boolean_facets_have_the_boolean_kind_for_bool_and_nullable_bool()
    {
        var dashboard = Create(b =>
        {
            b.BooleanFacet(x => x.IsActive);
            b.BooleanFacet(x => x.Verified);
        });

        Assert.All(dashboard.Facets, f => Assert.Equal(FacetKind.Boolean, f.Kind));
        var verified = Assert.IsType<ValueFacetIndex<bool?>>(dashboard.FacetIndex("Verified"));
        Assert.Equal(2, verified.Column.NullCount);
        Assert.Equal(2, verified.Column.DistinctCount);
    }

    [Fact]
    public void Value_facet_over_nullable_struct_keeps_nulls_out_of_the_dictionary()
    {
        var dashboard = Create(b => b.ValueFacet(x => x.Quantity));

        var index = Assert.IsType<ValueFacetIndex<int?>>(dashboard.FacetIndex("Quantity"));
        Assert.Equal(2, index.Column.NullCount);
        Assert.Equal(new int?[] { 2, 1, 5, 3, 0 }, Enumerable.Range(1, index.Column.DistinctCount).Select(index.Column.ValueOf));
    }

    [Fact]
    public void Value_facet_options_reach_the_index()
    {
        var dashboard = Create(b => b.ValueFacet(x => x.Country).Top(3).RankBy(RankMode.TotalCount).Searchable().Comparer(StringComparer.Ordinal));

        var index = Assert.IsType<ValueFacetIndex<string?>>(dashboard.FacetIndex("Country"));
        Assert.Equal(3, index.Top);
        Assert.Equal(RankMode.TotalCount, index.RankMode);
        Assert.True(index.Searchable);
        Assert.Equal(4, index.Column.DistinctCount); // "SE" and "se" stay apart under Ordinal
    }

    [Fact]
    public void Range_facets_accept_any_numeric_property_nullable_or_not()
    {
        var dashboard = Create(b =>
        {
            b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);
            b.RangeFacet(x => x.Discount);
            b.RangeFacet(x => x.Quantity).AutoBuckets(3);
            b.RangeFacet(x => x.Id);
        });

        var amount = Assert.IsType<RangeFacetIndex>(dashboard.FacetIndex("Amount"));
        Assert.Equal(4, amount.Column.BucketCount);
        Assert.Equal(999.5, amount.Column.Values[3]);
        Assert.False(amount.Column.HasNulls);

        var discount = Assert.IsType<RangeFacetIndex>(dashboard.FacetIndex("Discount"));
        Assert.Equal(3, discount.Column.NullCount);
        Assert.Equal(10, discount.Column.BucketCount);

        var quantity = Assert.IsType<RangeFacetIndex>(dashboard.FacetIndex("Quantity"));
        Assert.Equal(3, quantity.Column.BucketCount);
        Assert.Equal(0, quantity.Column.Min);
        Assert.Equal(5, quantity.Column.Max);
    }

    [Fact]
    public void Range_facet_over_a_non_numeric_property_is_rejected()
    {
        var error = Assert.Throws<ArgumentException>(() => Create(b => b.RangeFacet(x => x.Status)));
        Assert.Contains("numeric", error.Message);
        Assert.Throws<ArgumentException>(() => Create(b => b.RangeFacet(x => x.OrderDate)));
        Assert.Throws<ArgumentException>(() => Create(b => b.Sum("s", x => x.Country)));
    }

    [Fact]
    public void Date_facets_convert_every_supported_type_into_instants()
    {
        var dashboard = Create(b =>
        {
            b.DateFacet(x => x.OrderDate).TimeZone(TestData.Stockholm);     // DateTime, kind Unspecified → in zone
            b.DateFacet("utc", x => DateTime.SpecifyKind(x.OrderDate, DateTimeKind.Utc)).TimeZone(TestData.Stockholm);
            b.DateFacet(x => x.Shipped);                                     // DateTimeOffset?
            b.DateFacet(x => x.Due).TimeZone(TestData.Stockholm);           // DateOnly → midnight in zone
        });

        var orderDate = Assert.IsType<DateFacetIndex>(dashboard.FacetIndex("OrderDate"));
        Assert.Equal(TestData.Instant("2026-01-15T10:00:00+01:00").UtcTicks, orderDate.Column.UtcTicks[0]);
        Assert.Equal(TestData.Instant("2026-03-31T22:30:00+02:00").UtcTicks, orderDate.Column.UtcTicks[5]);

        var utc = Assert.IsType<DateFacetIndex>(dashboard.FacetIndex("utc"));
        Assert.Equal(TestData.Instant("2026-01-15T10:00:00Z").UtcTicks, utc.Column.UtcTicks[0]);

        var shipped = Assert.IsType<DateFacetIndex>(dashboard.FacetIndex("Shipped"));
        Assert.Equal(3, shipped.Column.NullCount);
        Assert.Equal(TestData.Instant("2026-01-16T08:00:00Z").UtcTicks, shipped.Column.UtcTicks[0]);

        var due = Assert.IsType<DateFacetIndex>(dashboard.FacetIndex("Due"));
        Assert.Equal(TestData.Instant("2026-01-20T00:00:00+01:00").UtcTicks, due.Column.UtcTicks[0]);
    }

    [Fact]
    public void Date_facet_options_reach_the_index()
    {
        var clock = new FixedTimeProvider(TestData.Instant("2026-03-15T10:00:00Z"));
        var dashboard = Create(b =>
        {
            b.DateFacet(x => x.OrderDate)
             .Title("Ordered")
             .TimeZone(TestData.Stockholm)
             .Granularity(DateGranularity.Week)
             .Presets(DatePreset.Today, DatePreset.Last7Days, DatePreset.Today);
            b.UseTimeProvider(clock);
        });

        var index = Assert.IsType<DateFacetIndex>(dashboard.FacetIndex("OrderDate"));
        Assert.Equal("Ordered", index.Title);
        Assert.Same(TestData.Stockholm, index.Column.Zone);
        Assert.Equal(DateGranularity.Week, index.Column.Granularity);
        Assert.Equal([DatePreset.Today, DatePreset.Last7Days], index.Presets);
        Assert.Same(clock, index.TimeProvider);
        Assert.Same(clock, dashboard.TimeProvider);
    }

    [Fact]
    public void Date_facet_over_an_unsupported_type_is_rejected()
    {
        var error = Assert.Throws<ArgumentException>(() => Create(b => b.DateFacet(x => x.Status)));
        Assert.Contains("DateOnly", error.Message);
    }

    [Fact]
    public void Metrics_are_defined_with_their_aggregation()
    {
        var dashboard = Create(b =>
        {
            b.Count("orders").Title("Orders");
            b.Sum("revenue", x => x.Amount);
            b.Average("avgDiscount", x => x.Discount);
            b.Min("minQty", x => x.Quantity);
            b.Max("maxId", x => x.Id);
            b.Distinct("countries", x => x.Country);
        });

        Assert.Equal(
            [
                new MetricInfo("orders", "Orders", Aggregation.Count),
                new MetricInfo("revenue", "revenue", Aggregation.Sum),
                new MetricInfo("avgDiscount", "avgDiscount", Aggregation.Average),
                new MetricInfo("minQty", "minQty", Aggregation.Min),
                new MetricInfo("maxId", "maxId", Aggregation.Max),
                new MetricInfo("countries", "countries", Aggregation.Distinct),
            ],
            dashboard.Metrics);

        Assert.Null(dashboard.MetricIndex("orders").Column);
        Assert.Equal(5424.5, dashboard.MetricIndex("revenue").Column!.Total.Sum);
        Assert.Equal(3, dashboard.MetricIndex("avgDiscount").Column!.NullCount);
        Assert.Null(dashboard.MetricIndex("countries").Column);
        Assert.Equal(3, dashboard.MetricIndex("countries").Distinct!.DistinctCount);
    }

    [Fact]
    public void Distinct_validates_like_every_other_metric()
    {
        Assert.Throws<ArgumentNullException>(() => Create(b => b.Distinct<string?>("countries", null!)));
        Assert.Throws<ArgumentException>(() => Create(b =>
        {
            b.Count("countries");
            b.Distinct("countries", x => x.Country);
        }));
    }

    [Fact]
    public void Sort_order_is_materialised_once_with_row_id_as_final_tiebreaker()
    {
        var none = Create(_ => { });
        var byAmount = Create(b => b.OrderBy(x => x.Amount));
        var byStatusThenIdDesc = Create(b => b.OrderBy(x => x.Status).ThenByDescending(x => x.Id));
        var byCountry = Create(b => b.OrderByDescending(x => x.Country, StringComparer.Ordinal));

        Assert.Null(none.SortedRows);
        Assert.Equal([6, 7, 0, 1, 2, 3, 4, 5], byAmount.SortedRows!);
        Assert.Equal([3, 2, 7, 5, 1, 0, 6, 4], byStatusThenIdDesc.SortedRows!);
        // Descending by Country, ordinal: "se" > "SE" > "NO" > "DK" > null; ties (rows 0 and 3, rows 1 and 7) keep row order.
        Assert.Equal([5, 0, 3, 1, 7, 4, 2, 6], byCountry.SortedRows!);
    }

    [Fact]
    public void Sort_keys_must_start_with_OrderBy_and_use_it_once()
    {
        Assert.Throws<InvalidOperationException>(() => Create(b => b.ThenBy(x => x.Id)));
        Assert.Throws<InvalidOperationException>(() => Create(b => b.OrderBy(x => x.Id).OrderBy(x => x.Amount)));
    }

    [Fact]
    public void Configuration_is_frozen_after_build()
    {
        DashboardBuilder<Order>? captured = null;
        ValueFacetBuilder<Order, string?>? facet = null;
        MetricBuilder<Order>? metric = null;

        Create(b =>
        {
            captured = b;
            facet = b.ValueFacet(x => x.Country);
            metric = b.Count("orders");
        });

        Assert.Throws<InvalidOperationException>(() => captured!.ValueFacet(x => x.Status));
        Assert.Throws<InvalidOperationException>(() => captured!.Where(_ => true));
        Assert.Throws<InvalidOperationException>(() => facet!.Top(5));
        Assert.Throws<InvalidOperationException>(() => metric!.Title("x"));
    }

    [Fact]
    public void Unknown_keys_are_reported()
    {
        var dashboard = Create(b => b.ValueFacet(x => x.Country));

        Assert.Throws<ArgumentException>(() => dashboard.FacetIndex("Nope"));
        Assert.Throws<ArgumentException>(() => dashboard.MetricIndex("Nope"));
        Assert.False(dashboard.TryGetFacetIndex("Nope", out _));
        Assert.True(dashboard.TryGetFacetIndex("Country", out _));
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => Dashboard.Create<Order>(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => Dashboard.Create(TestData.Orders(), null!));
        Assert.Throws<ArgumentNullException>(() => Create(b => b.Where(null!)));
        Assert.Throws<ArgumentNullException>(() => Create(b => b.ValueFacet<string>("k", null!)));
        Assert.Throws<ArgumentNullException>(() => Create(b => b.UseTimeProvider(null!)));
    }
}
