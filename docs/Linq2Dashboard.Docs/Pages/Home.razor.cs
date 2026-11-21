using Linq2Dashboard.SampleData;

namespace Linq2Dashboard.Docs.Pages;

/// <summary>
/// The landing page. The hero holds a small live dashboard over the demo's generated orders, built after the first
/// paint so the page shows at once and the dashboard follows.
/// </summary>
public partial class Home
{
    private const int HeroRows = 5_000;

    private const string BuildExample = """
        var dashboard = Dashboard.Create(orders, b =>
        {
            b.ValueFacet(x => x.Country);
            b.ValueFacet(x => x.Status);
            b.RangeFacet(x => x.Amount);
            b.DateFacet(x => x.OrderDate);
            b.SumMetric("revenue", x => x.Amount);
        });

        var state = dashboard.Calculate(selections);   // same selections, same state
        """;

    private const string RenderExample = """
        <DashboardView Dashboard="dashboard" @bind-Selections="selections">
            <ValueFacet For="x => x.Country" />
            <RangeFacet For="x => x.Amount" />
            <Metric Key="revenue" />
        </DashboardView>
        """;

    private Dashboard<SampleOrder>? dashboard;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await Task.Delay(1); // let the hero paint before the single-threaded build starts
        dashboard = SampleOrders.BuildDashboard(HeroRows);
    }
}
