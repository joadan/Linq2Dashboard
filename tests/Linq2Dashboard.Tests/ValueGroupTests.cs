namespace Linq2Dashboard.Tests;

/// <summary>Groups of values in a value facet (concept §6, design §2.2, §2.4, §4.4).</summary>
public class ValueGroupTests
{
    // Row: Country Status   Kind
    //  0:  SE      Open     Web
    //  1:  NO      Open     Store
    //  2:  null    Closed   Web
    //  3:  SE      Closed   Web
    //  4:  DK      Pending  Store
    //  5:  se      Open     Web
    //  6:  null    Pending  Store
    //  7:  NO      Open     Web
    private static readonly Dictionary<string, string> Markets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SE"] = "North",
        ["NO"] = "North",
        ["DK"] = "South",
    };

    private static Dashboard<Order> Build(Func<Order, string?> group, Action<ValueFacetBuilder<Order, string?>>? extra = null) =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            ValueFacetBuilder<Order, string?> country = b.ValueFacet(x => x.Country).GroupBy(group);
            extra?.Invoke(country);
            b.ValueFacet(x => x.Status);
            b.RangeFacet(x => x.Amount);
        });

    private static readonly Dashboard<Order> Shared = Build(x => x.Country is null ? null : Markets[x.Country]);

    private static ValueFacetState Country(DashboardState<Order> state) => (ValueFacetState)state.Facet("Country");

    private static ValueGroup Group(DashboardState<Order> state, string name) => Country(state).Groups.Single(g => g.Name == name);

    [Fact]
    public void A_group_lists_its_values_with_their_counts_summed()
    {
        var country = Country(Shared.Calculate());

        Assert.Equal(["North", "South"], country.Groups.Select(g => g.Name));
        ValueGroup north = country.Groups[0];
        Assert.Equal((5, 5), (north.TotalCount, north.FilteredCount));
        Assert.Equal(["SE", "NO"], north.Values.Select(v => v.Value));
        Assert.Equal((1, 1), (country.Groups[1].TotalCount, country.Groups[1].FilteredCount));
    }

    [Fact]
    public void Group_counts_follow_the_facets_context()
    {
        // Status Open leaves rows 0, 1, 5 and 7: SE 2, NO 2, DK 0.
        var state = Shared.Calculate(Selections.Empty.With("Status", ValueSelection.Of("Open")));

        ValueGroup north = Group(state, "North");
        Assert.Equal((5, 4), (north.TotalCount, north.FilteredCount));
        Assert.Equal(north.Values.Sum(v => v.FilteredCount), north.FilteredCount);
    }

    [Fact]
    public void A_group_whose_values_all_count_zero_stays()
    {
        var state = Shared.Calculate(Selections.Empty.With("Status", ValueSelection.Of("Open")));

        ValueGroup south = Group(state, "South");
        Assert.Equal((1, 0), (south.TotalCount, south.FilteredCount));
    }

    [Fact]
    public void Values_stay_the_flat_list_of_every_value_including_null()
    {
        var country = Country(Shared.Calculate());

        Assert.Equal(["SE", "NO", null, "DK"], country.Values.Select(v => v.Value)); // rank order; null counts 2
        Assert.Same(country.Values[0], country.Groups[0].Values[0]);
    }

    [Fact]
    public void The_null_value_is_never_in_a_group()
    {
        var dashboard = Build(_ => "All");
        var country = Country(dashboard.Calculate());

        ValueGroup all = Assert.Single(country.Groups);
        Assert.Equal(["SE", "NO", "DK"], all.Values.Select(v => v.Value));
        Assert.Equal(6, all.TotalCount);
        Assert.Null(country.GroupOf(null));
    }

    [Fact]
    public void A_null_group_leaves_the_value_ungrouped()
    {
        var dashboard = Build(x => x.Country is "DK" ? null : "North");
        var country = Country(dashboard.Calculate());

        ValueGroup north = Assert.Single(country.Groups);
        Assert.DoesNotContain(north.Values, v => v.Value is "DK");
        Assert.Contains(country.Values, v => v.Value is "DK");
        Assert.Null(country.GroupOf("DK"));
    }

    [Fact]
    public void The_group_is_read_from_the_first_row_of_each_value_and_disagreeing_rows_are_no_error()
    {
        // NO is introduced by row 1 (Store); row 7 (Web) disagrees and is ignored.
        var dashboard = Build(x => x.Kind.ToString());
        var country = Country(dashboard.Calculate());

        Assert.Equal("Store", country.GroupOf("NO"));
        Assert.Equal("Web", country.GroupOf("SE"));
        Assert.Equal(["NO", "DK"], country.Groups.Single(g => g.Name == "Store").Values.Select(v => v.Value));
    }

    [Fact]
    public void A_group_can_be_a_lookup_by_the_value()
    {
        var country = Country(Shared.Calculate());

        Assert.Equal("North", country.GroupOf("SE"));
        Assert.Equal("South", country.GroupOf("DK"));
    }

    [Fact]
    public void Group_names_compare_ignoring_case_and_keep_the_first_spelling()
    {
        // SE (row 0) says "North" before NO (row 1) says "north".
        var dashboard = Build(x => x.Id == 2 ? "north" : x.Country is null ? null : "North");
        var country = Country(dashboard.Calculate());

        ValueGroup north = Assert.Single(country.Groups);
        Assert.Equal("North", north.Name);
        Assert.Equal(["SE", "NO", "DK"], north.Values.Select(v => v.Value));
    }

    [Fact]
    public void GroupOf_answers_for_any_value_in_the_facets_own_forms()
    {
        var country = Country(Shared.Calculate());

        Assert.Equal("North", country.GroupOf("se"));
        Assert.Null(country.GroupOf("FI"));
    }

    [Fact]
    public void A_facet_without_GroupBy_has_no_groups()
    {
        var status = (ValueFacetState)Shared.Calculate().Facet("Status");

        Assert.Empty(status.Groups);
        Assert.Null(status.GroupOf("Open"));
    }

    [Fact]
    public void Groups_rank_by_filtered_count_by_default_and_ties_go_by_name()
    {
        // Pending leaves rows 4 and 6: North 0, South 1.
        var pending = Country(Shared.Calculate(Selections.Empty.With("Status", ValueSelection.Of("Pending"))));
        Assert.Equal(["South", "North"], pending.Groups.Select(g => g.Name));

        // Store {NO, DK} and Web {SE} both count 3 of 3.
        var tied = Country(Build(x => x.Kind.ToString()).Calculate());
        Assert.Equal(["Store", "Web"], tied.Groups.Select(g => g.Name));
    }

    [Fact]
    public void Groups_rank_by_total_count_under_RankBy_TotalCount()
    {
        var dashboard = Build(x => x.Country is null ? null : Markets[x.Country], c => c.RankBy(RankMode.TotalCount));
        var pending = Country(dashboard.Calculate(Selections.Empty.With("Status", ValueSelection.Of("Pending"))));

        Assert.Equal(["North", "South"], pending.Groups.Select(g => g.Name));
    }

    [Fact]
    public void A_group_with_no_value_in_a_scope_is_left_out()
    {
        // The Web rows have no DK, so the South group has nothing to show.
        var web = Shared.ScopeTo(x => x.Kind == OrderKind.Web);
        var country = Country(web.Calculate());

        ValueGroup north = Assert.Single(country.Groups);
        Assert.Equal((4, 4), (north.TotalCount, north.FilteredCount));
        Assert.Equal("South", country.GroupOf("DK"));
    }

    [Fact]
    public void Toggling_a_group_selects_all_its_values()
    {
        ValueGroup north = Group(Shared.Calculate(), "North");

        Selections selections = Selections.Empty.ToggleGroup("Country", north);

        Assert.Equal(ValueSelection.Of("SE", "NO"), selections["Country"]);
        var state = Shared.Calculate(selections);
        Assert.Equal(5, state.MatchingCount);
        Assert.True(Group(state, "North").Selected);
        Assert.False(Group(state, "North").PartlySelected);
    }

    [Fact]
    public void A_group_with_some_values_selected_is_partly_selected_and_a_toggle_completes_it()
    {
        Selections selections = Selections.Empty.Toggle("Country", "SE");
        ValueGroup north = Group(Shared.Calculate(selections), "North");
        Assert.False(north.Selected);
        Assert.True(north.PartlySelected);

        selections = selections.ToggleGroup("Country", north);

        Assert.True(Group(Shared.Calculate(selections), "North").Selected);
    }

    [Fact]
    public void Toggling_a_fully_selected_group_removes_its_values_and_keeps_the_others()
    {
        ValueGroup north = Group(Shared.Calculate(), "North");
        Selections selections = Selections.Empty.Toggle("Country", "DK").ToggleGroup("Country", north);

        selections = selections.ToggleGroup("Country", north);

        Assert.Equal(ValueSelection.Of("DK"), selections["Country"]);
        Assert.False(Selections.Empty.ToggleGroup("Country", north).ToggleGroup("Country", north).Contains("Country"));
    }

    [Fact]
    public void ToggleGroup_decides_from_the_selection_not_from_the_groups_flags()
    {
        // The group comes from a state where nothing was selected; the selection has since got both values.
        ValueGroup stale = Group(Shared.Calculate(), "North");
        Selections selections = Selections.Empty.With("Country", ValueSelection.Of("SE", "NO"));

        Assert.False(selections.ToggleGroup("Country", stale).Contains("Country"));
    }

    [Fact]
    public void A_group_selection_is_an_ordinary_value_selection_in_JSON()
    {
        Selections selections = Selections.Empty.ToggleGroup("Country", Group(Shared.Calculate(), "North"));

        string json = Shared.Serializer.ToJson(selections);

        Assert.Contains("\"SE\"", json);
        Assert.Contains("\"NO\"", json);
        Assert.DoesNotContain("North", json);
        Assert.Equal(selections, Shared.Serializer.FromJson(json));
    }

    [Fact]
    public void ToggleGroup_on_a_facet_with_another_kind_of_selection_throws()
    {
        ValueGroup north = Group(Shared.Calculate(), "North");
        Selections selections = Selections.Empty.With("Amount", RangeSelection.Between(1, 2));

        Assert.Throws<InvalidOperationException>(() => selections.ToggleGroup("Amount", north));
    }

    [Fact]
    public void GroupBy_and_Top_cannot_be_combined_in_either_order()
    {
        Assert.Throws<InvalidOperationException>(() => Dashboard.Create(TestData.Orders(), b => b.ValueFacet(x => x.Country).Top(2).GroupBy(_ => "All")));
        Assert.Throws<InvalidOperationException>(() => Dashboard.Create(TestData.Orders(), b => b.ValueFacet(x => x.Country).GroupBy(_ => "All").Top(2)));
    }

    [Fact]
    public void The_group_function_runs_once_per_distinct_value()
    {
        int calls = 0;
        Build(x =>
        {
            calls++;
            return "All";
        });

        Assert.Equal(3, calls); // SE, NO, DK; never for the null value
    }
}
