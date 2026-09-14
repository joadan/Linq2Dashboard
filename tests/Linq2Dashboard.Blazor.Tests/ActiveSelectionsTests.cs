using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

public class ActiveSelectionsTests : BunitContext
{
    private static Dashboard<Order> BuildDashboard() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.ValueFacet(x => x.Status).Title("Order status");
            b.ValueFacet("city", x => x.Country).Label(x => x.Address?.City);
            b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);
            b.DateFacet(x => x.OrderDate).Title("Ordered").TimeZone(TestData.Stockholm).Presets(DatePreset.ThisYear);
            b.UseTimeProvider(new FixedTimeProvider(TestData.Instant("2026-03-15T10:00:00Z")));
        });

    private IRenderedComponent<DashboardView<Order>> RenderChips(
        Selections selections, Action<Selections>? onChanged = null, Action<ComponentParameterCollectionBuilder<ActiveSelections<Order>>>? configure = null) =>
        Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, BuildDashboard());
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            parameters.Add(p => p.Selections, selections);
            if (onChanged is not null)
            {
                parameters.Add(p => p.SelectionsChanged, onChanged);
            }

            parameters.AddChildContent<ActiveSelections<Order>>(chips => configure?.Invoke(chips));
        });

    private static IReadOnlyList<IElement> Chips(IRenderedComponent<DashboardView<Order>> cut) => cut.FindAll("li.l2d-chip");

    private static string Facet(IElement chip) => chip.QuerySelector(".l2d-chip-facet")?.TextContent.Trim() ?? string.Empty;

    private static IEnumerable<string> Labels(IElement chip) => chip.QuerySelectorAll(".l2d-chip-label").Select(l => l.TextContent.Trim());

    private static string Label(IElement chip) => Labels(chip).Single();

    [Fact]
    public void Nothing_is_rendered_without_selections_unless_asked()
    {
        Assert.Empty(RenderChips(Selections.Empty).FindAll(".l2d-active"));

        var shown = RenderChips(Selections.Empty, configure: c => c.Add(x => x.HideWhenEmpty, false));
        Assert.Equal("No selections", shown.Find(".l2d-active-empty").TextContent);
    }

    [Fact]
    public void Values_of_one_facet_are_grouped_in_one_chip_in_facet_order()
    {
        var cut = RenderChips(Selections.Empty
            .With("Status", ValueSelection.Of("Open"))
            .With("Country", ValueSelection.Of("SE", null)));

        var chips = Chips(cut);
        Assert.Equal(["Country", "Order status"], chips.Select(Facet));
        Assert.Equal(["SE", "(none)"], Labels(chips[0]));
        Assert.Equal(["Open"], Labels(chips[1]));
        Assert.Contains("l2d-chip-group", chips[0].ClassName);
        Assert.Equal(", ", chips[0].QuerySelector(".l2d-chip-separator")!.TextContent);
        Assert.Empty(chips[1].QuerySelectorAll(".l2d-chip-separator"));
        Assert.Single(cut.FindAll(".l2d-active-clear-all"));
    }

    [Fact]
    public void Removing_one_value_inside_a_grouped_chip_toggles_only_that_value()
    {
        Selections? raised = null;
        var cut = RenderChips(Selections.Empty.With("Country", ValueSelection.Of("SE", "NO")), s => raised = s);

        var chip = Chips(cut).Single();
        chip.QuerySelectorAll(".l2d-chip-value").Single(v => v.QuerySelector(".l2d-chip-label")!.TextContent == "SE")
            .QuerySelector(".l2d-chip-value-remove")!.Click();

        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("NO")), raised);
        Assert.Equal(["NO"], Labels(Chips(cut).Single()));
    }

    [Fact]
    public void Removing_a_grouped_chip_clears_the_whole_facet()
    {
        Selections? raised = null;
        var cut = RenderChips(Selections.Empty.With("Country", ValueSelection.Of("SE", "NO")).With("Status", ValueSelection.Of("Open")), s => raised = s);

        Chips(cut)[0].QuerySelector(".l2d-chip-remove")!.Click();

        Assert.Equal(Selections.Empty.With("Status", ValueSelection.Of("Open")), raised);
        Assert.Single(Chips(cut));
    }

    [Fact]
    public void Removing_the_null_token_deselects_null()
    {
        Selections? raised = null;
        var cut = RenderChips(Selections.Empty.With("Country", ValueSelection.Of("SE", null)), s => raised = s);

        Chips(cut).Single().QuerySelectorAll(".l2d-chip-value").Single(v => v.QuerySelector(".l2d-chip-label")!.TextContent == "(none)")
            .QuerySelector(".l2d-chip-value-remove")!.Click();

        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("SE")), raised);
    }

    [Fact]
    public void The_separator_is_configurable()
    {
        var cut = RenderChips(Selections.Empty.With("Country", ValueSelection.Of("SE", "NO", "DK")), configure: c => c.Add(x => x.ValueSeparator, " or "));

        var separators = Chips(cut).Single().QuerySelectorAll(".l2d-chip-separator");
        Assert.Equal(2, separators.Length);
        Assert.All(separators, s => Assert.Equal(" or ", s.TextContent));
    }

    [Fact]
    public void Grouping_can_be_turned_off_for_one_chip_per_value()
    {
        Selections? raised = null;
        var cut = RenderChips(Selections.Empty.With("Country", ValueSelection.Of("SE", "NO")), s => raised = s, c => c.Add(x => x.GroupValues, false));

        var chips = Chips(cut);
        Assert.Equal(2, chips.Count);
        Assert.Equal(["SE", "NO"], chips.Select(Label));
        Assert.DoesNotContain(chips, c => c.ClassList.Contains("l2d-chip-group"));

        chips[0].QuerySelector(".l2d-chip-remove")!.Click();

        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("NO")), raised);
    }

    [Fact]
    public void A_range_selection_matching_a_bucket_uses_the_bucket_label_and_removal_clears_the_facet()
    {
        Selections? raised = null;
        var cut = RenderChips(Selections.Empty.With("Amount", new RangeSelection(100, 500, toInclusive: false)), s => raised = s);

        var chip = Chips(cut).Single();
        Assert.Equal("Amount", Facet(chip));
        Assert.Equal("100 – 500", Label(chip));
        Assert.DoesNotContain("l2d-chip-group", chip.ClassName);

        chip.QuerySelector(".l2d-chip-remove")!.Click();
        Assert.Equal(Selections.Empty, raised);
    }

    [Fact]
    public void Other_intervals_and_only_null_use_the_formatter()
    {
        Assert.Equal("≥ 250", Label(Chips(RenderChips(Selections.Empty.With("Amount", RangeSelection.AtLeast(250)))).Single()));
        Assert.Equal("250 – 750", Label(Chips(RenderChips(Selections.Empty.With("Amount", RangeSelection.Between(250, 750)))).Single()));
        Assert.Equal("< 500 or (none)", Label(Chips(RenderChips(Selections.Empty.With("Amount", new RangeSelection(null, 500, toInclusive: false, includeNull: true)))).Single()));
        Assert.Equal("(none)", Label(Chips(RenderChips(Selections.Empty.With("Amount", RangeSelection.OnlyNull))).Single()));
    }

    [Fact]
    public void Date_chips_show_preset_bucket_or_interval_text()
    {
        var march = (DateFacetState)BuildDashboard().Calculate().Facet("OrderDate");
        DateBucket marchBucket = march.Buckets.Single(b => b.PeriodStart == new DateTime(2026, 3, 1));

        Assert.Equal("This year", Label(Chips(RenderChips(Selections.Empty.With("OrderDate", DateSelection.Relative(DatePreset.ThisYear)))).Single()));
        Assert.Equal("Mar 2026", Label(Chips(RenderChips(Selections.Empty.With("OrderDate", marchBucket.ToSelection()))).Single()));
        Assert.Equal("(none)", Label(Chips(RenderChips(Selections.Empty.With("OrderDate", DateSelection.OnlyNull))).Single()));

        var interval = DateSelection.Between(TestData.Instant("2026-02-10T00:00:00+01:00"), null);
        var chip = Chips(RenderChips(Selections.Empty.With("OrderDate", interval))).Single();
        Assert.Equal("Ordered", Facet(chip));
        Assert.Equal("from 02/10/2026", Label(chip));
    }

    [Fact]
    public void Clear_all_empties_the_selections()
    {
        Selections? raised = null;
        var cut = RenderChips(Selections.Empty.With("Country", ValueSelection.Of("SE")).With("Amount", RangeSelection.AtLeast(1)), s => raised = s);

        cut.Find(".l2d-active-clear-all").Click();

        Assert.Equal(Selections.Empty, raised);
        Assert.Empty(cut.FindAll(".l2d-active"));
    }

    [Fact]
    public void Facet_titles_can_be_hidden()
    {
        var cut = RenderChips(Selections.Empty.With("Country", ValueSelection.Of("SE")), configure: c => c.Add(x => x.ShowFacetTitle, false));

        Assert.Empty(cut.FindAll(".l2d-chip-facet"));
        Assert.Equal("SE", Label(Chips(cut).Single()));
    }

    [Fact]
    public void Chips_show_the_facets_label_for_a_value()
    {
        var cut = RenderChips(Selections.Empty.With("city", ValueSelection.Of("se", "DK")));

        Assert.Equal(["Stockholm", "Copenhagen"], Labels(Chips(cut).Single()));
    }
}
