using System.Globalization;
using Microsoft.AspNetCore.Components;
using Linq2Dashboard.Blazor;
using Linq2Dashboard.SampleData;

namespace Linq2Dashboard.Docs.Pages;

public partial class Demo
{
    private static readonly int[] RowOptions = [20_000, 50_000, 100_000, 200_000, 500_000, 1_000_000];
    private const int DefaultRows = 50_000;
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

        await BuildAsync();
    }

    private async Task OnRowsChanged(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out int requested) && RowOptions.Contains(requested) && requested != rows)
        {
            rows = requested;
            // New data means a new dashboard; the selections carry over (concept §4.9), and the view restores them from the URL.
            await BuildAsync();
        }
    }

    private async Task BuildAsync()
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

        dashboard = built;
        building = false;
        UpdateRowsInUrl();
    }

    private Task OnSelectionsChanged(Selections changed)
    {
        selections = changed;
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

    /// <summary>
    /// The row count is the page's own URL parameter; the selections are the view's, kept by <c>SyncUrl</c>. The
    /// other parameters are copied as they are so the view's readable form is not re-encoded.
    /// </summary>
    private void UpdateRowsInUrl()
    {
        var uri = new Uri(Navigation.Uri);
        IEnumerable<string> others = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => !segment.StartsWith(RowsParameter + "=", StringComparison.Ordinal));
        string query = string.Join('&', rows == DefaultRows ? others : others.Prepend($"{RowsParameter}={rows}"));
        string url = uri.GetLeftPart(UriPartial.Path) + (query.Length > 0 ? "?" + query : string.Empty);
        if (url != Navigation.Uri)
        {
            Navigation.NavigateTo(url, replace: true);
        }
    }
}
