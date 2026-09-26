using System.Globalization;
using System.Linq.Expressions;
using AngleSharp.Dom;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Linq2Dashboard.Blazor.Tests;

/// <summary>Facets defined in markup by the components inside a view with <c>Items</c> (concept §7, design §9).</summary>
public class MarkupDefinitionTests : BunitContext
{
    private int builds;

    private IRenderedComponent<DashboardView<Order>> RenderItems(
        RenderFragment content,
        Action<DashboardBuilder<Order>>? build = null,
        Selections? selections = null,
        bool syncUrl = false,
        Action<DashboardState<Order>>? onState = null) =>
        Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Items, TestData.Orders());
            parameters.Add(p => p.Build, b =>
            {
                builds++;
                build?.Invoke(b);
            });
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            if (selections is not null)
            {
                parameters.Add(p => p.Selections, selections);
            }

            if (syncUrl)
            {
                parameters.Add(p => p.SyncUrl, true);
            }

            if (onState is not null)
            {
                parameters.Add(p => p.StateChanged, onState);
            }

            parameters.AddContent<Order>(content);
        });

    private static RenderFragment Facet(Expression<Func<Order, object?>>? @for, params (string Name, object? Value)[] parameters) => b =>
    {
        b.OpenComponent<ValueFacet<Order>>(0);
        if (@for is not null)
        {
            b.AddAttribute(1, nameof(ValueFacet<Order>.For), @for);
        }

        int sequence = 2;
        foreach ((string name, object? value) in parameters)
        {
            b.AddAttribute(sequence++, name, value);
        }

        b.CloseComponent();
    };

    private static RenderFragment Wrap(RenderFragment inner) => b =>
    {
        b.OpenComponent<Wrapper>(0);
        b.AddAttribute(1, nameof(Wrapper.ChildContent), inner);
        b.CloseComponent();
    };

    private static RenderFragment All(params RenderFragment[] parts) => b =>
    {
        for (int i = 0; i < parts.Length; i++)
        {
            b.OpenRegion(i);
            parts[i](b);
            b.CloseRegion();
        }
    };

    private static IReadOnlyList<string> Labels(IRenderedComponent<DashboardView<Order>> cut, string key) =>
        cut.FindAll($"section[data-key='{key}'] li.l2d-facet-value").Select(li => li.QuerySelector(".l2d-facet-value-label")!.TextContent.Trim()).ToList();

    private static string Count(IRenderedComponent<DashboardView<Order>> cut, string key, string label) =>
        cut.FindAll($"section[data-key='{key}'] li.l2d-facet-value")
            .Single(li => li.QuerySelector(".l2d-facet-value-label")!.TextContent.Trim() == label)
            .QuerySelector(".l2d-facet-value-count")!.TextContent.Trim();

    private static IElement ValueButton(IRenderedComponent<DashboardView<Order>> cut, string key, string label) =>
        cut.FindAll($"section[data-key='{key}'] button.l2d-facet-value-button")
            .Single(b => b.QuerySelector(".l2d-facet-value-label")!.TextContent.Trim() == label);

    [Fact]
    public void A_ValueFacet_with_For_defines_its_facet_under_an_Items_view()
    {
        var cut = RenderItems(Facet(x => x.Country));

        Assert.Equal(["SE", "NO", "(none)", "DK"], Labels(cut, "Country"));
        Assert.Equal("3", Count(cut, "Country", "SE"));
        Assert.Equal(FacetKind.Value, cut.Instance.Context.Dashboard.Facets.Single().Kind);
    }

    [Fact]
    public void Definition_parameters_reach_the_builder()
    {
        var cut = RenderItems(Facet(x => x.Country, ("Top", 1), ("Name", "Land")));

        Assert.Equal(["SE"], Labels(cut, "Country"));
        Assert.Contains("5", cut.Find(".l2d-facet-other .l2d-facet-value-count").TextContent);
        Assert.Equal("Land", cut.Instance.Context.Dashboard.Facets.Single().Name);
    }

    [Fact]
    public void Nested_definitions_are_built_once_per_first_render()
    {
        var cut = RenderItems(All(
            Facet(x => x.Country),
            Wrap(All(Facet(x => x.Status), Wrap(Facet(x => x.Kind))))));

        Assert.Equal(["Country", "Status", "Kind"], cut.Instance.Context.Dashboard.Facets.Select(f => f.Key));
        Assert.NotEmpty(Labels(cut, "Kind"));
        Assert.Equal(2, builds);   // Build alone, then once with every definition
    }

    [Fact]
    public void A_boolean_member_defines_a_boolean_facet_and_a_collection_with_Multiple_a_multi_valued_one()
    {
        var cut = RenderItems(All(Facet(x => x.IsActive), Facet(x => x.Tags, ("Multiple", true))));

        Assert.Equal(FacetKind.Boolean, cut.Instance.Context.Dashboard.Facets.Single(f => f.Key == "IsActive").Kind);
        Assert.Equal(FacetKind.MultiValue, cut.Instance.Context.Dashboard.Facets.Single(f => f.Key == "Tags").Kind);
        Assert.Contains("l2d-multi-value-facet", cut.Find("section[data-key='Tags']").ClassName);
    }

    [Fact]
    public void A_collection_without_Multiple_fails_clearly()
    {
        var error = Assert.Throws<InvalidOperationException>(() => RenderItems(Facet(x => x.Tags)));

        Assert.Contains("Multiple", error.Message);
    }

    [Fact]
    public void Key_and_For_together_define_a_facet_with_an_explicit_key()
    {
        var cut = RenderItems(Facet(x => x.OrderDate.Month, ("Key", "month")));

        Assert.Equal("month", cut.Instance.Context.Dashboard.Facets.Single().Key);
        Assert.Equal("3", Count(cut, "month", "3"));
    }

    [Fact]
    public void Define_reaches_the_typed_builder()
    {
        var cut = RenderItems(Facet(x => x.Country, ("Define", (Action<ValueFacetBuilder<Order, string?>>)(f => f.Comparer(StringComparer.Ordinal)))));

        Assert.Equal("2", Count(cut, "Country", "SE"));
        Assert.Equal("1", Count(cut, "Country", "se"));
    }

    [Fact]
    public void Define_with_the_wrong_builder_type_fails_clearly()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            RenderItems(Facet(x => x.Country, ("Define", (Action<ValueFacetBuilder<Order, int>>)(_ => { })))));

        Assert.Contains("ValueFacetBuilder<Order, String>", error.Message);
    }

    [Fact]
    public void A_selector_alone_displays_a_facet_that_Build_defines()
    {
        var cut = RenderItems(Facet(x => x.Country), build: b => b.ValueFacet(x => x.Country).Top(1));

        Assert.Equal(["SE"], Labels(cut, "Country"));
        Assert.Equal(1, builds);
    }

    [Fact]
    public void Definition_parameters_on_a_facet_Build_defines_fail_clearly()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            RenderItems(Facet(x => x.Country, ("Top", 2)), build: b => b.ValueFacet(x => x.Country)));

        Assert.Contains("Build already defines", error.Message);
    }

    [Fact]
    public void Two_components_defining_the_same_facet_fail_clearly()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            RenderItems(All(Facet(x => x.Country, ("Top", 1)), Facet(x => x.Country, ("Top", 2)))));

        Assert.Contains("both define facet 'Country'", error.Message);
    }

    [Fact]
    public void A_component_with_definition_parameters_takes_over_from_one_that_only_names_the_selector()
    {
        var cut = RenderItems(All(Facet(x => x.Country), Facet(x => x.Country, ("Top", 1))));

        Assert.All(cut.FindAll("section[data-key='Country']"), section => Assert.Single(section.QuerySelectorAll("li.l2d-facet-value")));
    }

    [Fact]
    public void A_Key_named_before_the_component_that_defines_it_waits_for_the_definition()
    {
        var cut = RenderItems(All(Facet(null, ("Key", "Country")), Facet(x => x.Country, ("Top", 1))));

        Assert.Equal(2, cut.FindAll("section[data-key='Country']").Count);
    }

    [Fact]
    public void A_Key_nothing_defines_fails_once_the_view_has_settled()
    {
        var error = Assert.ThrowsAny<Exception>(() => RenderItems(Facet(null, ("Key", "Nowhere"))));

        Assert.Contains("Nowhere", error.Message);
    }

    [Fact]
    public void Definition_parameters_under_a_prebuilt_Dashboard_fail_clearly()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Render<DashboardView<Order>>(parameters => parameters
            .Add(p => p.Dashboard, Dashboard.Create(TestData.Orders(), b => b.ValueFacet(x => x.Country)))
            .AddContent<Order>(Facet(x => x.Country, ("Top", 1)))));

        Assert.Contains("need a DashboardView with Items", error.Message);
    }

    [Fact]
    public void Definition_parameters_without_For_fail_clearly()
    {
        var error = Assert.Throws<InvalidOperationException>(() => RenderItems(Facet(null, ("Key", "Country"), ("Top", 1))));

        Assert.Contains("no For", error.Message);
    }

    [Fact]
    public void Selections_for_a_facet_the_markup_defines_are_held_until_it_is_defined()
    {
        var cut = RenderItems(Facet(x => x.Country), selections: Selections.Empty.With("Country", ValueSelection.Of("SE")));

        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("SE")), cut.Instance.Context.Selections);
        Assert.Equal(3, cut.Instance.Context.State.MatchingCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_URL_applies_to_a_facet_the_markup_defines(bool interactive)
    {
        SetRendererInfo(new RendererInfo(interactive ? "Server" : "Static", interactive));
        Services.GetRequiredService<NavigationManager>().NavigateTo("page?Country=NO");

        var cut = RenderItems(Facet(x => x.Country), syncUrl: true);

        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("NO")), cut.Instance.Context.Selections);
        Assert.Contains("l2d-selected", cut.FindAll("section[data-key='Country'] li.l2d-facet-value")[1].ClassName);
        Assert.Equal(interactive ? 0 : 4, cut.FindAll("section[data-key='Country'] a.l2d-facet-value-button").Count);
    }

    [Fact]
    public void The_host_hears_the_state_with_the_markup_facets()
    {
        DashboardState<Order>? last = null;

        RenderItems(Facet(x => x.Country), onState: s => last = s);

        Assert.NotNull(last);
        Assert.Equal("Country", Assert.Single(last.Facets).Key);
    }

    [Fact]
    public void A_changed_watched_value_rebuilds_and_new_code_alone_does_not()
    {
        int top = 1;
        Func<Order, string?> label = x => x.Country;
        var cut = RenderItems(b => Facet(x => x.Country, ("Top", top), ("Label", label))(b));
        Assert.Equal(2, builds);

        label = x => x.Country + "!";
        cut.Render();
        Assert.Equal(2, builds);

        top = 2;
        cut.Render();
        Assert.Equal(3, builds);
        Assert.Equal(2, Labels(cut, "Country").Count);
    }

    [Fact]
    public void A_component_that_appears_later_builds_once_and_keeps_the_selections()
    {
        bool late = false;
        var cut = RenderItems(b => All(Facet(x => x.Country), late ? Facet(x => x.Status) : _ => { })(b));
        ValueButton(cut, "Country", "SE").Click();

        late = true;
        cut.Render();

        Assert.Equal(3, builds);
        Assert.Equal(["Country", "Status"], cut.Instance.Context.Dashboard.Facets.Select(f => f.Key));
        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("SE")), cut.Instance.Context.Selections);
        Assert.NotEmpty(Labels(cut, "Status"));
    }

    private sealed class Wrapper : ComponentBase
    {
        [Parameter]
        public RenderFragment? ChildContent { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddContent(1, ChildContent);
            builder.CloseElement();
        }
    }
}
