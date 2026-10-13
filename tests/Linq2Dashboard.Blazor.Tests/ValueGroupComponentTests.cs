using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Linq2Dashboard.Blazor.Tests;

/// <summary>Groups of values in <see cref="ValueFacet{T}"/> and <see cref="ActiveSelections{T}"/> (concept §6, design §9 component 12).</summary>
public class ValueGroupComponentTests : BunitContext
{
    // Country counts: SE 3, NO 2, null 2, DK 1. Markets: SE and NO in North, DK in South; FI is not in the data.
    private static readonly Dictionary<string, string> Markets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SE"] = "North",
        ["NO"] = "North",
        ["DK"] = "South",
    };

    private static Dashboard<Order> BuildDashboard(Func<Order, string?>? group = null) =>
        Dashboard.Create(TestData.Orders(), b =>
        {
            b.ValueFacet(x => x.Country).GroupBy(group ?? (x => x.Country is null ? null : Markets[x.Country])).Searchable();
            b.ValueFacet(x => x.Status);
        });

    private IRenderedComponent<DashboardView<Order>> RenderFacet(
        Selections? selections = null,
        Action<Selections>? onChanged = null,
        Action<ComponentParameterCollectionBuilder<ValueFacet<Order>>>? configure = null,
        Dashboard<Order>? dashboard = null,
        bool chips = false) =>
        Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, dashboard ?? BuildDashboard());
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            if (selections is not null)
            {
                parameters.Add(p => p.Selections, selections);
            }

            if (onChanged is not null)
            {
                parameters.Add(p => p.SelectionsChanged, onChanged);
            }

            parameters.AddContent<ValueFacet<Order>>(facet =>
            {
                facet.Add(f => f.Key, "Country");
                configure?.Invoke(facet);
            });

            if (chips)
            {
                parameters.AddContent<ActiveSelections<Order>>(_ => { });
            }
        });

    private static IReadOnlyList<IElement> Groups(IRenderedComponent<DashboardView<Order>> cut) => cut.FindAll("li.l2d-group");

    private static IElement Group(IRenderedComponent<DashboardView<Order>> cut, string name) =>
        Groups(cut).Single(g => GroupName(g) == name);

    private static string GroupName(IElement group) => group.QuerySelector(".l2d-group-label")!.TextContent.Trim();

    private static IElement GroupRow(IElement group) => group.QuerySelector(".l2d-group-row")!;

    private static IElement GroupButton(IElement group) => group.QuerySelector(".l2d-group-button")!;

    private static string Label(IElement item) => item.QuerySelector(".l2d-facet-value-label")!.TextContent.Trim();

    private static IEnumerable<string> GroupValues(IElement group) => group.QuerySelectorAll("li.l2d-facet-value").Select(Label);

    /// <summary>The values listed outside any group, in order.</summary>
    private static IEnumerable<string> TopLevelValues(IRenderedComponent<DashboardView<Order>> cut) =>
        cut.FindAll("ul.l2d-facet-values > li.l2d-facet-value").Select(Label);

    [Fact]
    public void Groups_render_as_rows_with_summed_counts_and_their_values_inside()
    {
        var cut = RenderFacet();

        Assert.Equal(["North", "South"], Groups(cut).Select(GroupName));
        IElement north = Group(cut, "North");
        Assert.Equal("5", GroupButton(north).QuerySelector(".l2d-facet-value-count")!.TextContent.Trim());
        Assert.Equal(["SE", "NO"], GroupValues(north));
        Assert.Equal(["(none)"], TopLevelValues(cut));
    }

    [Fact]
    public void Ungrouped_values_follow_the_groups_and_the_null_value_comes_last()
    {
        var cut = RenderFacet(dashboard: BuildDashboard(x => x.Country is "DK" ? null : "North"));

        Assert.Equal(["North"], Groups(cut).Select(GroupName));
        Assert.Equal(["DK", "(none)"], TopLevelValues(cut));
    }

    [Fact]
    public void Clicking_a_group_selects_all_its_values()
    {
        Selections? raised = null;
        var cut = RenderFacet(onChanged: s => raised = s);

        GroupButton(Group(cut, "North")).Click();

        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("SE", "NO")), raised);
        IElement north = Group(cut, "North");
        Assert.Contains("l2d-selected", GroupRow(north).ClassName);
        Assert.Equal("true", GroupButton(north).GetAttribute("aria-pressed"));
        Assert.All(north.QuerySelectorAll("li.l2d-facet-value"), li => Assert.Contains("l2d-selected", li.ClassName));
    }

    [Fact]
    public void A_partly_selected_group_is_marked_mixed_and_a_click_completes_it()
    {
        Selections? raised = null;
        var cut = RenderFacet(Selections.Empty.Toggle("Country", "SE"), s => raised = s);

        IElement north = Group(cut, "North");
        Assert.Contains("l2d-group-partly", GroupRow(north).ClassName);
        Assert.Equal("mixed", GroupButton(north).GetAttribute("aria-pressed"));

        GroupButton(north).Click();

        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("SE", "NO")), raised);
    }

    [Fact]
    public void Clicking_a_fully_selected_group_removes_its_values()
    {
        Selections? raised = null;
        var cut = RenderFacet(Selections.Empty.With("Country", ValueSelection.Of("SE", "NO", "DK")), s => raised = s);

        GroupButton(Group(cut, "North")).Click();

        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("DK")), raised);
    }

    [Fact]
    public void The_chevron_folds_a_group_without_selecting_anything()
    {
        Selections? raised = null;
        var cut = RenderFacet(onChanged: s => raised = s);

        Group(cut, "North").QuerySelector(".l2d-group-toggle")!.Click();

        IElement north = Group(cut, "North");
        Assert.Contains("l2d-group-foldable", north.ClassName); // its values step in past the chevron
        Assert.Contains("l2d-group-folded", north.ClassName);
        Assert.Empty(GroupValues(north));
        Assert.Equal("false", north.QuerySelector(".l2d-group-toggle")!.GetAttribute("aria-expanded"));
        Assert.Null(raised);

        Group(cut, "North").QuerySelector(".l2d-group-toggle")!.Click();

        Assert.Equal(["SE", "NO"], GroupValues(Group(cut, "North")));
    }

    [Fact]
    public void GroupsCollapsed_starts_every_group_folded()
    {
        var cut = RenderFacet(configure: f => f.Add(x => x.GroupsCollapsed, true));

        Assert.All(Groups(cut), g => Assert.Empty(GroupValues(g)));
        Assert.Equal(["(none)"], TopLevelValues(cut));
    }

    [Fact]
    public void A_search_lists_matches_flat_with_their_group()
    {
        var cut = RenderFacet();

        cut.Find("input.l2d-facet-search").Input("e");

        Assert.Empty(Groups(cut));
        IElement se = cut.FindAll("li.l2d-facet-value").Single(li => li.QuerySelector(".l2d-facet-value-label")!.FirstChild!.TextContent == "SE");
        Assert.Equal("North", se.QuerySelector(".l2d-facet-value-group")!.TextContent);
    }

    [Fact]
    public void Sorting_by_label_orders_the_groups_by_name_and_the_values_inside()
    {
        var cut = RenderFacet(Selections.Empty.With("Status", ValueSelection.Of("Pending")), configure: f => f
            .Add(x => x.Sort, FacetSort.Label)
            .Add(x => x.SortDescending, true));

        // By rank, Pending puts South (1) before North (0); by name, descending, North comes first.
        Assert.Equal(["South", "North"], Groups(cut).Select(GroupName));
        Assert.Equal(["SE", "NO"], GroupValues(Group(cut, "North")));
    }

    [Fact]
    public void Groups_follow_the_rank_order_under_the_facets_context()
    {
        var cut = RenderFacet(Selections.Empty.With("Status", ValueSelection.Of("Pending")));

        Assert.Equal(["South", "North"], Groups(cut).Select(GroupName));
        Assert.Contains("l2d-zero", GroupRow(Group(cut, "North")).ClassName);
    }

    [Fact]
    public void HideZeroCounts_leaves_out_a_group_whose_values_all_count_zero()
    {
        var cut = RenderFacet(Selections.Empty.With("Status", ValueSelection.Of("Pending")), configure: f => f.Add(x => x.HideZeroCounts, true));

        Assert.Equal(["South"], Groups(cut).Select(GroupName));
    }

    [Fact]
    public void A_fully_selected_group_is_one_chip_part_and_removing_it_toggles_the_group_off()
    {
        Selections? raised = null;
        // The null value is never in a group, so it stays a part of its own beside North.
        var cut = RenderFacet(Selections.Empty.With("Country", ValueSelection.Of("SE", null, "NO")), s => raised = s, chips: true);

        IElement chip = cut.Find("li.l2d-chip");
        Assert.Equal(["North", "(none)"], chip.QuerySelectorAll(".l2d-chip-label").Select(l => l.TextContent.Trim()));

        chip.QuerySelectorAll(".l2d-chip-value-remove")[0].Click();

        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of([null])), raised);
    }

    [Fact]
    public void A_partly_selected_group_shows_its_values_as_chips()
    {
        var cut = RenderFacet(Selections.Empty.With("Country", ValueSelection.Of("SE")), chips: true);

        Assert.Equal("SE", cut.Find("li.l2d-chip .l2d-chip-label").TextContent.Trim());
    }

    [Fact]
    public void A_group_is_a_link_to_its_toggled_selections_when_the_view_is_not_interactive()
    {
        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("page");
        SetRendererInfo(new RendererInfo("Static", false));
        var cut = Render<DashboardView<Order>>(parameters =>
        {
            parameters.Add(p => p.Dashboard, BuildDashboard());
            parameters.Add(p => p.SyncUrl, true);
            parameters.Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture));
            parameters.AddContent<ValueFacet<Order>>(f => f.Add(x => x.Key, "Country").Add(x => x.GroupsCollapsed, true));
        });

        IElement link = cut.Find("a.l2d-group-button");
        Assert.Equal(
            Selections.Empty.With("Country", ValueSelection.Of("SE", "NO")),
            cut.Instance.Context.Dashboard.Serializer.FromQueryString(link.GetAttribute("href")!));
        Assert.Empty(cut.FindAll(".l2d-group-toggle"));
        Assert.DoesNotContain("l2d-group-foldable", Group(cut, "North").ClassName); // no chevron to step past
        Assert.Equal(["SE", "NO"], GroupValues(Group(cut, "North"))); // nothing could unfold a group, so links show them open
    }

    [Fact]
    public void Under_Items_GroupBy_defines_the_groups()
    {
        var cut = Render<DashboardView<Order>>(parameters => parameters
            .Add(p => p.Items, TestData.Orders())
            .Add(p => p.Formatter, new DefaultDashboardFormatter(CultureInfo.InvariantCulture))
            .AddContent<ValueFacet<Order>>(f => f
                .Add(x => x.For, x => x.Country)
                .Add(x => x.GroupBy, x => x.Country is null ? null : Markets[x.Country])));

        Assert.Equal(["North", "South"], Groups(cut).Select(GroupName));
    }

    [Fact]
    public void Under_Items_GroupBy_on_a_multi_valued_facet_throws()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Render<DashboardView<Order>>(parameters => parameters
            .Add(p => p.Items, TestData.Orders())
            .AddContent<ValueFacet<Order>>(f => f
                .Add(x => x.For, x => x.Tags)
                .Add(x => x.Multiple, true)
                .Add(x => x.GroupBy, _ => "All"))));

        Assert.Contains("no groups", error.Message);
    }
}
