using System.Globalization;
using Microsoft.AspNetCore.Components;
using Linq2Dashboard.Blazor;
using Linq2Dashboard.SampleData;

namespace Linq2Dashboard.Docs.Pages;

public partial class Demo
{
    private static readonly int[] RowOptions = [20_000, 50_000, 100_000, 200_000, 500_000, 1_000_000];
    private const int DefaultRows = 50_000;
    private const string SelectionsParameter = "s";
    private const string RowsParameter = "rows";

    private DashboardView<SampleOrder>? view;
    private Dashboard<SampleOrder>? dashboard;
    private Selections selections = Selections.Empty;
    private DateFacetState? timeline;
    private int timelineMax = 1;
    private int rows = DefaultRows;
    private long? generateMilliseconds;
    private long? buildMilliseconds;
    private bool building;

    protected override async Task OnInitializedAsync()
    {
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(Navigation.Uri).Query);
        if (int.TryParse(query[RowsParameter], out int requested) && RowOptions.Contains(requested))
        {
            rows = requested;
        }

        await BuildAsync(query[SelectionsParameter]);
    }

    private async Task OnRowsChanged(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out int requested) && RowOptions.Contains(requested) && requested != rows)
        {
            rows = requested;
            // New data means a new dashboard; the selections carry over (concept §4.9).
            string json = dashboard?.Serializer.ToJson(selections) ?? "{}";
            await BuildAsync(json);
        }
    }

    private async Task BuildAsync(string? selectionsJson)
    {
        building = true;
        dashboard = null;
        generateMilliseconds = null;
        buildMilliseconds = null;
        StateHasChanged();
        await Task.Delay(1); // let the "building" text paint before the single-threaded work starts

        var watch = System.Diagnostics.Stopwatch.StartNew();
        SampleOrder[] orders = SampleOrders.Generate(rows);
        generateMilliseconds = watch.ElapsedMilliseconds;

        watch.Restart();
        Dashboard<SampleOrder> built = SampleOrders.BuildDashboard(orders);
        buildMilliseconds = watch.ElapsedMilliseconds;

        selections = Restore(built, selectionsJson);
        dashboard = built;
        building = false;
        UpdateUrl();
    }

    private static Selections Restore(Dashboard<SampleOrder> target, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Selections.Empty;
        }

        try
        {
            return target.Serializer.FromJson(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return Selections.Empty;
        }
    }

    private Task OnSelectionsChanged(Selections changed)
    {
        selections = changed;
        UpdateUrl();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Every calculation, the first one included, hands the page the new state. The chart above the
    /// results is drawn from the date facet's monthly buckets; the page only stores what it needs to draw.
    /// </summary>
    private void OnStateChanged(DashboardState<SampleOrder> state)
    {
        timeline = (DateFacetState)state.Facet("OrderDate");
        // Bars are scaled to the largest total, so the histogram keeps its shape while the filtered part follows the selections (concept §5).
        timelineMax = Math.Max(1, timeline.Buckets.Count == 0 ? 1 : timeline.Buckets.Max(b => b.TotalCount));
    }

    /// <summary>A click on a bar selects that month; a click on a selected one clears the facet, as the facet's own bars do.</summary>
    private Task OnBarClicked(DateBucket bucket)
    {
        if (view is null)
        {
            return Task.CompletedTask;
        }

        return bucket.Selected
            ? view.Context.ClearAsync("OrderDate")
            : view.Context.SelectAsync("OrderDate", bucket.ToSelection());
    }

    private string Share(int count) => (count / (double)timelineMax).ToString("0.###", CultureInfo.InvariantCulture);

    private static string BarTitle(DateBucket bucket) =>
        $"{bucket.PeriodStart:MMM yyyy}: {bucket.FilteredCount:N0} of {bucket.TotalCount:N0} orders";

    private void UpdateUrl()
    {
        if (dashboard is null)
        {
            return;
        }

        string? json = selections.IsEmpty ? null : dashboard.Serializer.ToJson(selections);
        string url = Navigation.GetUriWithQueryParameters(new Dictionary<string, object?>
        {
            [RowsParameter] = rows == DefaultRows ? null : rows,
            [SelectionsParameter] = json,
        });
        Navigation.NavigateTo(url, replace: true);
    }
}
