namespace Linq2Dashboard.Docs.Pages;

public partial class FiveMinutes
{
    private const string CreateAppExample = """
        dotnet new blazor -n Shop --interactivity Server
        cd Shop
        dotnet add package Linq2Dashboard --prerelease
        dotnet add package Linq2Dashboard.Blazor --prerelease
        dotnet add package Microsoft.Extensions.Caching.Hybrid
        """;

    private const string RowsExample = """
        namespace Shop;

        public record Order(int Id, string? Country, string Status, decimal Amount, DateTime OrderDate)
        {
            public static readonly Order[] All =
            [
                new(1,  "SE", "Shipped",   120m,  new DateTime(2026, 1, 12)),
                new(2,  "SE", "Pending",   80m,   new DateTime(2026, 2, 3)),
                new(3,  "NO", "Shipped",   450m,  new DateTime(2026, 2, 17)),
                new(4,  "NO", "Cancelled", 30m,   new DateTime(2026, 3, 1)),
                new(5,  "DK", "Shipped",   1200m, new DateTime(2026, 3, 22)),
                new(6,  "DK", "Shipped",   640m,  new DateTime(2026, 4, 5)),
                new(7,  "FI", "Pending",   210m,  new DateTime(2026, 4, 19)),
                new(8,  "SE", "Shipped",   2100m, new DateTime(2026, 5, 8)),
                new(9,  "FI", "Cancelled", 95m,   new DateTime(2026, 5, 30)),
                new(10, "NO", "Shipped",   310m,  new DateTime(2026, 6, 14)),
                new(11, "SE", "Shipped",   75m,   new DateTime(2026, 7, 2)),
                new(12, null, "Pending",   500m,  new DateTime(2026, 7, 21)),
            ];
        }
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

                        b.Count("orders");
                        b.Sum("revenue", x => x.Amount);

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

    private const string ImportsExample = """
        @using Linq2Dashboard
        @using Linq2Dashboard.Blazor
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
            <DashboardView T="Order" Dashboard="dashboard" @bind-Selections="selections" SyncUrl="true">
                <div style="display: grid; grid-template-columns: 16rem 1fr; gap: 1rem;">
                    <aside>
                        <ValueFacet T="Order" Key="Country" />
                        <ValueFacet T="Order" Key="Status" />
                        <RangeFacet T="Order" Key="Amount" />
                        <DateFacet  T="Order" Key="OrderDate" />
                    </aside>
                    <main>
                        <div style="display: flex; gap: 0.5rem;">
                            <MatchingCount T="Order" />
                            <Metric T="Order" Key="revenue" />
                        </div>
                        <ActiveSelections T="Order" />
                        <Results T="Order" Layout="ResultsLayout.Table" PageSize="10">
                            <HeaderTemplate><tr><th>Id</th><th>Country</th><th>Status</th><th>Amount</th><th>Date</th></tr></HeaderTemplate>
                            <RowTemplate Context="order">
                                <tr>
                                    <td>@order.Id</td>
                                    <td>@(order.Country ?? "-")</td>
                                    <td>@order.Status</td>
                                    <td>@order.Amount</td>
                                    <td>@order.OrderDate.ToShortDateString()</td>
                                </tr>
                            </RowTemplate>
                        </Results>
                    </main>
                </div>
            </DashboardView>
        }

        @code {
            private Dashboard<Order>? dashboard;
            private Selections selections = Selections.Empty;

            protected override async Task OnInitializedAsync()
            {
                dashboard = await Dashboards.GetAsync();
            }
        }
        """;
}
