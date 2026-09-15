using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

public class TextFacetTests : BunitContext
{
    // Status: Open, Open, Closed, Closed, Pending, Open, Pending, Open; Country: SE, NO, null, SE, DK, se, null, NO
    private static Dashboard<Order> BuildDashboard() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.TextFacet("search", (order, text) =>
                order.Status.Contains(text, StringComparison.OrdinalIgnoreCase)
                || (order.Country?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false))
             .Name("Find");
        });

    private IRenderedComponent<DashboardView<Order>> RenderFacet(
        Selections? selections = null, Action<Selections>? onChanged = null, Action<ComponentParameterCollectionBuilder<TextFacet<Order>>>? configure = null,
        bool withChips = false, string key = "search", int debounce = 0) =>
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

            parameters.AddChildContent<TextFacet<Order>>(facet =>
            {
                facet.Add(f => f.Key, key);
                facet.Add(f => f.DebounceMilliseconds, debounce);
                configure?.Invoke(facet);
            });

            if (withChips)
            {
                parameters.AddChildContent<ActiveSelections<Order>>();
            }
        });

    private static IElement Input(IRenderedComponent<DashboardView<Order>> cut) => cut.Find("input.l2d-text-input");

    private static Selections Text(string text) => Selections.Empty.With("search", new TextSelection(text));

    [Fact]
    public void Renders_name_input_and_the_context_count()
    {
        var cut = RenderFacet();

        Assert.Equal("Find", cut.Find(".l2d-facet-title").TextContent);
        Assert.Equal("Type to filter", Input(cut).GetAttribute("placeholder"));
        Assert.Equal("Find", Input(cut).GetAttribute("aria-label"));
        Assert.Equal("", Input(cut).GetAttribute("value") ?? "");
        Assert.Equal("Among 8 rows", cut.Find(".l2d-text-context").TextContent);
        Assert.Equal("search", cut.Find(".l2d-text-facet").GetAttribute("data-key"));
        Assert.Empty(cut.FindAll(".l2d-facet-clear"));
    }

    [Fact]
    public void Typing_applies_a_text_selection_and_the_clear_link_appears()
    {
        Selections? raised = null;
        var cut = RenderFacet(onChanged: s => raised = s);

        Input(cut).Input("open");

        Assert.Equal(Text("open"), raised);
        Assert.Single(cut.FindAll(".l2d-facet-clear"));
        Assert.Equal("open", Input(cut).GetAttribute("value"));
    }

    [Fact]
    public void The_context_count_follows_the_other_facets_not_its_own_text()
    {
        var cut = RenderFacet(Text("open").Toggle("Country", "SE"));

        Assert.Equal("Among 3 rows", cut.Find(".l2d-text-context").TextContent);
        Assert.Equal("open", Input(cut).GetAttribute("value"));
    }

    [Fact]
    public void Blank_text_clears_the_facet()
    {
        Selections? raised = null;
        var cut = RenderFacet(Text("open"), s => raised = s);

        Input(cut).Input("   ");

        Assert.Equal(Selections.Empty, raised);
        Assert.Empty(cut.FindAll(".l2d-facet-clear"));
    }

    [Fact]
    public void The_clear_link_clears_the_facet_and_empties_the_input()
    {
        Selections? raised = null;
        var cut = RenderFacet(Text("open"), s => raised = s);

        cut.Find(".l2d-facet-clear").Click();

        Assert.Equal(Selections.Empty, raised);
        Assert.Equal("", Input(cut).GetAttribute("value") ?? "");
    }

    [Fact]
    public void With_a_debounce_the_text_is_applied_after_the_pause_or_on_enter()
    {
        var raised = new List<Selections>();
        var cut = RenderFacet(onChanged: raised.Add, debounce: 100);

        Input(cut).Input("op");
        Input(cut).Input("ope");
        Assert.Empty(raised);

        cut.WaitForAssertion(() => Assert.Equal([Text("ope")], raised), TimeSpan.FromSeconds(5));

        Input(cut).Input("open");
        Input(cut).KeyDown("Enter");
        Assert.Equal([Text("ope"), Text("open")], raised);
    }

    [Fact]
    public void Pending_text_is_dropped_when_the_facet_is_cleared_before_the_pause_ends()
    {
        var raised = new List<Selections>();
        var cut = RenderFacet(Text("open"), raised.Add, debounce: 100);

        Input(cut).Input("openx");
        cut.Find(".l2d-facet-clear").Click();

        Assert.Equal([Selections.Empty], raised);
        Thread.Sleep(250);
        Assert.Equal([Selections.Empty], raised);
    }

    [Fact]
    public void The_input_follows_selections_changed_elsewhere()
    {
        var cut = RenderFacet(Text("open"), withChips: true);
        Assert.Equal("open", Input(cut).GetAttribute("value"));

        cut.Find(".l2d-chip-remove").Click();
        Assert.Equal("", Input(cut).GetAttribute("value") ?? "");

        cut.Render(parameters => parameters.Add(p => p.Selections, Text("closed")));
        Assert.Equal("closed", Input(cut).GetAttribute("value"));
    }

    [Fact]
    public void The_chip_shows_the_text_and_removing_it_clears_the_facet()
    {
        Selections? raised = null;
        var cut = RenderFacet(Text("open").Toggle("Country", "SE"), s => raised = s, withChips: true);

        var chip = cut.FindAll("li.l2d-chip").Single(c => c.GetAttribute("data-key") == "search");
        Assert.Equal("Find", chip.QuerySelector(".l2d-chip-facet")!.TextContent);
        Assert.Equal("open", chip.QuerySelector(".l2d-chip-label")!.TextContent);

        chip.QuerySelector(".l2d-chip-remove")!.Click();

        Assert.Equal(Selections.Empty.Toggle("Country", "SE"), raised);
    }

    [Fact]
    public void Collapsing_hides_the_input_and_keeps_the_header()
    {
        var cut = RenderFacet(Text("open"));

        cut.Find(".l2d-facet-toggle").Click();

        Assert.Empty(cut.FindAll("input.l2d-text-input"));
        Assert.Contains("l2d-collapsed", cut.Find(".l2d-text-facet").ClassName);
        Assert.Single(cut.FindAll(".l2d-facet-clear"));
    }

    [Fact]
    public void Texts_and_the_context_line_are_configurable()
    {
        var cut = RenderFacet(configure: f =>
        {
            f.Add(x => x.Placeholder, "Sök");
            f.Add(x => x.ContextText, "{0} rader");
        });
        Assert.Equal("Sök", Input(cut).GetAttribute("placeholder"));
        Assert.Equal("8 rader", cut.Find(".l2d-text-context").TextContent);

        var hidden = RenderFacet(configure: f => f.Add(x => x.ShowContextCount, false));
        Assert.Empty(hidden.FindAll(".l2d-text-context"));
    }

    [Fact]
    public void Takes_a_class_and_passes_attributes_to_its_root()
    {
        var cut = RenderFacet(configure: f =>
        {
            f.Add(x => x.Class, "host-class");
            f.AddUnmatched("id", "host-id");
        });

        IElement root = cut.Find("#host-id");
        Assert.Equal(["l2d-facet", "l2d-text-facet", "host-class"], root.ClassList);
    }

    [Fact]
    public void A_key_of_another_kind_is_an_error()
    {
        var ex = Assert.ThrowsAny<Exception>(() => RenderFacet(key: "Country"));

        Assert.Contains("not a text facet", ex.Message);
    }
}
