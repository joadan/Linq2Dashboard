namespace Linq2Dashboard.Docs.Pages;

public partial class CachedDashboard
{
    private const string PackageExample = """
        dotnet add package Microsoft.Extensions.Caching.Hybrid
        """;

    private const string ServiceExample = """
        using Linq2Dashboard;
        using Microsoft.Extensions.Caching.Hybrid;

        namespace Shop;

        public sealed class DashboardService(HybridCache cache)
        {
            private const string Key = "orders-dashboard";

            private static readonly HybridCacheEntryOptions Options = new()
            {
                Expiration = TimeSpan.FromMinutes(10),
                Flags = HybridCacheEntryFlags.DisableDistributedCache,
            };

            public ValueTask<Dashboard<Order>> GetAsync() =>
                cache.GetOrCreateAsync(Key, static async cancellationToken =>
                {
                    IReadOnlyList<Order> orders = await LoadOrdersAsync(cancellationToken);

                    return Dashboard.Create(orders, b =>
                    {
                        b.ValueFacet(x => x.Country);
                        b.ValueFacet(x => x.Status);
                        b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);
                        b.DateFacet(x => x.OrderDate).Presets(DatePreset.ThisYear);

                        b.CountMetric("orders").Name("Orders");
                        b.SumMetric("revenue", x => x.Amount).Name("Revenue");

                        b.OrderByDescending(x => x.OrderDate);
                    });
                }, Options);

            // The entry expires on its own after ten minutes and the next GetAsync loads fresh rows and builds a new
            // dashboard. Call this to drop it sooner, for example from the code that changes the orders.
            public ValueTask ClearAsync() => cache.RemoveAsync(Key);

            private static async Task<IReadOnlyList<Order>> LoadOrdersAsync(CancellationToken cancellationToken)
            {
                // Stands in for the query that loads your rows: a database, an API, a file.
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                return Order.All;
            }
        }
        """;

    private const string ProgramExample = """
        // ... after builder.Services.AddRazorComponents() ...
        builder.Services.AddHybridCache();
        builder.Services.AddSingleton<Shop.DashboardService>();
        """;

    private const string PageExample = """
        @page "/"
        @rendermode InteractiveServer
        @inject DashboardService Dashboards

        <PageTitle>Orders</PageTitle>

        @if (dashboard is null)
        {
            <p>Loading orders...</p>
        }
        else
        {
            <DashboardView T="Order" Context="dash" Dashboard="dashboard" @bind-Selections="selections" SyncUrl="true">
                <div style="display: grid; grid-template-columns: 16rem 1fr; gap: 1rem;">
                    <aside>
                        <ValueFacet T="Order" For="x => x.Country" />
                        <ValueFacet T="Order" For="x => x.Status" />
                        <RangeFacet T="Order" For="x => x.Amount" />
                        <DateFacet  T="Order" For="x => x.OrderDate" />
                    </aside>
                    <main>
                        <div style="display: flex; gap: 0.5rem;">
                            <Metric T="Order" Key="orders" />
                            <Metric T="Order" Key="revenue" />
                        </div>
                        <ActiveSelections T="Order" />
                        <QuickGrid Items="dash.Items" Pagination="pagination">
                            <PropertyColumn Property="o => o.Id" Sortable="true" />
                            <PropertyColumn Property="o => o.Country" Sortable="true" />
                            <PropertyColumn Property="o => o.Status" Sortable="true" />
                            <PropertyColumn Property="o => o.Amount" Format="N2" Sortable="true" />
                            <PropertyColumn Property="o => o.OrderDate" Title="Date" Format="d" Sortable="true" />
                        </QuickGrid>
                        <Paginator State="pagination" />
                    </main>
                </div>
            </DashboardView>
        }

        @code {
            private Dashboard<Order>? dashboard;
            private Selections selections = Selections.Empty;
            private readonly PaginationState pagination = new() { ItemsPerPage = 10 };

            protected override async Task OnInitializedAsync()
            {
                dashboard = await Dashboards.GetAsync();
            }
        }
        """;
}
