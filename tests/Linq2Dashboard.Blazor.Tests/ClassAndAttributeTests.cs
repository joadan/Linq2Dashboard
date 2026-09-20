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
    private static Dashboard<Order> BuildDashboard(bool searchable = false) =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            if (searchable)
            {
                b.ValueFacet(x => x.Status).Searchable();
                b.TextFacet("search", (x, text) => x.Status.Contains(text, StringComparison.OrdinalIgnoreCase));
            }

            b.RangeFacet(x => x.Discount);
            b.DateFacet(x => x.OrderDate).Granularity(DateGranularity.Month).TimeZone(TestData.Stockholm);
            b.SumMetric("revenue", x => x.Amount).Name("Revenue");
            b.OrderBy(x => x.Id);
        });

    private IRenderedComponent<DashboardView<Order>> RenderWith<TComponent>(Action<ComponentParameterCollectionBuilder<TComponent>> configure, Selections? selections = null, bool searchable = false)
        where TComponent : IComponent =>
        Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, BuildDashboard(searchable));
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            if (selections is not null)
            {
                parameters.Add(p => p.Selections, selections);
            }

            parameters.AddContent<TComponent>(configure);
        });

    private static void AddStyling<TComponent>(ComponentParameterCollectionBuilder<TComponent> component)
        where TComponent : DashboardComponentBase<Order>
    {
        component.Add(c => c.Class, "host-class");
        component.AddUnmatched("id", "host-id");
        component.AddUnmatched("style", "grid-area: side");
        component.AddUnmatched("data-host", "yes");
        component.AddUnmatched("title", "host tooltip");
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
        Assert.Equal("host tooltip", root.GetAttribute("title"));
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
            parameters.AddUnmatched("title", "host tooltip");
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
            facet.AddUnmatched("title", "host tooltip");
        });

        IElement root = cut.Find(".l2d-value-facet");
        AssertStyled(root, "l2d-facet", "l2d-value-facet");
        Assert.Equal("Country", root.GetAttribute("data-key"));
    }

    /// <summary>
    /// <c>--l2d-facet-max-height</c> caps the value list alone (design §9): a per-facet value set on the root reaches the
    /// list by inheritance, while the header and the search box are siblings of the list, so they stay in place when it scrolls.
    /// </summary>
    [Fact]
    public void A_facet_max_height_on_the_root_reaches_the_value_list_but_not_the_header_or_search_box()
    {
        var cut = RenderWith<ValueFacet<Order>>(facet =>
        {
            facet.Add(f => f.Key, "Status");
            facet.AddUnmatched("style", "--l2d-facet-max-height: 8rem");
        }, searchable: true);

        IElement root = cut.Find(".l2d-value-facet");
        Assert.Equal("--l2d-facet-max-height: 8rem", root.GetAttribute("style"));

        IElement list = root.QuerySelector(".l2d-facet-values")!;
        Assert.NotEmpty(list.QuerySelectorAll(".l2d-facet-value"));
        Assert.Null(list.QuerySelector(".l2d-facet-header"));
        Assert.Null(list.QuerySelector(".l2d-facet-search"));
        Assert.Contains("l2d-facet", root.QuerySelector(".l2d-facet-header")!.ParentElement!.ClassList);
        Assert.Contains("l2d-facet", root.QuerySelector(".l2d-facet-search")!.ParentElement!.ClassList);
    }

    /// <summary>The bucket list of a range or date facet in list layout is capped the same way; the header stays outside it (design §9).</summary>
    [Fact]
    public void A_facet_max_height_on_the_root_reaches_the_bucket_list_but_not_the_header()
    {
        var cut = RenderWith<RangeFacet<Order>>(facet =>
        {
            facet.Add(f => f.Key, "Discount");
            facet.Add(f => f.Layout, BucketLayout.List);
            facet.AddUnmatched("style", "--l2d-facet-max-height: 8rem");
        });

        IElement root = cut.Find(".l2d-range-facet");
        Assert.Equal("--l2d-facet-max-height: 8rem", root.GetAttribute("style"));

        IElement list = root.QuerySelector(".l2d-buckets-list")!;
        Assert.NotEmpty(list.QuerySelectorAll(".l2d-bucket"));
        Assert.Null(list.QuerySelector(".l2d-facet-header"));
        Assert.Contains("l2d-facet", root.QuerySelector(".l2d-facet-header")!.ParentElement!.ClassList);
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
            facet.AddUnmatched("title", "host tooltip");
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
            facet.AddUnmatched("title", "host tooltip");
        });

        AssertStyled(cut.Find(".l2d-date-facet"), "l2d-facet", "l2d-date-facet");
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
            metric.AddUnmatched("title", "host tooltip");
        });

        IElement tile = cut.Find(".l2d-metric");
        AssertStyled(tile, "l2d-metric");
        Assert.Equal("revenue", tile.GetAttribute("data-key"));
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

    // InputClass replaces the library's default input skin (l2d-input) with the host's classes, so a CSS
    // framework's class such as form-control is not fought by the scoped stylesheet. The hook class stays.

    [Fact]
    public void TextFacet_input_has_the_hook_and_the_default_skin_without_an_InputClass()
    {
        var cut = RenderWith<TextFacet<Order>>(facet => facet.Add(f => f.Key, "search"), searchable: true);

        Assert.Equal("l2d-text-input l2d-input", cut.Find("input").GetAttribute("class"));
    }

    [Fact]
    public void TextFacet_InputClass_replaces_the_default_skin_and_keeps_the_hook()
    {
        var cut = RenderWith<TextFacet<Order>>(facet =>
        {
            facet.Add(f => f.Key, "search");
            facet.Add(f => f.InputClass, " form-control form-control-sm ");
        }, searchable: true);

        Assert.Equal("l2d-text-input form-control form-control-sm", cut.Find("input").GetAttribute("class"));
    }

    [Fact]
    public void ValueFacet_InputClass_styles_the_search_box()
    {
        var plain = RenderWith<ValueFacet<Order>>(facet => facet.Add(f => f.Key, "Status"), searchable: true);
        Assert.Equal("l2d-facet-search l2d-input", plain.Find("input").GetAttribute("class"));

        var styled = RenderWith<ValueFacet<Order>>(facet =>
        {
            facet.Add(f => f.Key, "Status");
            facet.Add(f => f.InputClass, "form-control");
        }, searchable: true);
        Assert.Equal("l2d-facet-search form-control", styled.Find("input").GetAttribute("class"));
    }

    [Fact]
    public void RangeFacet_InputClass_styles_the_slider_number_inputs_and_leaves_the_range_inputs_alone()
    {
        var plain = RenderWith<RangeFacet<Order>>(facet =>
        {
            facet.Add(f => f.Key, "Discount");
            facet.Add(f => f.ShowSlider, true);
        });
        Assert.Equal("l2d-slider-input-from l2d-input", plain.Find("input[type=number].l2d-slider-input-from").GetAttribute("class"));
        Assert.Equal("l2d-slider-input-to l2d-input", plain.Find("input[type=number].l2d-slider-input-to").GetAttribute("class"));

        var styled = RenderWith<RangeFacet<Order>>(facet =>
        {
            facet.Add(f => f.Key, "Discount");
            facet.Add(f => f.ShowSlider, true);
            facet.Add(f => f.InputClass, "form-control");
        });
        Assert.Equal("l2d-slider-input-from form-control", styled.Find("input[type=number].l2d-slider-input-from").GetAttribute("class"));
        Assert.Equal("l2d-slider-input-to form-control", styled.Find("input[type=number].l2d-slider-input-to").GetAttribute("class"));
        Assert.Equal("l2d-slider-from", styled.Find("input.l2d-slider-from").GetAttribute("class"));
        Assert.Equal("l2d-slider-to", styled.Find("input.l2d-slider-to").GetAttribute("class"));
    }
}
