using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

public class RangeSliderTests : BunitContext
{
    // Amount: 100, 250, 500, 999.5, 1000, 2500, 0, 75 → min 0, max 2500, default step 10.
    private static Dashboard<Order> BuildDashboard() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);
        });

    private IRenderedComponent<DashboardView<Order>> RenderFacet(
        Selections? selections = null, Action<Selections>? onChanged = null, Action<ComponentParameterCollectionBuilder<RangeFacet<Order>>>? configure = null) =>
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

            parameters.AddContent<RangeFacet<Order>>(facet =>
            {
                facet.Add(f => f.Key, "Amount");
                facet.Add(f => f.ShowSlider, true);
                configure?.Invoke(facet);
            });
        });

    private static (string From, string To) Handles(IRenderedComponent<DashboardView<Order>> cut) =>
        (cut.Find("input.l2d-slider-from").GetAttribute("value")!, cut.Find("input.l2d-slider-to").GetAttribute("value")!);

    private static bool[] SelectedBuckets(IRenderedComponent<DashboardView<Order>> cut) =>
        cut.FindAll("li.l2d-bucket").Select(b => b.ClassList.Contains("l2d-selected")).ToArray();

    [Fact]
    public void Slider_is_opt_in()
    {
        var without = Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, BuildDashboard());
            parameters.AddContent<RangeFacet<Order>>(f => f.Add(x => x.Key, "Amount"));
        });

        Assert.Empty(without.FindAll(".l2d-slider"));
        Assert.Single(RenderFacet().FindAll(".l2d-slider"));
    }

    [Fact]
    public void Handles_start_at_the_dataset_bounds_with_a_sensible_step()
    {
        var cut = RenderFacet();

        Assert.Equal(("0", "2500"), Handles(cut));
        var from = cut.Find("input.l2d-slider-from");
        Assert.Equal("0", from.GetAttribute("min"));
        Assert.Equal("2500", from.GetAttribute("max"));
        Assert.Equal("10", from.GetAttribute("step"));
        Assert.Equal("--l2d-slider-from: 0%; --l2d-slider-to: 100%;", cut.Find(".l2d-slider").GetAttribute("style"));
    }

    [Theory]
    [InlineData(0, 2500, 10)]
    [InlineData(0, 100, 1)]
    [InlineData(0, 99, 0.1)]
    [InlineData(0, 12345, 100)]
    [InlineData(0, 0.5, 0.01)]
    [InlineData(5, 5, 1)]
    public void Default_step_is_a_power_of_ten_near_a_hundredth_of_the_range(double min, double max, double expected)
    {
        Assert.Equal(expected, RangeSlider.DefaultStep(min, max));
    }

    [Fact]
    public void Releasing_a_handle_applies_an_inclusive_interval_unbounded_where_a_handle_rests_at_its_end()
    {
        Selections? raised = null;
        var cut = RenderFacet(onChanged: s => raised = s);

        cut.Find("input.l2d-slider-to").Change("500");

        Assert.Equal(Selections.Empty.With("Amount", RangeSelection.AtMost(500)), raised);
        Assert.Equal(("0", "500"), Handles(cut));
        Assert.Equal("--l2d-slider-from: 0%; --l2d-slider-to: 20%;", cut.Find(".l2d-slider").GetAttribute("style"));

        cut.Find("input.l2d-slider-from").Change("100");
        Assert.Equal(Selections.Empty.With("Amount", RangeSelection.Between(100, 500)), raised);
    }

    [Fact]
    public void A_handle_at_its_end_lights_the_open_ended_bucket()
    {
        var cut = RenderFacet();

        // Buckets: below 100, 100–500, 500–1000, 1000 and above.
        cut.Find("input.l2d-slider-to").Change("500");
        Assert.Equal(new[] { true, true, false, false }, SelectedBuckets(cut));

        cut.Find("input.l2d-slider-to").Change("2500");
        cut.Find("input.l2d-slider-from").Change("1000");
        Assert.Equal(new[] { false, false, false, true }, SelectedBuckets(cut));
    }

    [Fact]
    public void The_upper_end_is_reached_within_one_step_because_the_grid_starts_at_the_minimum()
    {
        Selections? raised = null;
        var cut = RenderFacet(onChanged: s => raised = s, configure: f => f.Add(x => x.SliderStep, 300d));

        cut.Find("input.l2d-slider-to").Change("2100"); // 400 short of 2500: a real bound
        Assert.Equal(Selections.Empty.With("Amount", RangeSelection.AtMost(2100)), raised);

        cut.Find("input.l2d-slider-to").Change("2400"); // the last grid point below 2500: the end
        Assert.Equal(Selections.Empty, raised);

        Assert.True(RangeSlider.AtUpperEnd(19301.47, 19302.36, 100));
        Assert.False(RangeSlider.AtUpperEnd(19201.47, 19302.36, 100));
        Assert.True(RangeSlider.AtLowerEnd(1.47, 1.47));
        Assert.False(RangeSlider.AtLowerEnd(101.47, 1.47));
    }

    [Fact]
    public void Dragging_previews_without_applying()
    {
        int raisedCount = 0;
        var cut = RenderFacet(onChanged: _ => raisedCount++);

        cut.Find("input.l2d-slider-to").Input("800");

        Assert.Equal(0, raisedCount);
        Assert.Equal(("0", "800"), Handles(cut));
    }

    /// <summary>Each handle stays where the browser put it, since Blazor cannot push a clamped value back into the DOM; the interval runs from the lower one to the upper one.</summary>
    [Fact]
    public void Handles_may_cross_and_the_lower_one_is_from()
    {
        Selections? raised = null;
        var cut = RenderFacet(Selections.Empty.With("Amount", RangeSelection.Between(100, 500)), s => raised = s);

        // While dragging, the first input is past the second: the picture, the inputs and the labels follow the order of the values.
        cut.Find("input.l2d-slider-from").Input("900");
        Assert.Equal(("900", "500"), Handles(cut));
        Assert.Null(raised);
        Assert.Equal("--l2d-slider-from: 20%; --l2d-slider-to: 36%;", cut.Find(".l2d-slider").GetAttribute("style"));
        Assert.Equal("500", cut.Find("input.l2d-slider-input-from").GetAttribute("value"));
        Assert.Equal("900", cut.Find("input.l2d-slider-input-to").GetAttribute("value"));
        Assert.Equal("To", cut.Find("input.l2d-slider-from").GetAttribute("aria-label"));
        Assert.Equal("From", cut.Find("input.l2d-slider-to").GetAttribute("aria-label"));

        // On release the ordered interval is applied; the host feeds it back and the handles take it in order again.
        cut.Find("input.l2d-slider-from").Change("900");
        Assert.Equal(Selections.Empty.With("Amount", RangeSelection.Between(500, 900)), raised);
        Assert.Equal(("500", "900"), Handles(cut));
        Assert.Equal("From", cut.Find("input.l2d-slider-from").GetAttribute("aria-label"));

        // A number above the other handle crosses them the same way.
        cut.Find("input.l2d-slider-input-from").Change("1200");
        Assert.Equal(Selections.Empty.With("Amount", RangeSelection.Between(900, 1200)), raised);
        Assert.Equal(("900", "1200"), Handles(cut));
        Assert.Equal("900", cut.Find("input.l2d-slider-input-from").GetAttribute("value"));
        Assert.Equal("1200", cut.Find("input.l2d-slider-input-to").GetAttribute("value"));
    }

    /// <summary>A host that does not feed the selection back leaves the handles crossed; the number inputs then address the lower and upper handle, whichever input holds them.</summary>
    [Fact]
    public void Number_inputs_address_the_lower_and_upper_handle_when_crossed()
    {
        var cut = Render<RangeSlider>(parameters =>
        {
            parameters.Add(p => p.Min, 0d);
            parameters.Add(p => p.Max, 2500d);
            parameters.Add(p => p.From, 100d);
            parameters.Add(p => p.To, 500d);
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
        });

        cut.Find("input.l2d-slider-from").Change("900");
        Assert.Equal("900", cut.Find("input.l2d-slider-from").GetAttribute("value"));
        Assert.Equal("500", cut.Find("input.l2d-slider-to").GetAttribute("value"));

        cut.Find("input.l2d-slider-input-from").Change("600");   // the lower handle is the second input
        Assert.Equal("900", cut.Find("input.l2d-slider-from").GetAttribute("value"));
        Assert.Equal("600", cut.Find("input.l2d-slider-to").GetAttribute("value"));

        cut.Find("input.l2d-slider-input-to").Change("1000");    // the upper handle is the first input
        Assert.Equal("1000", cut.Find("input.l2d-slider-from").GetAttribute("value"));
        Assert.Equal("600", cut.Find("input.l2d-slider-to").GetAttribute("value"));
        Assert.Equal("600", cut.Find("input.l2d-slider-input-from").GetAttribute("value"));
        Assert.Equal("1000", cut.Find("input.l2d-slider-input-to").GetAttribute("value"));
    }

    /// <summary>Two handles on the same spot are not crossed: the first input keeps the From label and the lower number input.</summary>
    [Fact]
    public void Meeting_handles_keep_their_roles()
    {
        var cut = RenderFacet(Selections.Empty.With("Amount", RangeSelection.Between(100, 500)));

        cut.Find("input.l2d-slider-from").Change("500");

        Assert.Equal(("500", "500"), Handles(cut));
        Assert.Equal("From", cut.Find("input.l2d-slider-from").GetAttribute("aria-label"));

        cut.Find("input.l2d-slider-input-from").Change("200");
        Assert.Equal(("200", "500"), Handles(cut));
    }

    [Fact]
    public void Full_range_clears_the_facet()
    {
        Selections? raised = null;
        var cut = RenderFacet(Selections.Empty.With("Amount", RangeSelection.Between(100, 500)), s => raised = s);

        cut.Find("input.l2d-slider-from").Change("0");
        Assert.Equal(Selections.Empty.With("Amount", RangeSelection.AtMost(500)), raised);

        cut.Find("input.l2d-slider-to").Change("2500");
        Assert.Equal(Selections.Empty, raised);
        Assert.Equal(("0", "2500"), Handles(cut));
    }

    /// <summary>The handles show one interval; a single bar click is one, a second bar makes a set the slider cannot show, so they rest at the ends.</summary>
    [Fact]
    public void Handles_follow_a_single_interval_and_rest_at_the_ends_for_several()
    {
        var cut = RenderFacet();
        Assert.Equal(("0", "2500"), Handles(cut));

        cut.FindAll("li.l2d-bucket button")[1].Click(); // 100 – 500
        Assert.Equal(("100", "500"), Handles(cut));

        cut.FindAll("li.l2d-bucket button")[3].Click(); // ≥ 1000 joins: two intervals
        Assert.Equal(("0", "2500"), Handles(cut));

        cut.Render(parameters => parameters.Add(p => p.Selections, Selections.Empty.With("Amount", RangeSelection.AtLeast(1000))));
        Assert.Equal(("1000", "2500"), Handles(cut));

        cut.Render(parameters => parameters.Add(p => p.Selections, Selections.Empty.With("Amount", RangeSelection.OnlyNull)));
        Assert.Equal(("0", "2500"), Handles(cut));
    }

    /// <summary>The slider's interval replaces the whole set, bars included: it is a single-interval editor.</summary>
    [Fact]
    public void Applying_the_slider_replaces_every_toggled_bar()
    {
        Selections? raised = null;
        var low = new RangeInterval(null, 100, toInclusive: false);
        var high = new RangeInterval(1000, null, toInclusive: false);
        var cut = RenderFacet(Selections.Empty.With("Amount", new RangeSelection([low, high], includeNull: true)), s => raised = s);

        cut.Find("input.l2d-slider-input-from").Change("250");

        Assert.Equal(Selections.Empty.With("Amount", RangeSelection.AtLeast(250)), raised);
    }

    [Fact]
    public void Number_inputs_apply_too_and_can_be_hidden()
    {
        Selections? raised = null;
        var cut = RenderFacet(onChanged: s => raised = s);

        cut.Find("input.l2d-slider-input-from").Change("250");
        Assert.Equal(Selections.Empty.With("Amount", RangeSelection.AtLeast(250)), raised);

        cut.Find("input.l2d-slider-input-to").Change("99999"); // clamped to max, which is the end
        Assert.Equal(Selections.Empty.With("Amount", RangeSelection.AtLeast(250)), raised);

        var noInputs = RenderFacet(configure: f => f.Add(x => x.ShowSliderInputs, false));
        Assert.Empty(noInputs.FindAll("input[type=number]"));
        Assert.Equal("0 – 2,500", noInputs.Find(".l2d-slider-values").TextContent);
    }

    [Fact]
    public void Unparseable_input_is_ignored()
    {
        Selections? raised = null;
        var cut = RenderFacet(onChanged: s => raised = s);

        cut.Find("input.l2d-slider-input-to").Change("abc");

        Assert.Null(raised);
        Assert.Equal(("0", "2500"), Handles(cut));
    }

    [Fact]
    public void Explicit_step_is_used()
    {
        var cut = RenderFacet(configure: f => f.Add(x => x.SliderStep, 250d));

        Assert.Equal("250", cut.Find("input.l2d-slider-from").GetAttribute("step"));
    }
}
