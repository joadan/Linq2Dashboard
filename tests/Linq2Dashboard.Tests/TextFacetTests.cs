using System.Text.Json.Nodes;

namespace Linq2Dashboard.Tests;

/// <summary>Text facets: free text matched by an application function (concept §5).</summary>
public class TextFacetTests
{
    // Row: Country Status   Amount
    //  0:  SE      Open     100
    //  1:  NO      Open     250
    //  2:  null    Closed   500
    //  3:  SE      Closed   999.5
    //  4:  DK      Pending  1000
    //  5:  se      Open     2500
    //  6:  null    Pending  0
    //  7:  NO      Open     75
    private static Dashboard<Order> Build(Action<DashboardBuilder<Order>>? extra = null) =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);
            b.TextFacet("search", (order, text) =>
                order.Status.Contains(text, StringComparison.OrdinalIgnoreCase)
                || (order.Country?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false));
            b.CountMetric("orders");
            extra?.Invoke(b);
        });

    private static readonly Dashboard<Order> Shared = Build();

    private static Selections Text(string text) => Selections.Empty.With("search", new TextSelection(text));

    [Fact]
    public void The_text_is_a_selection_and_narrows_the_matching_rows()
    {
        var state = Shared.Calculate(Text("open"));

        Assert.Equal(4, state.MatchingCount);
        Assert.Equal([100m, 250m, 2500m, 75m], state.Items.Select(o => o.Amount));
        Assert.Equal(4, state.Metric("orders").Value);
    }

    [Fact]
    public void The_text_joins_the_and_across_facets()
    {
        var state = Shared.Calculate(Text("open").Toggle("Country", "SE"));

        Assert.Equal([1, 6], state.Items.Select(o => o.Id));
    }

    [Fact]
    public void Other_facets_count_under_the_text_and_the_text_is_matched_among_the_other_facets_rows()
    {
        var state = Shared.Calculate(Text("open").Toggle("Country", "SE"));

        var country = (ValueFacetState)state.Facet("Country");
        Assert.Equal(4, country.ContextCount);
        Assert.Equal(
            [("SE", 3, 2), ("NO", 2, 2), (null, 2, 0), ("DK", 1, 0)],
            country.Values.Select(v => (v.Value, v.TotalCount, v.FilteredCount)));

        var search = (TextFacetState)state.Facet("search");
        Assert.Equal(FacetKind.Text, search.Kind);
        Assert.Equal("open", search.Text);
        Assert.True(search.HasSelection);
        Assert.Equal(3, search.ContextCount); // the SE rows, this facet's own text excluded (concept §4.2)
    }

    [Fact]
    public void An_unconstrained_text_facet_has_no_text()
    {
        var search = (TextFacetState)Shared.Calculate().Facet("search");

        Assert.Null(search.Text);
        Assert.False(search.HasSelection);
        Assert.Equal(8, search.ContextCount);
    }

    [Fact]
    public void The_text_is_trimmed_and_whitespace_only_text_clears_the_facet()
    {
        Assert.Equal("open", new TextSelection("  open\t").Text);
        Assert.Equal(new TextSelection("open"), new TextSelection(" open "));
        Assert.True(new TextSelection("   ").IsEmpty);

        var cleared = Text("open").With("search", new TextSelection("  "));
        Assert.False(cleared.Contains("search"));
        Assert.Equal(Selections.Empty, cleared);
    }

    [Fact]
    public void The_predicate_receives_the_trimmed_text_once_per_row()
    {
        var seen = new List<string>();
        var dashboard = Build(b => b.TextFacet("t", (_, text) =>
        {
            lock (seen)
            {
                seen.Add(text);
            }

            return true;
        }));

        dashboard.Calculate(Selections.Empty.With("t", new TextSelection(" acme ")));

        Assert.Equal(8, seen.Count);
        Assert.All(seen, t => Assert.Equal("acme", t));
    }

    [Fact]
    public void Case_and_which_properties_take_part_belong_to_the_predicate()
    {
        var sensitive = Build(b => b.TextFacet("exact", (order, text) => order.Status.Contains(text, StringComparison.Ordinal)));

        Assert.Equal(4, sensitive.Calculate(Selections.Empty.With("exact", new TextSelection("Open"))).MatchingCount);
        Assert.Equal(0, sensitive.Calculate(Selections.Empty.With("exact", new TextSelection("open"))).MatchingCount);
    }

    [Fact]
    public void A_wrong_selection_kind_for_a_text_facet_throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => Shared.Calculate(Selections.Empty.With("search", ValueSelection.Of("open"))));

        Assert.Contains("TextSelection", ex.Message);
    }

    [Fact]
    public void Parallel_and_serial_scans_give_the_same_rows()
    {
        string[] rows = Enumerable.Range(0, 1000).Select(i => $"row {i}").ToArray();
        Func<string, string, bool> contains = (s, t) => s.Contains(t, StringComparison.Ordinal);
        var serial = Dashboard.Create(rows, b => b.TextFacet("t", contains));
        var parallel = Dashboard.Create(rows, b =>
        {
            b.TextFacet("t", contains);
            b.EnableParallelCounting();
        });

        foreach (string text in new[] { "7", "row 99", "row", "nothing" })
        {
            var selections = Selections.Empty.With("t", new TextSelection(text));
            Assert.Equal(serial.Calculate(selections).Items, parallel.Calculate(selections).Items);
        }

        Assert.Equal(271, serial.Calculate(Selections.Empty.With("t", new TextSelection("7"))).MatchingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_predicate_exception_reaches_the_caller_as_itself(bool parallel)
    {
        string[] rows = Enumerable.Range(0, 200).Select(i => i.ToString()).ToArray();
        var dashboard = Dashboard.Create(rows, b =>
        {
            b.TextFacet("t", (_, _) => throw new InvalidOperationException("boom"));
            b.EnableParallelCounting(parallel);
        });

        var ex = Assert.Throws<InvalidOperationException>(() => dashboard.Calculate(Selections.Empty.With("t", new TextSelection("x"))));

        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public void The_rows_for_a_text_are_cached_by_the_text()
    {
        var dashboard = Build();

        dashboard.Calculate(Text("open"));
        dashboard.Calculate(Text(" open ").Toggle("Country", "NO"));
        dashboard.Calculate(Text("open"));

        Assert.Equal(2, dashboard.CachedRowSetCount); // "open" and Country = NO
    }

    [Fact]
    public void Several_text_facets_are_allowed_and_each_has_its_own_key()
    {
        var dashboard = Build(b => b.TextFacet("amount", (order, text) => order.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(text)));

        var state = dashboard.Calculate(Text("open").With("amount", new TextSelection("5")));

        Assert.Equal([2, 6, 8], state.Items.Select(o => o.Id)); // 250, 2500, 75
    }

    [Fact]
    public void Text_selections_serialise_as_text_and_round_trip()
    {
        var selections = Text("acme").Toggle("Country", "SE");

        string json = Shared.Serializer.ToJson(selections);
        JsonObject parsed = (JsonObject)JsonNode.Parse(json)!;

        Assert.Equal("""{"text":"acme"}""", parsed["search"]!.ToJsonString());
        Assert.Equal(selections, Shared.Serializer.FromJson(json));
    }

    [Theory]
    [InlineData("""{ "search": {} }""")]
    [InlineData("""{ "search": { "text": "   " } }""")]
    [InlineData("""{ "search": { "text": 5 } }""")]
    [InlineData("""{ "search": { "text": null } }""")]
    [InlineData("""{ "search": { "values": ["open"] } }""")]
    public void Unreadable_text_selections_are_dropped_not_thrown(string json)
    {
        Assert.Equal(Selections.Empty, Shared.Serializer.FromJson(json));
    }

    [Fact]
    public void Read_text_is_trimmed_like_typed_text()
    {
        Selections restored = Shared.Serializer.FromJson("""{ "search": { "text": " acme " } }""");

        Assert.Equal(Text("acme"), restored);
    }

    [Fact]
    public void The_builder_validates_eagerly()
    {
        Assert.Throws<ArgumentNullException>(() => Build(b => b.TextFacet("t", null!)));
        Assert.Throws<ArgumentException>(() => Build(b => b.TextFacet(" ", (_, _) => true)));
        Assert.Throws<ArgumentException>(() => Build(b => b.TextFacet("Country", (_, _) => true)));
        Assert.Throws<ArgumentException>(() => Build(b => b.TextFacet("t", (_, _) => true).Name("")));
    }

    [Fact]
    public void The_facet_is_described_with_its_kind_and_name()
    {
        var dashboard = Build(b => b.TextFacet("t", (_, _) => true).Name("Find"));

        Assert.Equal(new FacetInfo("search", "search", FacetKind.Text), dashboard.Facets[2]);
        Assert.Equal(new FacetInfo("t", "Find", FacetKind.Text), dashboard.Facets[3]);
    }

    [Fact]
    public void An_empty_dataset_calculates()
    {
        var empty = Dashboard.Create(Array.Empty<Order>(), b => b.TextFacet("t", (_, _) => true));

        var state = empty.Calculate(Selections.Empty.With("t", new TextSelection("x")));

        Assert.Equal(0, state.MatchingCount);
        Assert.Equal("x", ((TextFacetState)state.Facet("t")).Text);
    }
}
