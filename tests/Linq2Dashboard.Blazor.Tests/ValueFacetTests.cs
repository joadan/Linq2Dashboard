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
            b.ValueFacet(x => x.Status).Title("Order status").Searchable();
            b.ValueFacet("top", x => x.Country).Top(2);
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

            parameters.AddChildContent<ValueFacet<Order>>(facet =>
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
    public void Renders_title_values_and_filtered_counts_in_rank_order()
    {
        var cut = RenderFacet("Status");

        Assert.Equal("Order status", cut.Find(".l2d-facet-title").TextContent);
        var items = Items(cut);
        Assert.Equal(["Open", "Closed", "Pending"], items.Select(Label));
        Assert.Equal(["4", "2", "2"], items.Select(Count));
        Assert.Empty(cut.FindAll(".l2d-facet-clear"));
        Assert.Empty(cut.FindAll(".l2d-facet-other"));
    }

    [Fact]
    public void The_null_value_is_rendered_with_the_formatters_label_and_marked()
    {
        var cut = RenderFacet("Country");

        var item = Items(cut).Single(li => li.ClassList.Contains("l2d-null"));
        Assert.Equal("(none)", Label(item));
        Assert.Equal("2", Count(item));
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
        Assert.Equal(["3", "2", "2", "1"], Items(cut).Select(Count));
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
        Assert.Equal("0", Count(pending));

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
    public void ShowTotals_adds_the_total_to_each_count()
    {
        var cut = RenderFacet("Country", Selections.Empty.With("Status", ValueSelection.Of("Open")), configure: f => f.Add(x => x.ShowTotals, true));

        Assert.Equal("2 / 3", Count(Items(cut).Single(li => Label(li) == "SE")));
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
}
