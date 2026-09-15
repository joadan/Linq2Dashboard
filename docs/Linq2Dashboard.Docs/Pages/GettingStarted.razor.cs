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

            b.Count("orders");
            b.Sum("revenue", x => x.Amount);
            b.Average("average", x => x.Amount);
            b.Distinct("customers", x => x.CustomerId);
            b.Calculated("perCustomer", m => m["revenue"] / m["customers"]);

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
        selections = selections.With("Amount", amount.Buckets[1].ToSelection());   // a bucket click

        ResultPage<Order> page = state.GetPage(pageIndex: 0, pageSize: 50);

        string bookmark = dashboard.Serializer.ToJson(selections);
        Selections restored = dashboard.Serializer.FromJson(bookmark);
        """;

    private const string BlazorExample = """
        @inject Dashboard<Order> Dashboard

        <DashboardView T="Order" Dashboard="Dashboard" @bind-Selections="selections">
            <aside>
                <TextFacet  T="Order" Key="search" />
                <ValueFacet T="Order" Key="Country" />
                <ValueFacet T="Order" Key="Customer" />
                <RangeFacet T="Order" Key="Amount" ShowSlider="true" />
                <DateFacet  T="Order" Key="OrderDate" />
            </aside>
            <main>
                <MatchingCount T="Order" />
        <Metric T="Order" Key="revenue" />
                <ActiveSelections T="Order" />
                <Results T="Order" Layout="ResultsLayout.Table" PageSize="25">
                    <HeaderTemplate><tr><th>Id</th><th>Country</th><th>Amount</th></tr></HeaderTemplate>
                    <RowTemplate Context="order">
                        <tr><td>@order.Id</td><td>@order.Country</td><td>@order.Amount</td></tr>
                    </RowTemplate>
                </Results>
            </main>
        </DashboardView>

        @code {
            private Selections selections = Selections.Empty;
        }
        """;
}
