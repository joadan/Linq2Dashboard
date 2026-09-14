using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

/// <summary>
/// Every public component takes a <c>Class</c> parameter and passes unmatched attributes through to its
/// root element, so a host can target one component from its own stylesheet (design §9).
/// </summary>
public class ClassAndAttributeTests : BunitContext
{
    private static Dashboard<Order> BuildDashboard() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.RangeFacet(x => x.Discount);
            b.DateFacet(x => x.OrderDate).TimeZone(TestData.Stockholm);
            b.Sum("revenue", x => x.Amount).Title("Revenue");
            b.OrderBy(x => x.Id);
        });

    private static readonly RenderFragment<Order> Row = order => builder =>
    {
        builder.OpenElement(0, "div");
        builder.AddContent(1, order.Id);
        builder.CloseElement();
    };

    private IRenderedComponent<DashboardView<Order>> RenderWith<TComponent>(Action<ComponentParameterCollectionBuilder<TComponent>> configure, Selections? selections = null)
        where TComponent : IComponent =>
        Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, BuildDashboard());
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            if (selections is not null)
            {
                parameters.Add(p => p.Selections, selections);
            }

            parameters.AddChildContent<TComponent>(configure);
        });

    private static void AddStyling<TComponent>(ComponentParameterCollectionBuilder<TComponent> component)
        where TComponent : DashboardComponentBase<Order>
    {
        component.Add(c => c.Class, "host-class");
        component.AddUnmatched("id", "host-id");
        component.AddUnmatched("style", "grid-area: side");
        component.AddUnmatched("data-host", "yes");
    }

    private static void AssertStyled(IElement root, params string[] libraryClasses)
    {
        foreach (string libraryClass in libraryClasses)
        {
            Assert.Contains(libraryClass, root.ClassList);
        }

        Assert.Contains("host-class", root.ClassList);
        Assert.Equal("host-id", root.Id);
        Assert.Equal("grid-area: side", root.GetAttribute("style"));
        Assert.Equal("yes", root.GetAttribute("data-host"));
    }

    [Fact]
    public void DashboardView_takes_a_class_and_passes_attributes_to_its_root()
    {
        var cut = Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, BuildDashboard());
            parameters.Add(p => p.Class, "host-class");
            parameters.AddUnmatched("id", "host-id");
            parameters.AddUnmatched("style", "grid-area: side");
            parameters.AddUnmatched("data-host", "yes");
        });

        AssertStyled(cut.Find("#host-id"), "l2d-dashboard");
        Assert.NotNull(cut.Find(".l2d-dashboard.host-class .l2d-summary"));
    }

    [Fact]
    public void ValueFacet_takes_a_class_and_passes_attributes_to_its_root()
    {
        var cut = RenderWith<ValueFacet<Order>>(facet =>
        {
            facet.Add(f => f.Key, "Country");
            facet.Add(f => f.Class, "host-class");
            facet.AddUnmatched("id", "host-id");
            facet.AddUnmatched("style", "grid-area: side");
            facet.AddUnmatched("data-host", "yes");
        });

        IElement root = cut.Find(".l2d-value-facet");
        AssertStyled(root, "l2d-facet", "l2d-value-facet");
        Assert.Equal("Country", root.GetAttribute("data-key"));
    }

    [Fact]
    public void RangeFacet_keeps_its_layout_and_collapsed_classes_beside_the_host_class()
    {
        var cut = RenderWith<RangeFacet<Order>>(facet =>
        {
            facet.Add(f => f.Key, "Discount");
            facet.Add(f => f.Collapsed, true);
            facet.Add(f => f.Class, "host-class");
            facet.AddUnmatched("id", "host-id");
            facet.AddUnmatched("style", "grid-area: side");
            facet.AddUnmatched("data-host", "yes");
        });

        AssertStyled(cut.Find(".l2d-range-facet"), "l2d-facet", "l2d-range-facet", "l2d-range-histogram", "l2d-collapsed");
    }

    [Fact]
    public void DateFacet_takes_a_class_and_passes_attributes_to_its_root()
    {
        var cut = RenderWith<DateFacet<Order>>(facet =>
        {
            facet.Add(f => f.Key, "OrderDate");
            facet.Add(f => f.Class, "host-class");
            facet.AddUnmatched("id", "host-id");
            facet.AddUnmatched("style", "grid-area: side");
            facet.AddUnmatched("data-host", "yes");
        });

        AssertStyled(cut.Find(".l2d-date-facet"), "l2d-facet", "l2d-date-facet");
    }

    [Fact]
    public void Results_takes_a_class_and_passes_attributes_to_its_root()
    {
        var cut = RenderWith<Results<Order>>(results =>
        {
            results.Add(r => r.RowTemplate, Row);
            results.Add(r => r.Class, "host-class");
            results.AddUnmatched("id", "host-id");
            results.AddUnmatched("style", "grid-area: side");
            results.AddUnmatched("data-host", "yes");
        });

        AssertStyled(cut.Find(".l2d-results"), "l2d-results");
    }

    [Fact]
    public void ActiveSelections_styles_its_root_whether_empty_or_not()
    {
        var empty = RenderWith<ActiveSelections<Order>>(active =>
        {
            active.Add(a => a.HideWhenEmpty, false);
            AddStyling(active);
        });
        AssertStyled(empty.Find(".l2d-active"), "l2d-active", "l2d-active-empty");

        var withChips = RenderWith<ActiveSelections<Order>>(AddStyling, Selections.Empty.With("Country", ValueSelection.Of("SE")));
        IElement root = withChips.Find(".l2d-active");
        AssertStyled(root, "l2d-active");
        Assert.DoesNotContain("l2d-active-empty", root.ClassList);
        Assert.NotEmpty(root.QuerySelectorAll(".l2d-chip"));
    }

    [Fact]
    public void Metric_passes_its_class_and_attributes_through_to_the_tile()
    {
        var cut = RenderWith<Metric<Order>>(metric =>
        {
            metric.Add(m => m.Key, "revenue");
            metric.Add(m => m.Class, "host-class");
            metric.AddUnmatched("id", "host-id");
            metric.AddUnmatched("style", "grid-area: side");
            metric.AddUnmatched("data-host", "yes");
        });

        IElement tile = cut.Find(".l2d-metric");
        AssertStyled(tile, "l2d-metric");
        Assert.Equal("revenue", tile.GetAttribute("data-key"));
    }

    [Fact]
    public void MatchingCount_keeps_its_own_class_beside_the_host_class()
    {
        var cut = RenderWith<MatchingCount<Order>>(AddStyling);

        AssertStyled(cut.Find(".l2d-metric"), "l2d-metric", "l2d-metric-matching");
    }

    [Fact]
    public void StateSummary_takes_a_class_and_passes_attributes_to_its_root()
    {
        var cut = RenderWith<StateSummary<Order>>(AddStyling);

        AssertStyled(cut.Find(".l2d-summary"), "l2d-summary");
    }

    [Fact]
    public void A_lower_case_class_attribute_binds_to_the_Class_parameter_and_never_replaces_the_library_classes()
    {
        // Parameter names match case-insensitively, so class="..." on the component is the Class parameter.
        var cut = RenderWith<ValueFacet<Order>>(facet =>
        {
            facet.Add(f => f.Key, "Country");
            facet.AddUnmatched("class", "host-class");
        });

        Assert.Equal("l2d-facet l2d-value-facet host-class", cut.Find("section").GetAttribute("class"));
    }

    [Fact]
    public void A_data_key_attribute_cannot_override_the_library_key()
    {
        var cut = RenderWith<Metric<Order>>(metric =>
        {
            metric.Add(m => m.Key, "revenue");
            metric.AddUnmatched("data-key", "other");
        });

        Assert.Equal("revenue", cut.Find(".l2d-metric").GetAttribute("data-key"));
    }

    [Fact]
    public void Without_a_class_the_root_has_only_the_library_classes()
    {
        var cut = RenderWith<ValueFacet<Order>>(facet => facet.Add(f => f.Key, "Country"));

        Assert.Equal("l2d-facet l2d-value-facet", cut.Find("section").GetAttribute("class"));
    }
}
