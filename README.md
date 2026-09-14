# Linq2Dashboard

[![CI](https://github.com/joadan/Linq2Dashboard/actions/workflows/ci.yml/badge.svg)](https://github.com/joadan/Linq2Dashboard/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Linq2Dashboard.svg?label=Linq2Dashboard)](https://www.nuget.org/packages/Linq2Dashboard/)
[![NuGet](https://img.shields.io/nuget/v/Linq2Dashboard.Blazor.svg?label=Linq2Dashboard.Blazor)](https://www.nuget.org/packages/Linq2Dashboard.Blazor/)

Interactive exploration of large in-memory collections for .NET: facets with counts, metrics, and paged results that all update together on every click. The faceted-search experience of an e-commerce site, applied to any collection, with a LINQ-flavoured API.

The core library has no UI dependency. A Blazor package renders it. **Docs and a live demo:** [joadan.github.io/Linq2Dashboard](https://joadan.github.io/Linq2Dashboard/), a Blazor WebAssembly site that builds the dashboard in your browser.

## Status

The core engine is complete for the first version and meets its performance targets: a million rows with eight facets builds in about a second and recalculates in 5 to 20 ms per click. The Blazor package has every component from the plan: facets for each kind, active-selection chips, metric tiles and paged results, with templates and a custom-property stylesheet. The API may still change before a first release.

## Install

```powershell
dotnet add package Linq2Dashboard          # the engine, no UI dependency
dotnet add package Linq2Dashboard.Blazor   # the components
```

Versions come from [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning): `version.json` holds the major.minor and the prerelease tag, the build height supplies the patch. Releases are cut manually from the **Create Release** workflow, which tests, packs, pushes to NuGet through [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) and tags the commit. No API key is stored anywhere.

## Example

```csharp
using Linq2Dashboard;

var dashboard = Dashboard.Create(orders, b =>
{
    b.Where(x => x.CompanyId == 42);                       // fixed filter: defines the dataset

    b.ValueFacet(x => x.Country);
    b.ValueFacet(x => x.Status).Title("Order status");
    b.ValueFacet(x => x.Customer).Top(20).Searchable();
    b.BooleanFacet(x => x.IsActive);
    b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);   // below 100, 100-500, 500-1000, 1000 and above
    b.DateFacet(x => x.OrderDate)
     .TimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm"))
     .Granularity(DateGranularity.Month)
     .Presets(DatePreset.Last30Days, DatePreset.ThisYear);
    b.TextFacet("search", (x, text) =>                       // free text; the function decides what matches
        x.Customer.Contains(text, StringComparison.OrdinalIgnoreCase));

    b.Count("orders");
    b.Sum("revenue", x => x.Amount);
    b.Average("average", x => x.Amount);
    b.Distinct("customers", x => x.Customer);              // how many different customers the selection touches
    b.Calculated("perCustomer", m => m["revenue"] / m["customers"]);   // a formula over earlier metrics

    b.OrderByDescending(x => x.OrderDate);
});

// The UI owns the selections. The dashboard is a pure function of them.
var selections = Selections.Empty
    .Toggle("Country", "SE")
    .With("Amount", RangeSelection.Between(100, 1000));

DashboardState<Order> state = dashboard.Calculate(selections);

state.MatchingCount;                                     // rows matching every selection
state.Metric("revenue").Value;                           // sum over the matching rows, null if none
state.Metric("revenue").Share;                           // that sum as a fraction of the sum over all rows

var country = (ValueFacetState)state.Facet("Country");
foreach (FacetValue value in country.Values)             // SE is selected and still shows every other country's count
    Console.WriteLine($"{value.Value ?? "(none)"}  {value.FilteredCount}/{value.TotalCount}");

var amount = (RangeFacetState)state.Facet("Amount");
selections = selections.With("Amount", amount.Buckets[1].ToSelection());   // a bucket click

ResultPage<Order> page = state.GetPage(pageIndex: 0, pageSize: 50);

string bookmark = dashboard.Serializer.ToJson(selections);   // store, put in a URL, restore later
Selections restored = dashboard.Serializer.FromJson(bookmark);
```

## Blazor

`Linq2Dashboard.Blazor` renders a dashboard and turns clicks into selections. It never counts anything itself. Register the dashboard once, then:

```razor
<DashboardView T="Order" Dashboard="Dashboard" @bind-Selections="selections">
    <aside>
        <ValueFacet T="Order" Key="Country" />
        <ValueFacet T="Order" Key="Customer" />           @* searchable, with an "Other" row *@
        <RangeFacet T="Order" Key="Amount" />             @* histogram; bars keep their shape *@
        <DateFacet  T="Order" Key="OrderDate" />          @* presets and periods *@
    </aside>
    <main>
        <MatchingCount T="Order" />
        <Metric T="Order" Key="revenue" />
        <ActiveSelections T="Order" />
        <Results T="Order" Layout="ResultsLayout.Table" PageSize="25">
            <HeaderTemplate><tr><th>Id</th><th>Country</th><th>Amount</th></tr></HeaderTemplate>
            <RowTemplate Context="order"><tr><td>@order.Id</td><td>@order.Country</td><td>@order.Amount</td></tr></RowTemplate>
        </Results>
    </main>
</DashboardView>
```

- **One formatter.** An `IDashboardFormatter` cascades from `DashboardView`; culture, number formats, the null label and preset names all come from it. Pass your own for other wording.
- **Two callbacks.** `SelectionsChanged` gives the host every click for bookmarking; `StateChanged` gives it the new `DashboardState<T>` after every calculation, the initial one included, for rendering a chart or summary of its own.
- **Templates.** `HeaderTemplate` and `ValueTemplate` on the facets, `MetricTemplate` on the tiles, `RowTemplate`, `HeaderTemplate` and `EmptyTemplate` on the results.
- **Collapsing.** Every facet has a header toggle by default (`Collapsible="false"` removes it) and a bindable `Collapsed` value, so a host can remember or set which facets are open.
- **Results.** Paged by default; `Virtualize="true"` scrolls every matching row in a fixed-height container instead, rendering only the visible ones. `RangeFacet` gets a dual-handle slider with `ShowSlider="true"`.
- **Styling.** Plain CSS. Every `--l2d-*` custom property is declared on `.l2d-dashboard`; set them on that element or any ancestor to restyle without touching markup. Dark-scheme neutrals are built in.
- **Hosting.** Blazor Server is the primary target. WebAssembly works unchanged; the browser's memory sets the dataset size.

The sample in `samples/` runs the components over 200 000 generated rows.

## How it behaves

The rules are decisions, not options. They are spelled out in the [concept document](Linq2Dashboard-concept.md); the short version:

- **OR within a facet, AND across facets.** Selecting Sweden and Norway matches either; adding Status = Open narrows both.
- **A facet's own selection is excluded from its own counts.** Under Country you see what selecting Norway *would* add, not zeros everywhere else.
- **Every value carries two counts**, total over the dataset and filtered under the other facets' selections, and filtered counts always sum to the facet's context count.
- **Null is a value.** It is shown, counted and selectable like any other, never silently dropped.
- **Zero-count values stay in the state.** Hiding or greying them is the UI's choice.
- **Range and date buckets are fixed at build**; only their counts change. A bucket click produces exactly the interval the bucket covers.
- **Free text is a facet too.** A text facet has no values; its text narrows the matching rows through the function you give it, and searching inside a value facet's list never does.
- **Metrics skip null** and divide averages by rows that have a value. Distinct counts different non-null values with the facets' equality rules. Count, sum and distinct also carry their share of the total, so a tile can read "12 400 (38 %)". Calculated metrics are formulas over earlier metrics: null in, no value out, and never infinity.
- **The data is fixed at initialisation.** New data means a new dashboard; selections are serialisable, so the view carries over.

## Performance

Measured at one million rows on a 4-core machine (`benchmarks/`):

| Scenario | Time |
|---|---|
| Build, 8 facets, 3 metrics, sort order | 1.04 s |
| Recalculate, 3 facets selected, warm | 9.4 ms (4.9 ms with parallel counting) |
| Recalculate, cold caches | 20 ms |
| Search over 100 000 customer values | 4.3 ms |
| New text in a text facet, two `Contains` per row | 81 ms (20 ms with parallel counting) |
| Memory for the full dashboard | 78 MB |

Every facet is a dictionary-encoded column; counting is one pass over the rows in context and is independent of how many distinct values a facet has. Details in the [design document](Linq2Dashboard-design.md).

## Repository

```text
src/Linq2Dashboard/              the core library, net10.0, no dependencies
src/Linq2Dashboard.Blazor/       Blazor components
samples/Linq2Dashboard.Sample/   Blazor Server sample app
samples/Linq2Dashboard.SampleData/   generated sample data shared by the sample and the docs site
docs/Linq2Dashboard.Docs/        Blazor WebAssembly docs site with the live demo, deployed to GitHub Pages by .github/workflows/pages.yml
tests/Linq2Dashboard.Tests/      xUnit; every behavioural rule has a named test
tests/Linq2Dashboard.Blazor.Tests/   bUnit component tests
benchmarks/Linq2Dashboard.Benchmarks/   BenchmarkDotNet suite and a --memory report
Linq2Dashboard-concept.md        what it is and how it behaves
Linq2Dashboard-design.md         how it is built, with measured numbers
```

```powershell
dotnet test                                                      # all tests
dotnet run --project samples/Linq2Dashboard.Sample               # the sample app
dotnet run --project docs/Linq2Dashboard.Docs                    # the docs site, locally
dotnet run -c Release --project benchmarks/Linq2Dashboard.Benchmarks -- --memory
dotnet run -c Release --project benchmarks/Linq2Dashboard.Benchmarks -- --job short --filter *
```

## Licence

MIT. See [LICENSE.txt](LICENSE.txt).
