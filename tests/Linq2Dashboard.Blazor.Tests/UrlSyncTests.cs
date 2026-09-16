using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Linq2Dashboard.Blazor.Tests;

/// <summary>Selections in the page URL through <c>SyncUrl</c> and <c>Key</c> on <see cref="DashboardView{T}"/> (design §2.5, §9).</summary>
public class UrlSyncTests : BunitContext
{
    private static Dashboard<Order> BuildDashboard() =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country);
            b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);
        });

    private BunitNavigationManager Navigation => (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<DashboardView<Order>> RenderView(
        Dashboard<Order>? dashboard = null,
        string? key = "o",
        bool syncUrl = true,
        bool interactive = true,
        Selections? selections = null,
        Action<Selections>? onChanged = null)
    {
        SetRendererInfo(new RendererInfo("Server", interactive));
        return Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, dashboard ?? BuildDashboard());
            parameters.Add(p => p.Key, key);
            parameters.Add(p => p.SyncUrl, syncUrl);
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            if (selections is not null)
            {
                parameters.Add(p => p.Selections, selections);
            }

            if (onChanged is not null)
            {
                parameters.Add(p => p.SelectionsChanged, onChanged);
            }
        });
    }

    private static IElement ValueButton(IRenderedComponent<DashboardView<Order>> cut, string facetKey, string label) =>
        cut.FindAll($"section[data-key='{facetKey}'] button.l2d-value").Single(b => b.TextContent.Trim() == label);

    private static string Matching(IRenderedComponent<DashboardView<Order>> cut) => cut.Find(".l2d-matching").TextContent;

    [Fact]
    public void The_url_wins_on_first_render_and_the_host_hears_about_it()
    {
        Navigation.NavigateTo("page?rows=5&o.Country=SE&r.Country=NO");
        Selections? raised = null;

        var cut = RenderView(selections: Selections.Empty.With("Country", ValueSelection.Of("DK")), onChanged: s => raised = s);

        Assert.Equal("3", Matching(cut));
        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("SE")), raised);
        Assert.Equal("http://localhost/page?rows=5&o.Country=SE&r.Country=NO", Navigation.Uri);
        Assert.Empty(Navigation.History.Skip(1));
    }

    [Fact]
    public void The_parameter_applies_when_the_url_has_nothing_for_the_view_and_is_then_written()
    {
        Navigation.NavigateTo("page?rows=5");
        Selections? raised = null;

        var cut = RenderView(selections: Selections.Empty.With("Country", ValueSelection.Of("DK")), onChanged: s => raised = s);

        Assert.Equal("1", Matching(cut));
        Assert.Null(raised);
        Assert.Equal("http://localhost/page?rows=5&o.Country=DK", Navigation.Uri);
    }

    [Fact]
    public void A_click_replaces_the_url_in_place_and_keeps_the_pages_other_parameters()
    {
        Navigation.NavigateTo("page?rows=5&r.Country=NO#top");
        var cut = RenderView();

        ValueButton(cut, "Country", "SE").Click();

        Assert.Equal("http://localhost/page?rows=5&r.Country=NO&o.Country=SE#top", Navigation.Uri);
        Assert.True(Navigation.History.Last().Options.ReplaceHistoryEntry);

        cut.FindAll("section[data-key='Amount'] button.l2d-value")[1].Click();
        Assert.Equal("http://localhost/page?rows=5&r.Country=NO&o.Country=SE&o.Amount=[100..500)#top", Navigation.Uri);

        ValueButton(cut, "Country", "SE").Click();
        Assert.Equal("http://localhost/page?rows=5&r.Country=NO&o.Amount=[100..500)#top", Navigation.Uri);
    }

    [Fact]
    public void Without_a_key_the_parameters_are_the_bare_facet_keys()
    {
        Navigation.NavigateTo("page");
        var cut = RenderView(key: null);

        ValueButton(cut, "Country", "SE").Click();

        Assert.Equal("http://localhost/page?Country=SE", Navigation.Uri);
        Assert.Null(cut.Find(".l2d-dashboard").GetAttribute("data-key"));
    }

    [Fact]
    public void A_navigation_within_the_page_applies_the_urls_selections_to_the_view()
    {
        Navigation.NavigateTo("page?o.Country=SE");
        Selections? raised = null;
        var cut = RenderView(onChanged: s => raised = s);
        Assert.Equal("3", Matching(cut));

        Navigation.NavigateTo("page?o.Country=NO");

        cut.WaitForAssertion(() => Assert.Equal("2", Matching(cut)));
        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("NO")), raised);

        Navigation.NavigateTo("page");
        cut.WaitForAssertion(() => Assert.Equal("8", Matching(cut)));
        Assert.Equal(Selections.Empty, raised);
    }

    [Fact]
    public void A_navigation_to_another_page_is_ignored()
    {
        Navigation.NavigateTo("page?o.Country=SE");
        var cut = RenderView();

        Navigation.NavigateTo("other");

        Assert.Equal("3", Matching(cut));
        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("SE")), cut.Instance.Context.Selections);
    }

    [Fact]
    public void Selections_set_by_the_host_are_written_to_the_url()
    {
        Navigation.NavigateTo("page");
        var cut = RenderView();

        cut.Render(parameters => parameters.Add(p => p.Selections, Selections.Empty.With("Amount", RangeSelection.AtLeast(500))));

        Assert.Equal("http://localhost/page?o.Amount=500..", Navigation.Uri);
        Assert.Equal("4", Matching(cut));
    }

    [Fact]
    public void A_prerendering_view_reads_the_url_but_does_not_write_it()
    {
        Navigation.NavigateTo("page?o.Country=SE");

        var cut = RenderView(interactive: false, selections: Selections.Empty.With("Country", ValueSelection.Of("DK")));

        Assert.Equal("3", Matching(cut));
        Assert.Empty(Navigation.History.Skip(1));
    }

    [Fact]
    public void Without_SyncUrl_the_url_is_neither_read_nor_written()
    {
        Navigation.NavigateTo("page?o.Country=SE");
        var cut = RenderView(syncUrl: false);
        Assert.Equal("8", Matching(cut));

        ValueButton(cut, "Country", "NO").Click();

        Assert.Equal("http://localhost/page?o.Country=SE", Navigation.Uri);
        Assert.Equal("o", cut.Find(".l2d-dashboard").GetAttribute("data-key"));
    }

    [Fact]
    public void Switching_the_dashboard_keeps_the_urls_selections()
    {
        Navigation.NavigateTo("page?o.Country=SE");
        var dashboard = BuildDashboard();
        var cut = RenderView(dashboard);

        cut.Render(parameters => parameters.Add(p => p.Dashboard, dashboard.Where(x => x.Amount < 500)));

        Assert.Equal("1", Matching(cut));
        Assert.Equal("http://localhost/page?o.Country=SE", Navigation.Uri);
    }
}
