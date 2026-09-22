using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Docs.Pages;

public partial class GettingStarted
{
    private const string BuilderExample = """
        using Linq2Dashboard;

        var dashboard = Dashboard.Create(orders, b =>
        {
            b.Where(x => x.CompanyId == 42);                       // fixed filter: defines the dataset

            b.ValueFacet(x => x.Country);
            b.ValueFacet(x => x.Status).Name("Order status");
            b.ValueFacet("Customer", x => x.CustomerId)            // count and select by id ...
             .Label(x => x.CustomerName)                           // ... show and search by name
             .Top(20).Searchable();
            b.BooleanFacet(x => x.IsActive);
            b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);   // below 100, 100-500, 500-1000, 1000 and above
            b.DateFacet(x => x.OrderDate)
             .TimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm"))
             .Granularity(DateGranularity.Month)
             .Presets(DatePreset.Last30Days, DatePreset.ThisYear);
            b.TextFacet("search", (x, text) =>                    // free text; your function decides what matches
                x.CustomerName.Contains(text, StringComparison.OrdinalIgnoreCase));

            b.CountMetric("orders");
            b.SumMetric("revenue", x => x.Amount);
            b.AverageMetric("average", x => x.Amount);
            b.DistinctMetric("customers", x => x.CustomerId);
            b.CalculatedMetric("perCustomer", m => m["revenue"] / m["customers"]);

            b.OrderByDescending(x => x.OrderDate);
        });
        """;

    private const string CalculateExample = """
        var selections = Selections.Empty
            .Toggle("Country", "SE")
            .With("Amount", RangeSelection.Between(100, 1000));

        DashboardState<Order> state = dashboard.Calculate(selections);

        state.MatchingCount;                          // rows matching every selection
        state.Metric("revenue").Value;                // sum over the matching rows, null if none

        var country = (ValueFacetState)state.Facet("Country");
        foreach (FacetValue value in country.Values)  // SE is selected; other countries keep their counts
            Console.WriteLine($"{value.Value ?? "(none)"}  {value.FilteredCount}/{value.TotalCount}");

        var amount = (RangeFacetState)state.Facet("Amount");
        selections = selections.ToggleInterval("Amount", amount.Buckets[1].ToInterval());   // a bar click: bars toggle like values

        IReadOnlyList<Order> rows = state.Items;      // counted and indexable: hand it to your grid

        string bookmark = dashboard.Serializer.ToJson(selections);
        Selections restored = dashboard.Serializer.FromJson(bookmark);
        """;

    private const string BlazorExample = """
        @inject Dashboard<Order> Dashboard

        <DashboardView T="Order" Context="dash" Dashboard="Dashboard" @bind-Selections="selections">
            <aside>
                <TextFacet  T="Order" Key="search" />
                <ValueFacet T="Order" For="x => x.Country" />
                <ValueFacet T="Order" Key="Customer" />
                <RangeFacet T="Order" For="x => x.Amount" ShowSlider="true" />
                <DateFacet  T="Order" For="x => x.OrderDate" />
            </aside>
            <main>
                <Metric T="Order" Key="orders" />
                <Metric T="Order" Key="revenue" />
                <ActiveSelections T="Order" />
                <QuickGrid Items="dash.Items" Virtualize="true">
                    <PropertyColumn Property="o => o.Id" Sortable="true" />
                    <PropertyColumn Property="o => o.Country" Sortable="true" />
                    <PropertyColumn Property="o => o.Amount" Format="N2" Sortable="true" />
                </QuickGrid>
            </main>
        </DashboardView>

        @code {
            private Selections selections = Selections.Empty;
        }
        """;
}
