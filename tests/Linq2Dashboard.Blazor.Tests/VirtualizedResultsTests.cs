using System.Globalization;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

public class VirtualizedResultsTests : BunitContext
{
    private static Dashboard<Order> BuildDashboard() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.OrderBy(x => x.Id);
        });

    private static readonly RenderFragment<Order> Row = order => builder =>
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "row");
        builder.AddContent(2, order.Id);
        builder.CloseElement();
    };

    private static readonly RenderFragment<Order> TableRow = order => builder =>
    {
        builder.OpenElement(0, "tr");
        builder.AddAttribute(1, "class", "row");
        builder.OpenElement(2, "td");
        builder.AddContent(3, order.Id);
        builder.CloseElement();
        builder.CloseElement();
    };

    private IRenderedComponent<DashboardView<Order>> RenderResults(
        Selections? selections = null, Action<ComponentParameterCollectionBuilder<Results<Order>>>? configure = null) =>
        Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, BuildDashboard());
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            if (selections is not null)
            {
                parameters.Add(p => p.Selections, selections);
            }

            parameters.AddChildContent<Results<Order>>(results =>
            {
                results.Add(r => r.RowTemplate, Row);
                results.Add(r => r.Virtualize, true);
                configure?.Invoke(results);
            });
        });

    private static IEnumerable<string> Rows(IRenderedComponent<DashboardView<Order>> cut) =>
        cut.FindAll(".row").Select(r => r.TextContent.Trim());

    [Fact]
    public void Renders_rows_inside_a_scroll_container_without_a_pager()
    {
        var cut = RenderResults();

        Assert.Single(cut.FindAll(".l2d-results-scroll"));
        Assert.Contains("l2d-results-virtualized", cut.Find(".l2d-results").ClassName);
        Assert.Empty(cut.FindAll(".l2d-pager"));
        Assert.Equal("8 rows", cut.Find(".l2d-results-summary").TextContent.Trim());
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8"], Rows(cut));
    }

    [Fact]
    public void Table_layout_puts_virtualised_rows_in_tbody_with_tr_spacers()
    {
        var cut = RenderResults(configure: r =>
        {
            r.Add(x => x.Layout, ResultsLayout.Table);
            r.Add(x => x.RowTemplate, TableRow);
            r.Add(x => x.HeaderTemplate, "<tr><th>Id</th></tr>");
        });

        Assert.Equal("Id", cut.Find(".l2d-results-scroll table thead th").TextContent);
        Assert.Equal(8, cut.FindAll(".l2d-results-scroll table tbody tr.row").Count);
        Assert.Empty(cut.FindAll(".l2d-results-scroll table tbody div"));
    }

    [Fact]
    public void Rows_follow_the_selection_and_refresh_when_it_changes()
    {
        var cut = RenderResults(Selections.Empty.With("Country", ValueSelection.Of("SE")));
        Assert.Equal(["1", "4", "6"], Rows(cut));
        Assert.Equal("3 rows", cut.Find(".l2d-results-summary").TextContent.Trim());

        cut.Render(parameters => parameters.Add(p => p.Selections, Selections.Empty.With("Country", ValueSelection.Of("NO"))));

        cut.WaitForAssertion(() => Assert.Equal(["2", "8"], Rows(cut)));
        Assert.Equal("2 rows", cut.Find(".l2d-results-summary").TextContent.Trim());
    }

    [Fact]
    public void Empty_state_and_summary_text_apply()
    {
        var empty = RenderResults(Selections.Empty.With("Country", ValueSelection.Of("FI")));
        Assert.Equal("No matching rows", empty.Find(".l2d-results-empty").TextContent.Trim());
        Assert.Empty(empty.FindAll(".l2d-results-scroll"));

        var custom = RenderResults(configure: r => r.Add(x => x.VirtualSummaryFormat, "{0} orders"));
        Assert.Equal("8 orders", custom.Find(".l2d-results-summary").TextContent.Trim());
    }

    [Fact]
    public void Invalid_virtualisation_parameters_are_rejected()
    {
        Assert.ThrowsAny<Exception>(() => RenderResults(configure: r => r.Add(x => x.ItemSize, 0f)));
        Assert.ThrowsAny<Exception>(() => RenderResults(configure: r => r.Add(x => x.OverscanCount, -1)));
    }
}
