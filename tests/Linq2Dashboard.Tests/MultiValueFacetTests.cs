using System.Text.Json.Nodes;
using Linq2Dashboard.Facets;
using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Tests;

/// <summary>Multi-valued facets over collection properties (concept §5, §6).</summary>
public class MultiValueFacetTests
{
    // Row: Country Status   Tags
    //  0:  SE      Open     urgent, gift
    //  1:  NO      Open     gift
    //  2:  null    Closed   null
    //  3:  SE      Closed   Urgent, urgent, b2b      (one value twice, in two spellings)
    //  4:  DK      Pending  (empty)
    //  5:  se      Open     b2b
    //  6:  null    Pending  (only a null item)
    //  7:  NO      Open     gift, b2b
    private static Dashboard<Order> Build(Action<DashboardBuilder<Order>>? extra = null) =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.MultiValueFacet(x => x.Tags);
            b.CountMetric("orders");
            extra?.Invoke(b);
        });

    private static readonly Dashboard<Order> Shared = Build();

    private static ValueFacetState Tags(DashboardState<Order> state) => (ValueFacetState)state.Facet("Tags");

    [Fact]
    public void Each_row_is_counted_under_every_value_it_has_and_an_absent_collection_is_the_null_value()
    {
        var tags = Tags(Shared.Calculate());

        Assert.Equal(FacetKind.MultiValue, tags.Kind);
        Assert.True(tags.IsMultiValued);
        Assert.Equal(8, tags.ContextCount);
        Assert.Equal(
            [("gift", 3, 3), ("b2b", 3, 3), (null, 3, 3), ("urgent", 2, 2)],
            tags.Values.Select(v => (v.Value, v.TotalCount, v.FilteredCount)));
        Assert.Equal(4, tags.DistinctCount);
        Assert.True(tags.Values.Sum(v => v.FilteredCount) >= tags.ContextCount); // 11 over 8 rows
    }

    [Fact]
    public void A_value_is_counted_once_per_row_however_often_the_row_repeats_it()
    {
        // Row 3 lists "Urgent" and "urgent": one value under the default comparer, one count, first spelling kept.
        var tags = Tags(Shared.Calculate());

        FacetValue urgent = tags.Values.Single(v => v.Value is "urgent");
        Assert.Equal(2, urgent.TotalCount);
        Assert.Equal("urgent", urgent.Value); // row 0 introduced it
    }

    [Fact]
    public void Null_empty_and_all_null_collections_are_the_null_value()
    {
        var dashboard = Build();
        var index = Assert.IsType<ValueFacetIndex<string?>>(dashboard.FacetIndex("Tags"));
        var column = Assert.IsType<MultiValueColumn<string?>>(index.Column);

        Assert.Equal(3, column.NullCount);
        Assert.True(column.CodesOf(2).IsEmpty);
        Assert.True(column.CodesOf(4).IsEmpty);
        Assert.True(column.CodesOf(6).IsEmpty);
        Assert.Equal(8, column.OccurrenceCount);
        Assert.Equal([1, 2], column.CodesOf(0).ToArray()); // urgent, gift in row order
    }

    [Fact]
    public void Kind_and_info_say_multi_value()
    {
        Assert.Equal(new FacetInfo("Tags", "Tags", FacetKind.MultiValue), Shared.Facets.Single(f => f.Key == "Tags"));
    }

    [Fact]
    public void A_selection_matches_rows_having_any_of_the_selected_values()
    {
        var state = Shared.Calculate(Selections.Empty.Toggle("Tags", "urgent").Toggle("Tags", "gift"));

        Assert.Equal([1, 2, 4, 8], state.Items.Select(o => o.Id));
        Assert.Equal(4, state.Metric("orders").Value);
    }

    [Fact]
    public void The_null_value_selects_the_rows_without_values_and_combines_with_other_values()
    {
        Assert.Equal([3, 5, 7], Shared.Calculate(Selections.Empty.Toggle("Tags", null)).Items.Select(o => o.Id));
        Assert.Equal([3, 4, 5, 6, 7, 8], Shared.Calculate(Selections.Empty.Toggle("Tags", null).Toggle("Tags", "b2b")).Items.Select(o => o.Id));
    }

    [Fact]
    public void Its_own_selection_is_excluded_when_counting_the_facet()
    {
        var tags = Tags(Shared.Calculate(Selections.Empty.Toggle("Tags", "gift")));

        Assert.Equal(8, tags.ContextCount);
        Assert.Equal([3, 3, 3, 2], tags.Values.Select(v => v.FilteredCount));
        Assert.Equal(["gift"], tags.Values.Where(v => v.Selected).Select(v => v.Value));
    }

    [Fact]
    public void Other_facets_selections_narrow_the_counts_and_the_facet_narrows_the_others()
    {
        var state = Shared.Calculate(Selections.Empty.Toggle("Country", "SE").Toggle("Tags", "b2b"));

        var tags = Tags(state);
        Assert.Equal(3, tags.ContextCount); // rows 0, 3, 5
        Assert.Equal(
            [("b2b", 3, 2), ("urgent", 2, 2), ("gift", 3, 1), (null, 3, 0)],
            tags.Values.Select(v => (v.Value, v.TotalCount, v.FilteredCount)));

        var country = (ValueFacetState)state.Facet("Country");
        Assert.Equal(3, country.ContextCount); // rows 3, 5, 7 have b2b
        Assert.Equal([("SE", 3, 2), ("NO", 2, 1), (null, 2, 0), ("DK", 1, 0)],
            country.Values.Select(v => (v.Value, v.TotalCount, v.FilteredCount)));

        Assert.Equal([4, 6], state.Items.Select(o => o.Id));
    }

    [Fact]
    public void Top_n_limits_the_list_pins_selected_values_and_has_no_other()
    {
        var dashboard = Build(b => b.MultiValueFacet("top", x => x.Tags).Top(2));

        var top = (ValueFacetState)dashboard.Calculate().Facet("top");
        Assert.Equal(["gift", "b2b"], top.Values.Select(v => v.Value));
        Assert.Null(top.Other);
        Assert.Equal(4, top.DistinctCount);

        var pinned = (ValueFacetState)dashboard.Calculate(Selections.Empty.Toggle("top", "urgent")).Facet("top");
        Assert.Equal(["gift", "urgent"], pinned.Values.Select(v => v.Value)); // the pinned value takes one of the two slots
        Assert.Null(pinned.Other);
    }

    [Fact]
    public void Search_finds_values_by_text_with_counts_under_the_context()
    {
        var dashboard = Build(b => b.MultiValueFacet("searchable", x => x.Tags).Searchable());

        var facet = (ValueFacetState)dashboard.Calculate(Selections.Empty.Toggle("Country", "NO")).Facet("searchable");
        Assert.True(facet.IsSearchable);
        Assert.Equal([("gift", 3, 2)], facet.Search("gi").Select(v => (v.Value, v.TotalCount, v.FilteredCount)));
    }

    [Fact]
    public void A_label_is_read_from_the_value_once_per_distinct_value()
    {
        int calls = 0;
        var dashboard = Build(b => b.MultiValueFacet("labelled", x => x.Tags).Label(tag =>
        {
            calls++;
            return tag?.ToUpperInvariant();
        }));

        var facet = (ValueFacetState)dashboard.Calculate().Facet("labelled");
        Assert.Equal(3, calls);
        Assert.Equal(["GIFT", "B2B", null, "URGENT"], facet.Values.Select(v => v.Label));
        Assert.Equal("URGENT", facet.LabelOf("Urgent"));
        Assert.Equal([("urgent", "URGENT")], facet.Search("URG").Select(v => (v.Value, v.Label)));
    }

    [Fact]
    public void A_comparer_decides_equality_and_repeats_within_a_row()
    {
        var dashboard = Build(b => b.MultiValueFacet("exact", x => x.Tags).Comparer(StringComparer.Ordinal));

        var facet = (ValueFacetState)dashboard.Calculate().Facet("exact");
        Assert.Equal(
            [("gift", 3), ("b2b", 3), (null, 3), ("urgent", 2), ("Urgent", 1)],
            facet.Values.Select(v => (v.Value, v.TotalCount)));
    }

    [Fact]
    public void Selections_round_trip_through_json_and_the_query_string()
    {
        var selections = Selections.Empty.Toggle("Tags", "gift").Toggle("Tags", null);

        string json = Shared.Serializer.ToJson(selections);
        Assert.Equal("""{"Tags":{"values":["gift",null]}}""", JsonNode.Parse(json)!.ToJsonString());
        Assert.Equal(selections, Shared.Serializer.FromJson(json));

        string query = Shared.Serializer.ToQueryString(selections);
        Assert.Equal(selections, Shared.Serializer.FromQueryString(query));
    }

    [Fact]
    public void A_scope_lists_only_the_values_its_rows_have()
    {
        Dashboard<Order> nordic = Shared.ScopeTo(x => x.Country == "NO");

        var tags = Tags(nordic.Calculate());
        Assert.Equal([("gift", 2, 2), ("b2b", 1, 1)], tags.Values.Select(v => (v.Value, v.TotalCount, v.FilteredCount)));
        Assert.Equal(2, tags.DistinctCount);

        Dashboard<Order> byTag = Shared.ScopeTo(Selections.Empty.Toggle("Tags", "urgent"));
        Assert.Equal(2, byTag.TotalCount);
        Assert.Equal([("SE", 2, 2)], ((ValueFacetState)byTag.Calculate().Facet("Country")).Values.Select(v => (v.Value, v.TotalCount, v.FilteredCount)));
    }

    [Fact]
    public void The_key_is_derived_from_the_member_and_the_builder_validates_eagerly()
    {
        Assert.Equal("Tags", Shared.Facets.Single(f => f.Kind == FacetKind.MultiValue).Key);
        Assert.Throws<ArgumentException>(() => Build(b => b.MultiValueFacet(x => x.Tags!.Take(1))));
        Assert.Throws<ArgumentException>(() => Build(b => b.MultiValueFacet("Tags", x => x.Tags)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(b => b.MultiValueFacet("t", x => x.Tags).Top(0)));
    }

    private sealed record Item(int[]? Codes);

    [Fact]
    public void Items_of_other_types_work_with_the_default_serialisation()
    {
        Item[] rows = [new([1, 2]), new([2]), new(null)];
        var dashboard = Dashboard.Create(rows, b => b.MultiValueFacet(x => x.Codes));

        var facet = (ValueFacetState)dashboard.Calculate().Facet("Codes");
        Assert.Equal([(2, 2), (1, 1), (null, 1)], facet.Values.Select(v => (v.Value, v.TotalCount)));

        var selections = Selections.Empty.Toggle("Codes", 1);
        Assert.Equal(1, dashboard.Calculate(selections).MatchingCount);
        Assert.Equal(selections, dashboard.Serializer.FromQueryString(dashboard.Serializer.ToQueryString(selections)));
    }
}
