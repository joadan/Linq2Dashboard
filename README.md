# Linq2Dashboard

Interactive exploration of large in-memory collections for .NET: facets with counts, metrics, and paged results that all update together on every click. The faceted-search experience of an e-commerce site, applied to any collection, with a LINQ-flavoured API.

The core library has no UI dependency. A Blazor package is planned on top of it.

## Status

The core engine is complete for the first version and meets its performance targets: a million rows with eight facets builds in about a second and recalculates in 5 to 20 ms per click. The Blazor package does not exist yet. The API may still change before a first release.

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

    b.Count("orders");
    b.Sum("revenue", x => x.Amount);
    b.Average("average", x => x.Amount);

    b.OrderByDescending(x => x.OrderDate);
});

// The UI owns the selections. The dashboard is a pure function of them.
var selections = Selections.Empty
    .Toggle("Country", "SE")
    .With("Amount", RangeSelection.Between(100, 1000));

DashboardState<Order> state = dashboard.Calculate(selections);

state.MatchingCount;                                     // rows matching every selection
state.Metric("revenue").Value;                           // sum over the matching rows, null if none

var country = (ValueFacetState)state.Facet("Country");
foreach (FacetValue value in country.Values)             // SE is selected and still shows every other country's count
    Console.WriteLine($"{value.Value ?? "(none)"}  {value.FilteredCount}/{value.TotalCount}");

var amount = (RangeFacetState)state.Facet("Amount");
selections = selections.With("Amount", amount.Buckets[1].ToSelection());   // a bucket click

ResultPage<Order> page = state.GetPage(pageIndex: 0, pageSize: 50);

string bookmark = dashboard.Serializer.ToJson(selections);   // store, put in a URL, restore later
Selections restored = dashboard.Serializer.FromJson(bookmark);
```

## How it behaves

The rules are decisions, not options. They are spelled out in the [concept document](Linq2Dashboard-concept.md); the short version:

- **OR within a facet, AND across facets.** Selecting Sweden and Norway matches either; adding Status = Open narrows both.
- **A facet's own selection is excluded from its own counts.** Under Country you see what selecting Norway *would* add, not zeros everywhere else.
- **Every value carries two counts**, total over the dataset and filtered under the other facets' selections, and filtered counts always sum to the facet's context count.
- **Null is a value.** It is shown, counted and selectable like any other, never silently dropped.
- **Zero-count values stay in the state.** Hiding or greying them is the UI's choice.
- **Range and date buckets are fixed at build**; only their counts change. A bucket click produces exactly the interval the bucket covers.
- **Metrics skip null** and divide averages by rows that have a value.
- **The data is fixed at initialisation.** New data means a new dashboard; selections are serialisable, so the view carries over.

## Performance

Measured at one million rows on a 4-core machine (`benchmarks/`):

| Scenario | Time |
|---|---|
| Build, 8 facets, 3 metrics, sort order | 1.04 s |
| Recalculate, 3 facets selected, warm | 9.4 ms (4.9 ms with parallel counting) |
| Recalculate, cold caches | 20 ms |
| Search over 100 000 customer values | 4.3 ms |
| Memory for the full dashboard | 78 MB |

Every facet is a dictionary-encoded column; counting is one pass over the rows in context and is independent of how many distinct values a facet has. Details in the [design document](Linq2Dashboard-design.md).

## Repository

```text
src/Linq2Dashboard/              the core library, net10.0, no dependencies
tests/Linq2Dashboard.Tests/      xUnit; every behavioural rule has a named test
benchmarks/Linq2Dashboard.Benchmarks/   BenchmarkDotNet suite and a --memory report
Linq2Dashboard-concept.md        what it is and how it behaves
Linq2Dashboard-design.md         how it is built, with measured numbers
```

```powershell
dotnet test                                                      # all tests
dotnet run -c Release --project benchmarks/Linq2Dashboard.Benchmarks -- --memory
dotnet run -c Release --project benchmarks/Linq2Dashboard.Benchmarks -- --job short --filter *
```

## Licence

MIT. See [LICENSE.txt](LICENSE.txt).
