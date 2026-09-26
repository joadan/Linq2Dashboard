namespace Linq2Dashboard.Docs.Pages;

public partial class FiveMinutes
{
    private const string CreateAppExample = """
        dotnet new blazor -n Shop --interactivity Server
        cd Shop
        dotnet add package Linq2Dashboard.Blazor --prerelease
        dotnet add package Microsoft.AspNetCore.Components.QuickGrid
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

    private const string ImportsExample = """
        @using Linq2Dashboard
        @using Linq2Dashboard.Blazor
        @using Microsoft.AspNetCore.Components.QuickGrid
        """;

    private const string PageExample = """
        @page "/"
        @rendermode InteractiveServer

        <PageTitle>Orders</PageTitle>

        <DashboardView Context="dash" Items="Order.All" Build="b => b.OrderByDescending(x => x.OrderDate)"
                       @bind-Selections="selections" SyncUrl="true">
            <div style="display: grid; grid-template-columns: 16rem 1fr; gap: 1rem;">
                <aside>
                    <ValueFacet For="x => x.Country" />
                    <ValueFacet For="x => x.Status" />
                    <RangeFacet For="x => x.Amount" Buckets="[100, 500, 1000]" />
                    <DateFacet For="x => x.OrderDate" Presets="[DatePreset.ThisYear]" />
                </aside>
                <main>
                    <div style="display: flex; gap: 0.5rem;">
                        <Metric Key="orders" Count="true" Name="Orders" />
                        <Metric Key="revenue" Sum="x => x.Amount" Name="Revenue" />
                    </div>
                    <ActiveSelections />
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

        @code {
            private Selections selections = Selections.Empty;
            private readonly PaginationState pagination = new() { ItemsPerPage = 10 };
        }
        """;
}
