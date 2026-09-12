using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

public class ResultsTests : BunitContext
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
        builder.OpenElement(1, "td");
        builder.AddContent(2, order.Id);
        builder.CloseElement();
        builder.CloseElement();
    };

    private IRenderedComponent<DashboardView<Order>> RenderResults(
        Selections? selections = null, int pageSize = 3, Action<ComponentParameterCollectionBuilder<Results<Order>>>? configure = null) =>
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
                results.Add(r => r.PageSize, pageSize);
                configure?.Invoke(results);
            });
        });

    private static IEnumerable<string> Rows(IRenderedComponent<DashboardView<Order>> cut) =>
        cut.FindAll(".row").Select(r => r.TextContent.Trim());

    private static string Summary(IRenderedComponent<DashboardView<Order>> cut) => cut.Find(".l2d-results-summary").TextContent.Trim();

    private static string Status(IRenderedComponent<DashboardView<Order>> cut) => cut.Find(".l2d-pager-status").TextContent.Trim();

    [Fact]
    public void Renders_the_first_page_through_the_row_template_with_a_summary()
    {
        var cut = RenderResults();

        Assert.Equal(["1", "2", "3"], Rows(cut));
        Assert.Equal("Showing 1–3 of 8", Summary(cut));
        Assert.Equal("Page 1 of 3", Status(cut));
        Assert.True(cut.Find(".l2d-pager-previous").HasAttribute("disabled"));
        Assert.False(cut.Find(".l2d-pager-next").HasAttribute("disabled"));
    }

    [Fact]
    public void Pager_moves_through_the_pages_and_disables_at_the_ends()
    {
        var cut = RenderResults();

        cut.Find(".l2d-pager-next").Click();
        Assert.Equal(["4", "5", "6"], Rows(cut));
        Assert.Equal("Showing 4–6 of 8", Summary(cut));

        cut.Find(".l2d-pager-last").Click();
        Assert.Equal(["7", "8"], Rows(cut));
        Assert.Equal("Page 3 of 3", Status(cut));
        Assert.True(cut.Find(".l2d-pager-next").HasAttribute("disabled"));
        Assert.True(cut.Find(".l2d-pager-last").HasAttribute("disabled"));

        cut.Find(".l2d-pager-previous").Click();
        Assert.Equal(["4", "5", "6"], Rows(cut));

        cut.Find(".l2d-pager-first").Click();
        Assert.Equal(["1", "2", "3"], Rows(cut));
    }

    [Fact]
    public void Page_index_is_reported_to_the_host()
    {
        int? reported = null;
        var cut = RenderResults(configure: r => r.Add(x => x.PageIndexChanged, i => reported = i));

        cut.Find(".l2d-pager-next").Click();

        Assert.Equal(1, reported);
    }

    [Fact]
    public void A_selection_change_returns_to_the_first_page()
    {
        var cut = RenderResults();
        cut.Find(".l2d-pager-last").Click();
        Assert.Equal("Page 3 of 3", Status(cut));

        cut.Render(parameters => parameters.Add(p => p.Selections, Selections.Empty.With("Country", ValueSelection.Of("SE", "NO"))));

        Assert.Equal(["1", "2", "4"], Rows(cut)); // SE and NO orders: 1, 2, 4, 6, 8
        Assert.Equal("Showing 1–3 of 5", Summary(cut));
        Assert.Equal("Page 1 of 2", Status(cut));
    }

    [Fact]
    public void Rows_follow_the_selection_and_the_sort_order()
    {
        var cut = RenderResults(Selections.Empty.With("Country", ValueSelection.Of("SE")));

        Assert.Equal(["1", "4", "6"], Rows(cut));
        Assert.Empty(cut.FindAll(".l2d-pager")); // one page only
        Assert.Equal("Showing 1–3 of 3", Summary(cut));
    }

    [Fact]
    public void Empty_state_uses_the_text_or_template()
    {
        var text = RenderResults(Selections.Empty.With("Country", ValueSelection.Of("FI")));
        Assert.Equal("No matching rows", text.Find(".l2d-results-empty").TextContent.Trim());
        Assert.Empty(text.FindAll(".l2d-results-summary"));

        var template = RenderResults(Selections.Empty.With("Country", ValueSelection.Of("FI")),
            configure: r => r.Add(x => x.EmptyTemplate, "<em class='nothing'>Nothing here</em>"));
        Assert.Equal("Nothing here", template.Find(".l2d-results-empty .nothing").TextContent);
    }

    [Fact]
    public void Table_layout_puts_header_in_thead_and_rows_in_tbody()
    {
        var cut = RenderResults(configure: r =>
        {
            r.Add(x => x.Layout, ResultsLayout.Table);
            r.Add(x => x.RowTemplate, TableRow);
            r.Add(x => x.HeaderTemplate, "<tr><th>Id</th></tr>");
        });

        Assert.Equal("Id", cut.Find("table.l2d-results-table thead th").TextContent);
        Assert.Equal(["1", "2", "3"], cut.FindAll("table.l2d-results-table tbody td").Select(td => td.TextContent));
    }

    [Fact]
    public void Initial_page_index_is_honoured_and_clamped()
    {
        var second = RenderResults(configure: r => r.Add(x => x.PageIndex, 1));
        Assert.Equal(["4", "5", "6"], Rows(second));

        var beyond = RenderResults(configure: r => r.Add(x => x.PageIndex, 99));
        Assert.Equal(["7", "8"], Rows(beyond));
        Assert.Equal("Page 3 of 3", Status(beyond));
    }

    [Fact]
    public void Summary_can_be_hidden_and_texts_replaced()
    {
        var cut = RenderResults(configure: r =>
        {
            r.Add(x => x.ShowSummary, false);
            r.Add(x => x.PageFormat, "{0}/{1}");
        });

        Assert.Empty(cut.FindAll(".l2d-results-summary"));
        Assert.Equal("1/3", Status(cut));
    }

    [Fact]
    public void Invalid_parameters_are_rejected()
    {
        Assert.ThrowsAny<Exception>(() => RenderResults(pageSize: 0));
        Assert.ThrowsAny<Exception>(() => RenderResults(configure: r => r.Add(x => x.PageIndex, -1)));
    }
}
