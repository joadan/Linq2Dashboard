using System.Globalization;
using System.Text;

namespace Linq2Dashboard.Tests;

/// <summary>Concept §4.10: a dashboard can be scoped to a subset of its dataset.</summary>
public class ScopeTests
{
    // Now is Sunday 15 March 2026, 11:00 in Stockholm.
    private static readonly FixedTimeProvider Clock = new(TestData.Instant("2026-03-15T10:00:00Z"));

    // Row: Id Country Status  Active Amount Discount OrderDate (Stockholm)
    //  0:  1  SE      Open    T      100    10       15 Jan
    //  1:  2  NO      Open    T      250    null      3 Feb
    //  2:  3  null    Closed  F      500    50       20 Feb
    //  3:  4  SE      Closed  T      999.5  0         1 Mar 23:30
    //  4:  5  DK      Pending F      1000   null     10 Mar
    //  5:  6  se      Open    T      2500   250      31 Mar 22:30
    //  6:  7  null    Pending F      0      null      2 Apr
    //  7:  8  NO      Open    T      75     5        15 Apr
    // The active rows are 0, 1, 3, 5 and 7: SE 3, NO 2; Open 4, Closed 1; revenue 3 924.5.
    private static void Configure(DashboardBuilder<Order> b)
    {
        b.ValueFacet(x => x.Country).Top(2);
        b.ValueFacet(x => x.Status);
        b.BooleanFacet(x => x.IsActive);
        b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);
        b.RangeFacet(x => x.Discount).Buckets(10, 100);
        b.DateFacet(x => x.OrderDate).TimeZone(TestData.Stockholm).Presets(DatePreset.ThisMonth, DatePreset.Last7Days);
        b.TextFacet("search", (x, text) => x.Status.Contains(text, StringComparison.OrdinalIgnoreCase));
        b.CountMetric("orders");
        b.SumMetric("revenue", x => x.Amount);
        b.AverageMetric("avgDiscount", x => x.Discount);
        b.DistinctMetric("countries", x => x.Country);
        b.CalculatedMetric("perCountry", m => m["revenue"] / m["countries"]);
        b.OrderByDescending(x => x.Amount);
        b.UseTimeProvider(Clock);
    }

    private static readonly Dashboard<Order> Parent = Dashboard.Create(TestData.Orders(), Configure);

    private static readonly Dashboard<Order> Active = Parent.ScopeTo(x => x.IsActive);

    private static readonly Dashboard<Order> RebuiltActive = Dashboard.Create(TestData.Orders(), b =>
    {
        b.Where(x => x.IsActive);
        Configure(b);
    });

    private static readonly Selections[] SelectionSets =
    [
        Selections.Empty,
        Selections.Empty.With("Country", ValueSelection.Of("SE")),
        Selections.Empty.With("Country", ValueSelection.Of("NO", null)).With("Status", ValueSelection.Of("Open")),
        Selections.Empty.With("Amount", new RangeSelection(100, 1000)),
        Selections.Empty.With("Discount", RangeSelection.OnlyNull),
        Selections.Empty.With("OrderDate", DateSelection.Relative(DatePreset.ThisMonth)),
        Selections.Empty.With("search", new TextSelection("open")).With("IsActive", ValueSelection.Of(true)),
    ];

    private static ValueFacetState Values(DashboardState<Order> state, string key) => (ValueFacetState)state.Facet(key);

    /// <summary>Everything observable in a state, as text, so two states can be compared whole.</summary>
    private static string Snapshot(DashboardState<Order> state)
    {
        var text = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;
        text.AppendLine(culture, $"total {state.TotalCount} matching {state.MatchingCount} pages {state.PageCount(2)}");
        text.AppendLine(culture, $"items {string.Join(",", state.Items.Select(o => o.Id))}");
        text.AppendLine(culture, $"page1 {string.Join(",", state.GetPage(1, 2).Items.Select(o => o.Id))}");
        foreach (FacetState facet in state.Facets)
        {
            text.AppendLine(culture, $"{facet.Key} context {facet.ContextCount} selected {facet.HasSelection}");
            switch (facet)
            {
                case ValueFacetState value:
                    text.AppendLine(culture, $"  distinct {value.DistinctCount} other {value.Other}");
                    foreach (FacetValue v in value.Values)
                    {
                        text.AppendLine(culture, $"  {v.Value ?? "null"} {v.TotalCount}/{v.FilteredCount} {v.Selected}");
                    }

                    text.AppendLine(culture, $"  search {string.Join(",", value.Search("e").Select(v => $"{v.Value}:{v.TotalCount}/{v.FilteredCount}"))}");
                    break;
                case RangeFacetState range:
                    text.AppendLine(culture, $"  null {range.Null.TotalCount}/{range.Null.FilteredCount}"); // Min and Max are the parent's, see Buckets_come_from_the_parent
                    foreach (RangeBucket bucket in range.Buckets)
                    {
                        text.AppendLine(culture, $"  [{bucket.From},{bucket.To}) {bucket.TotalCount}/{bucket.FilteredCount} {bucket.Selected}");
                    }

                    break;
                case DateFacetState date:
                    text.AppendLine(culture, $"  null {date.Null.TotalCount}/{date.Null.FilteredCount}");
                    foreach (DateBucket bucket in date.Buckets)
                    {
                        text.AppendLine(culture, $"  {bucket.PeriodStart:yyyy-MM} {bucket.TotalCount}/{bucket.FilteredCount} {bucket.Selected}");
                    }

                    foreach (PresetState preset in date.Presets)
                    {
                        text.AppendLine(culture, $"  {preset.Preset} {preset.TotalCount}/{preset.FilteredCount} {preset.Selected}");
                    }

                    break;
                case TextFacetState textFacet:
                    text.AppendLine(culture, $"  text {textFacet.Text}");
                    break;
            }
        }

        foreach (MetricState metric in state.Metrics)
        {
            text.AppendLine(culture, $"{metric.Key} {metric.Value} share {metric.Share}");
        }

        return text.ToString();
    }

    [Fact]
    public void A_scoped_dashboard_equals_one_built_with_the_predicate_as_a_fixed_filter()
    {
        Assert.Equal(RebuiltActive.TotalCount, Active.TotalCount);
        foreach (Selections selections in SelectionSets)
        {
            Assert.Equal(Snapshot(RebuiltActive.Calculate(selections)), Snapshot(Active.Calculate(selections)));
        }
    }

    [Fact]
    public void Totals_are_measured_against_the_scope()
    {
        var state = Active.Calculate();

        Assert.Equal(5, state.TotalCount);
        Assert.Equal(5, state.MatchingCount);
        Assert.Equal([("SE", 3, 3), ("NO", 2, 2)], Values(state, "Country").Values.Select(v => (v.Value, v.TotalCount, v.FilteredCount)));

        var amount = (RangeFacetState)state.Facet("Amount");
        Assert.Equal([1, 2, 1, 1], amount.Buckets.Select(b => b.TotalCount)); // 75 | 100, 250 | 999.5 | 2500

        var date = (DateFacetState)state.Facet("OrderDate");
        Assert.Equal(2, date.Presets[0].TotalCount); // ThisMonth: rows 3 and 5; the parent has row 4 as well
        Assert.Equal(3, ((DateFacetState)Parent.Calculate().Facet("OrderDate")).Presets[0].TotalCount);
    }

    [Fact]
    public void Values_no_row_in_the_scope_has_do_not_appear()
    {
        var state = Active.Calculate();

        var country = Values(state, "Country");
        Assert.Equal(2, country.DistinctCount);
        Assert.Null(country.Other); // Top(2) over exactly two values truncates nothing
        Assert.DoesNotContain(country.Values, v => v.IsNull || Equals(v.Value, "DK"));
        Assert.Empty(country.Search("DK"));

        Assert.DoesNotContain(Values(state, "Status").Values, v => Equals(v.Value, "Pending"));
        Assert.Equal([true], Values(state, "IsActive").Values.Select(v => v.Value));
    }

    [Fact]
    public void Other_is_measured_against_the_scope()
    {
        var scoped = Parent.ScopeTo(x => x.Country is not "DK").Calculate(); // SE 3, NO 2, null 2; Top(2) leaves null as Other

        var country = Values(scoped, "Country");
        Assert.Equal(7, scoped.TotalCount);
        Assert.Equal(3, country.DistinctCount);
        Assert.Equal(["SE", "NO"], country.Values.Select(v => v.Value));
        Assert.Equal(new FacetCount(2, 2), country.Other);
    }

    [Fact]
    public void Share_of_the_total_is_measured_against_the_scope()
    {
        var state = Active.Calculate(Selections.Empty.With("Country", ValueSelection.Of("SE")));

        Assert.Equal(3, state.Metric("orders").Value);
        Assert.Equal(3.0 / 5, state.Metric("orders").Share);
        Assert.Equal(3599.5, state.Metric("revenue").Value);
        Assert.Equal(3599.5 / 3924.5, state.Metric("revenue").Share);
        Assert.Equal(1, state.Metric("countries").Value);
        Assert.Equal(1.0 / 2, state.Metric("countries").Share);
        Assert.Equal(3599.5, state.Metric("perCountry").Value);
    }

    [Fact]
    public void Buckets_come_from_the_parent()
    {
        var parent = Dashboard.Create(TestData.Orders(), b =>
        {
            b.RangeFacet(x => x.Amount).AutoBuckets(5);
            b.DateFacet(x => x.OrderDate).TimeZone(TestData.Stockholm);
        });
        var parentState = parent.Calculate();
        var scopedState = parent.ScopeTo(x => x.OrderDate.Month == 2).Calculate(); // rows 1 and 2: 250 and 500, both February

        var parentAmount = (RangeFacetState)parentState.Facet("Amount");
        var scopedAmount = (RangeFacetState)scopedState.Facet("Amount");
        Assert.Equal(parentAmount.Buckets.Select(b => (b.From, b.To)), scopedAmount.Buckets.Select(b => (b.From, b.To)));
        Assert.Equal((parentAmount.Min, parentAmount.Max), (scopedAmount.Min, scopedAmount.Max));
        Assert.Equal(2, scopedAmount.Buckets.Sum(b => b.TotalCount));

        var parentDate = (DateFacetState)parentState.Facet("OrderDate");
        var scopedDate = (DateFacetState)scopedState.Facet("OrderDate");
        Assert.Equal(parentDate.Buckets.Select(b => b.PeriodStart), scopedDate.Buckets.Select(b => b.PeriodStart));
        Assert.Equal([0, 2, 0, 0], scopedDate.Buckets.Select(b => b.TotalCount));

        // A dashboard built over the subset would have derived February alone.
        var rebuilt = Dashboard.Create(TestData.Orders(), b =>
        {
            b.Where(x => x.OrderDate.Month == 2);
            b.DateFacet(x => x.OrderDate).TimeZone(TestData.Stockholm);
        });
        Assert.Single(((DateFacetState)rebuilt.Calculate().Facet("OrderDate")).Buckets);
    }

    [Fact]
    public void The_same_selections_apply_to_every_scope()
    {
        Selections selections = Selections.Empty
            .With("Country", ValueSelection.Of("NO"))
            .With("Amount", new RangeSelection(50, 300));

        string json = Parent.Serializer.ToJson(selections);
        Assert.Same(Parent.Serializer, Active.Serializer);
        Assert.Equal(selections, Active.Serializer.FromJson(json));

        var state = Active.Calculate(Active.Serializer.FromJson(json));
        Assert.Equal([2, 8], state.Items.Select(o => o.Id));
        Assert.Equal(2, state.MatchingCount);
    }

    [Fact]
    public void Scopes_compose()
    {
        var twice = Active.ScopeTo(x => x.Country is not null && x.Country.Equals("SE", StringComparison.OrdinalIgnoreCase));
        var once = Parent.ScopeTo(x => x.IsActive && x.Country is not null && x.Country.Equals("SE", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(3, twice.TotalCount);
        foreach (Selections selections in SelectionSets)
        {
            Assert.Equal(Snapshot(once.Calculate(selections)), Snapshot(twice.Calculate(selections)));
        }
    }

    [Fact]
    public void The_scope_is_not_a_selection()
    {
        var state = Active.Calculate();

        Assert.True(state.Selections.IsEmpty);
        Assert.All(state.Facets, f => Assert.False(f.HasSelection));
        Assert.Equal("{}", Active.Serializer.ToJson(state.Selections));
    }

    [Fact]
    public void Scoping_leaves_the_parent_unchanged()
    {
        var before = Snapshot(Parent.Calculate());
        _ = Parent.ScopeTo(x => x.IsActive).Calculate(Selections.Empty.With("Country", ValueSelection.Of("SE")));

        Assert.Equal(8, Parent.TotalCount);
        Assert.Equal(before, Snapshot(Parent.Calculate()));
        Assert.Equal(Parent.Facets, Active.Facets);
        Assert.Equal(Parent.Metrics, Active.Metrics);
    }

    [Fact]
    public void Result_pages_keep_the_parents_order()
    {
        var state = Active.Calculate();

        Assert.Equal([6, 4, 2, 1, 8], state.Items.Select(o => o.Id)); // amount descending, inactive rows gone
        Assert.Equal([2, 1], state.GetPage(1, 2).Items.Select(o => o.Id));
        Assert.Equal([8], state.GetItems(4, 10).Select(o => o.Id));
    }

    [Fact]
    public void An_empty_scope_is_a_dashboard_with_no_rows()
    {
        var state = Parent.ScopeTo(_ => false).Calculate(Selections.Empty.With("Country", ValueSelection.Of("SE")));

        Assert.Equal(0, state.TotalCount);
        Assert.Equal(0, state.MatchingCount);
        Assert.Empty(Values(state, "Country").Values);
        Assert.Equal(0, Values(state, "Country").DistinctCount);
        Assert.All(((RangeFacetState)state.Facet("Amount")).Buckets, b => Assert.Equal(0, b.TotalCount));
        Assert.Equal(0, state.Metric("orders").Value);
        Assert.Null(state.Metric("orders").Share);
        Assert.Null(state.Metric("revenue").Value);
        Assert.Empty(state.Items);
    }

    [Fact]
    public void A_text_facet_matches_within_the_scope()
    {
        var state = Active.Calculate(Selections.Empty.With("search", new TextSelection("closed")));

        Assert.Equal(1, state.MatchingCount); // row 3; row 2 is closed but inactive
        Assert.Equal([4], state.Items.Select(o => o.Id));
        Assert.Equal(5, state.Facet("search").ContextCount);
    }

    [Fact]
    public void A_scoped_dashboard_shares_the_parents_row_set_cache()
    {
        var parent = Dashboard.Create(TestData.Orders(), Configure);
        var scoped = parent.ScopeTo(x => x.IsActive);
        Selections selections = Selections.Empty.With("Status", ValueSelection.Of("Open"));

        scoped.Calculate(selections);
        Assert.Equal(1, parent.CachedRowSetCount);
        Assert.Equal(1, scoped.CachedRowSetCount);
        Assert.Equal(0, parent.CachedStateCount); // states are per dashboard
        Assert.Equal(1, scoped.CachedStateCount);

        parent.Calculate(selections);
        Assert.Equal(1, parent.CachedRowSetCount); // the row set was already there
    }

    [Fact]
    public void A_scope_given_as_selections_equals_the_predicate_form()
    {
        (Selections Scope, Func<Order, bool> Predicate)[] forms =
        [
            (Selections.Empty.With("Country", ValueSelection.Of("se")), x => string.Equals(x.Country, "SE", StringComparison.OrdinalIgnoreCase)),
            (Selections.Empty.With("Country", ValueSelection.Of(null, "DK")), x => x.Country is null or "DK"),
            (Selections.Empty.With("Amount", new RangeSelection(100, 1000)), x => x.Amount is >= 100 and <= 1000),
            (Selections.Empty.With("Discount", RangeSelection.OnlyNull), x => x.Discount is null),
            (Selections.Empty.With("OrderDate", DateSelection.Relative(DatePreset.ThisMonth)), x => x.OrderDate.Month == 3),
            (Selections.Empty.With("search", new TextSelection("open")).With("IsActive", ValueSelection.Of(true)), x => x.Status.Contains("open", StringComparison.OrdinalIgnoreCase) && x.IsActive),
            (Selections.Empty, _ => true),
        ];

        foreach ((Selections scope, Func<Order, bool> predicate) in forms)
        {
            Dashboard<Order> bySelections = Parent.ScopeTo(scope);
            Dashboard<Order> byPredicate = Parent.ScopeTo(predicate);
            Assert.Equal(byPredicate.TotalCount, bySelections.TotalCount);
            foreach (Selections selections in SelectionSets)
            {
                Assert.Equal(Snapshot(byPredicate.Calculate(selections)), Snapshot(bySelections.Calculate(selections)));
            }
        }
    }

    [Fact]
    public void A_scope_from_selections_starts_with_nothing_selected_and_shows_only_the_values_in_scope()
    {
        var state = Parent.ScopeTo(Selections.Empty.With("Country", ValueSelection.Of("SE"))).Calculate();

        Assert.True(state.Selections.IsEmpty);
        Assert.Equal(3, state.TotalCount);
        var country = Values(state, "Country");
        Assert.False(country.HasSelection);
        Assert.Equal([("SE", 3, 3, false)], country.Values.Select(v => (v.Value, v.TotalCount, v.FilteredCount, v.Selected)));
        Assert.Equal(1, country.DistinctCount);
    }

    [Fact]
    public void A_selection_on_a_scoped_facet_narrows_further()
    {
        var nordic = Parent.ScopeTo(Selections.Empty.With("Country", ValueSelection.Of("SE", "NO")));

        Assert.Equal(5, nordic.TotalCount);
        Assert.Equal(3, nordic.Calculate(Selections.Empty.With("Country", ValueSelection.Of("SE"))).MatchingCount);

        var outside = nordic.Calculate(Selections.Empty.With("Country", ValueSelection.Of("DK")));
        Assert.Equal(0, outside.MatchingCount);
        Assert.DoesNotContain(Values(outside, "Country").Values, v => Equals(v.Value, "DK"));
    }

    [Fact]
    public void A_relative_preset_is_frozen_when_the_scope_is_made()
    {
        var clock = new AdjustableTimeProvider(TestData.Instant("2026-03-15T10:00:00Z"));
        var parent = Dashboard.Create(TestData.Orders(), b =>
        {
            b.DateFacet(x => x.OrderDate).TimeZone(TestData.Stockholm).Presets(DatePreset.ThisMonth);
            b.UseTimeProvider(clock);
        });
        var thisMonth = parent.ScopeTo(Selections.Empty.With("OrderDate", DateSelection.Relative(DatePreset.ThisMonth)));
        Assert.Equal(3, thisMonth.TotalCount); // March: rows 3, 4 and 5

        clock.Now = TestData.Instant("2026-04-20T10:00:00Z");
        Assert.Equal(3, thisMonth.Calculate().TotalCount);
        Assert.Equal(3, ((DateFacetState)thisMonth.Calculate().Facet("OrderDate")).Buckets.Sum(b => b.TotalCount));
        Assert.Equal(2, ((DateFacetState)parent.Calculate().Facet("OrderDate")).Presets[0].TotalCount); // the parent follows the clock: April has rows 6 and 7
    }

    [Fact]
    public void ScopeTo_with_selections_rejects_unknown_keys_and_null()
    {
        Assert.Throws<ArgumentNullException>(() => Parent.ScopeTo((Selections)null!));
        var e = Assert.Throws<ArgumentException>(() => Parent.ScopeTo(Selections.Empty.With("Nope", ValueSelection.Of(1))));
        Assert.Contains("Nope", e.Message);
    }

    [Fact]
    public void ScopeTo_rejects_a_null_predicate()
    {
        Assert.Throws<ArgumentNullException>(() => Parent.ScopeTo((Func<Order, bool>)null!));
    }
}
