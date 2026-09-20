using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

/// <summary>The <see cref="ValueFacet{T}"/> component over a multi-valued facet (concept §5, §6): the same list, never an "Other" row.</summary>
public class MultiValueFacetComponentTests : BunitContext
{
    // Tags per order: 1 urgent, gift; 2 gift; 3 none; 4 Urgent, urgent, b2b; 5 empty; 6 b2b; 7 only null; 8 gift, b2b
    private static Dashboard<Order> BuildDashboard() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.MultiValueFacet(x => x.Tags);
            b.MultiValueFacet("top", x => x.Tags).Top(2);
        });

    private IRenderedComponent<DashboardView<Order>> RenderFacet(string key, Action<Selections>? onChanged = null) =>
        Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, BuildDashboard());
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            if (onChanged is not null)
            {
                parameters.Add(p => p.SelectionsChanged, onChanged);
            }

            parameters.AddContent<ValueFacet<Order>>(facet => facet.Add(f => f.Key, key));
        });

    private static IReadOnlyList<IElement> Items(IRenderedComponent<DashboardView<Order>> cut) => cut.FindAll("li.l2d-facet-value");

    private static string Label(IElement item) => item.QuerySelector(".l2d-facet-value-label")!.TextContent.Trim();

    private static string Count(IElement item) => item.QuerySelector(".l2d-facet-value-count")!.TextContent.Trim();

    [Fact]
    public void Renders_every_value_with_overlapping_counts_and_marks_the_root()
    {
        var cut = RenderFacet("Tags");

        var items = Items(cut);
        Assert.Equal(["gift", "b2b", "(none)", "urgent"], items.Select(Label));
        Assert.Equal(["3", "3", "3", "2"], items.Select(Count)); // 11 over 8 rows
        Assert.Contains("l2d-multi-value-facet", cut.Find("section.l2d-value-facet").ClassName);
        Assert.DoesNotContain("l2d-multi-value-facet", RenderFacet("Country").Find("section.l2d-value-facet").ClassName);
    }

    [Fact]
    public void Top_n_renders_no_other_row()
    {
        var cut = RenderFacet("top");

        Assert.Equal(["gift", "b2b"], Items(cut).Select(Label));
        Assert.Empty(cut.FindAll("li.l2d-facet-other"));
    }

    [Fact]
    public void A_click_toggles_the_value_and_the_facets_own_counts_stay()
    {
        Selections? changed = null;
        var cut = RenderFacet("Tags", s => changed = s);

        cut.FindAll("li.l2d-facet-value button")[0].Click();

        Assert.Equal(Selections.Empty.Toggle("Tags", "gift"), changed);
        var items = Items(cut);
        Assert.Contains("l2d-selected", items[0].ClassName);
        Assert.Equal(["3", "3", "3", "2"], items.Select(Count));
    }
}
