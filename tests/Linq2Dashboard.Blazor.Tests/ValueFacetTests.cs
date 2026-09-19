using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

public class ValueFacetTests : BunitContext
{
    private static Dashboard<Order> BuildDashboard() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.ValueFacet(x => x.Status).Name("Order status").Searchable();
            b.ValueFacet("top", x => x.Country).Top(2);
            b.ValueFacet("city", x => x.Country).Label(x => x.Address?.City).Searchable();
            b.BooleanFacet(x => x.IsActive);
            b.RangeFacet(x => x.Amount);
        });

    private IRenderedComponent<DashboardView<Order>> RenderFacet(
        string key, Selections? selections = null, Action<Selections>? onChanged = null, Action<ComponentParameterCollectionBuilder<ValueFacet<Order>>>? configure = null) =>
        Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, BuildDashboard());
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            if (selections is not null)
            {
                parameters.Add(p => p.Selections, selections);
            }

            if (onChanged is not null)
            {
                parameters.Add(p => p.SelectionsChanged, onChanged);
            }

            parameters.AddContent<ValueFacet<Order>>(facet =>
            {
                facet.Add(f => f.Key, key);
                configure?.Invoke(facet);
            });
        });

    private static IReadOnlyList<IElement> Items(IRenderedComponent<DashboardView<Order>> cut) =>
        cut.FindAll("li.l2d-facet-value");

    private static string Label(IElement item) => item.QuerySelector(".l2d-facet-value-label")!.TextContent.Trim();

    private static string Count(IElement item) => item.QuerySelector(".l2d-facet-value-count")!.TextContent.Trim();

    [Fact]
    public void Renders_name_values_and_filtered_counts_in_rank_order()
    {
        var cut = RenderFacet("Status");

        Assert.Equal("Order status", cut.Find(".l2d-facet-title").TextContent);
        var items = Items(cut);
        Assert.Equal(["Open", "Closed", "Pending"], items.Select(Label));
        Assert.Equal(["4 (4)", "2 (2)", "2 (2)"], items.Select(Count));
        Assert.Empty(cut.FindAll(".l2d-facet-clear"));
        Assert.Empty(cut.FindAll(".l2d-facet-other"));
    }

    [Fact]
    public void The_null_value_is_rendered_with_the_formatters_label_and_marked()
    {
        var cut = RenderFacet("Country");

        var item = Items(cut).Single(li => li.ClassList.Contains("l2d-null"));
        Assert.Equal("(none)", Label(item));
        Assert.Equal("2 (2)", Count(item));
    }

    [Fact]
    public void Clicking_a_value_toggles_it_and_marks_it_selected()
    {
        Selections? raised = null;
        var cut = RenderFacet("Country", onChanged: s => raised = s);

        Items(cut).Single(li => Label(li) == "SE").QuerySelector("button")!.Click();

        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("SE")), raised);
        var selected = Items(cut).Single(li => Label(li) == "SE");
        Assert.Contains("l2d-selected", selected.ClassName);
        Assert.Equal("true", selected.QuerySelector("button")!.GetAttribute("aria-pressed"));
        Assert.Single(cut.FindAll(".l2d-facet-clear"));

        // The facet's own counts are unchanged by its own selection (concept §4.2).
        Assert.Equal(["3 (3)", "2 (2)", "2 (2)", "1 (1)"], Items(cut).Select(Count));
    }

    [Fact]
    public void Clicking_the_null_value_selects_the_null_rows()
    {
        Selections? raised = null;
        var cut = RenderFacet("Country", onChanged: s => raised = s);

        Items(cut).Single(li => li.ClassList.Contains("l2d-null")).QuerySelector("button")!.Click();

        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of([null])), raised);
    }

    [Fact]
    public void Zero_count_values_are_marked_and_can_be_hidden()
    {
        var selections = Selections.Empty.With("Country", ValueSelection.Of("SE"));

        var shown = RenderFacet("Status", selections);
        var pending = Items(shown).Single(li => Label(li) == "Pending");
        Assert.Contains("l2d-zero", pending.ClassName);
        Assert.Equal("0 (2)", Count(pending));

        var hidden = RenderFacet("Status", selections, configure: f => f.Add(x => x.HideZeroCounts, true));
        Assert.Equal(["Open", "Closed"], Items(hidden).Select(Label));
    }

    [Fact]
    public void A_selected_zero_count_value_stays_visible_when_zero_counts_are_hidden()
    {
        // Status = Pending and Country = SE: no Swedish order is pending, but Pending is selected and must stay.
        var selections = Selections.Empty.With("Country", ValueSelection.Of("SE")).With("Status", ValueSelection.Of("Pending"));

        var cut = RenderFacet("Status", selections, configure: f => f.Add(x => x.HideZeroCounts, true));

        Assert.Contains("Pending", Items(cut).Select(Label));
    }

    [Fact]
    public void Totals_are_shown_after_each_filtered_count_by_default()
    {
        var cut = RenderFacet("Country", Selections.Empty.With("Status", ValueSelection.Of("Open")));

        Assert.Equal("2 (3)", Count(Items(cut).Single(li => Label(li) == "SE")));
    }

    [Fact]
    public void ShowTotals_off_shows_the_filtered_count_alone()
    {
        var cut = RenderFacet("Country", Selections.Empty.With("Status", ValueSelection.Of("Open")), configure: f => f.Add(x => x.ShowTotals, false));

        Assert.Equal("2", Count(Items(cut).Single(li => Label(li) == "SE")));
        Assert.Empty(cut.FindAll(".l2d-facet-value-total"));
    }

    [Fact]
    public void Tooltip_shows_filtered_count_total_and_the_filtered_share_of_the_total()
    {
        var cut = RenderFacet("Country", Selections.Empty.With("Status", ValueSelection.Of("Open")));

        var button = Items(cut).Single(li => Label(li) == "SE").QuerySelector("button")!;
        Assert.Equal("2 (3) 66.7 %", button.GetAttribute("title"));
    }

    [Fact]
    public void Other_is_shown_when_top_n_truncates_and_hidden_while_searching()
    {
        var cut = RenderFacet("top");

        Assert.Equal(["SE", "NO"], Items(cut).Select(Label));
        Assert.Contains("Other", cut.Find(".l2d-facet-other").TextContent);
        Assert.Contains("3", cut.Find(".l2d-facet-other .l2d-facet-value-count").TextContent);
    }

    [Fact]
    public void Search_box_appears_only_for_searchable_facets_and_filters_the_list()
    {
        Assert.Empty(RenderFacet("Country").FindAll(".l2d-facet-search"));

        Selections? raised = null;
        var cut = RenderFacet("Status", onChanged: s => raised = s);
        var search = cut.Find(".l2d-facet-search");

        search.Input("en");
        Assert.Equal(["Open", "Pending"], Items(cut).Select(Label));

        search.Input("zzz");
        Assert.Empty(Items(cut));
        Assert.Equal("No matches", cut.Find(".l2d-facet-empty").TextContent);

        search.Input("clo");
        Items(cut).Single().QuerySelector("button")!.Click();
        Assert.Equal(Selections.Empty.With("Status", ValueSelection.Of("Closed")), raised);

        search.Input("");
        Assert.Equal(3, Items(cut).Count);
    }

    [Fact]
    public void Clear_removes_the_facets_selection_only()
    {
        Selections? raised = null;
        var selections = Selections.Empty.With("Country", ValueSelection.Of("SE")).With("Status", ValueSelection.Of("Open"));
        var cut = RenderFacet("Country", selections, s => raised = s);

        cut.Find(".l2d-facet-clear").Click();

        Assert.Equal(Selections.Empty.With("Status", ValueSelection.Of("Open")), raised);
        Assert.Empty(cut.FindAll(".l2d-facet-clear"));
    }

    [Fact]
    public void Boolean_facets_render_through_the_same_component()
    {
        var cut = RenderFacet("IsActive");

        Assert.Equal(["Yes", "No"], Items(cut).Select(Label));
    }

    [Fact]
    public void Wrong_kind_or_unknown_key_fails_clearly()
    {
        var wrongKind = Assert.ThrowsAny<Exception>(() => RenderFacet("Amount"));
        Assert.Contains("not a value or boolean facet", wrongKind.Message);

        var unknown = Assert.ThrowsAny<Exception>(() => RenderFacet("Nope"));
        Assert.Contains("Nope", unknown.Message);
    }

    [Fact]
    public void Labels_are_shown_instead_of_values_and_searched()
    {
        Selections? raised = null;
        var cut = RenderFacet("city", onChanged: s => raised = s);

        // Labels from the first row per country (concept §5); the null value keeps the formatter's label.
        Assert.Equal(["Stockholm", "Oslo", "(none)", "Copenhagen"], Items(cut).Select(Label));

        cut.Find(".l2d-facet-search").Input("oslo");
        Assert.Equal(["Oslo"], Items(cut).Select(Label));

        // The click still toggles the value, not the label.
        Items(cut).Single().QuerySelector("button")!.Click();
        Assert.Equal(Selections.Empty.With("city", ValueSelection.Of("NO")), raised);
    }

    [Fact]
    public void Sort_by_label_orders_alphabetically_with_null_last()
    {
        // Rank order is SE, NO, (none), DK; the UI reorders what the core presented (concept §6).
        var cut = RenderFacet("Country", configure: f => f.Add(x => x.Sort, FacetSort.Label));

        Assert.Equal(["DK", "NO", "SE", "(none)"], Items(cut).Select(Label));
    }

    [Fact]
    public void Sort_by_label_descending_keeps_null_last()
    {
        var cut = RenderFacet("Country", configure: f =>
        {
            f.Add(x => x.Sort, FacetSort.Label);
            f.Add(x => x.SortDescending, true);
        });

        Assert.Equal(["SE", "NO", "DK", "(none)"], Items(cut).Select(Label));
    }

    [Fact]
    public void Sort_by_label_uses_the_shown_label_not_the_value()
    {
        // Values are country codes, labels are cities: Copenhagen (DK), Oslo (NO), Stockholm (SE).
        var cut = RenderFacet("city", configure: f => f.Add(x => x.Sort, FacetSort.Label));

        Assert.Equal(["Copenhagen", "Oslo", "Stockholm", "(none)"], Items(cut).Select(Label));
    }

    [Fact]
    public void Sort_by_value_uses_the_values_own_order_with_null_last()
    {
        // Same keys as "Country" but labelled by city: value order is DK, NO, SE regardless of label.
        var cut = RenderFacet("city", configure: f => f.Add(x => x.Sort, FacetSort.Value));

        Assert.Equal(["Copenhagen", "Oslo", "Stockholm", "(none)"], Items(cut).Select(Label));

        var descending = RenderFacet("Country", configure: f =>
        {
            f.Add(x => x.Sort, FacetSort.Value);
            f.Add(x => x.SortDescending, true);
        });
        Assert.Equal(["SE", "NO", "DK", "(none)"], Items(descending).Select(Label));
    }

    [Fact]
    public void Rank_descending_reverses_the_core_order()
    {
        var cut = RenderFacet("Country", configure: f => f.Add(x => x.SortDescending, true));

        Assert.Equal(["DK", "(none)", "NO", "SE"], Items(cut).Select(Label));
    }

    [Fact]
    public void Sorting_applies_to_the_top_n_only_and_Other_stays_last()
    {
        // Top(2) by filtered count presents SE (3) and NO (2, ahead of null on the tiebreak); the core decides that, the UI only orders the two.
        var cut = RenderFacet("top", configure: f => f.Add(x => x.Sort, FacetSort.Label));

        Assert.Equal(["NO", "SE"], Items(cut).Select(Label));
        Assert.Equal("l2d-facet-other", cut.FindAll("ul.l2d-facet-values > li").Last().ClassName);
    }

    [Fact]
    public void Sorting_applies_to_search_results()
    {
        var cut = RenderFacet("city", configure: f => f.Add(x => x.Sort, FacetSort.Label));

        cut.Find(".l2d-facet-search").Input("o");
        Assert.Equal(["Copenhagen", "Oslo", "Stockholm"], Items(cut).Select(Label));
    }
}

public class LabelSortCultureTests : BunitContext
{
    private sealed record Town(string Name);

    private static readonly Town[] Towns = [new("Zürich"), new("Örebro"), new("Malmö")];

    private IReadOnlyList<string> Labels(CultureInfo culture) =>
        Render<DashboardView<Town>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, Dashboard.Create(Towns, b => b.ValueFacet(x => x.Name)));
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(culture));
            parameters.AddContent<Town, ValueFacet<Town>>(facet =>
            {
                facet.Add(f => f.Key, "Name");
                facet.Add(f => f.Sort, FacetSort.Label);
            });
        }).FindAll("li.l2d-facet-value .l2d-facet-value-label").Select(e => e.TextContent.Trim()).ToList();

    [Fact]
    public void Sort_by_label_follows_the_formatters_culture_not_the_machines()
    {
        // The same values, two formatters: Swedish puts ö after z, the invariant culture puts it next to o.
        // Neither depends on CultureInfo.CurrentCulture, so the test passes on a Swedish machine and on CI alike.
        Assert.Equal(["Malmö", "Zürich", "Örebro"], Labels(CultureInfo.GetCultureInfo("sv-SE")));
        Assert.Equal(["Malmö", "Örebro", "Zürich"], Labels(CultureInfo.InvariantCulture));
    }

}

public class ValueFacetAccessibilityTests : BunitContext
{
    [Fact]
    public void The_search_box_is_labelled_with_the_facet_name_and_no_matches_is_announced()
    {
        var cut = Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, Dashboard.Create(TestData.Orders(), b => b.ValueFacet(x => x.Status).Name("Order status").Searchable()));
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            parameters.AddContent<ValueFacet<Order>>(facet => facet.Add(f => f.Key, "Status"));
        });

        Assert.Equal("Order status", cut.Find(".l2d-facet-search").GetAttribute("aria-label"));

        cut.Find(".l2d-facet-search").Input("zzz");
        Assert.Equal("status", cut.Find(".l2d-facet-empty").GetAttribute("role"));
    }
}
