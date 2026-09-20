# Linq2Dashboard – Usage

A short guide for using the library in another project. It is written so it can be pasted into a project's own instructions file, for a person or a coding assistant, and covers the API surface, the rules that surprise, and the mistakes to avoid. The [concept](Linq2Dashboard-concept.md) is the full specification; the [design](Linq2Dashboard-design.md) explains how it is built.

## What it is

Interactive exploration of a large in-memory collection: facets with counts, metrics and the matching rows that all update together on every selection. The faceted search of an e-commerce site, applied to any `IEnumerable<T>`.

| Package | Namespace | Contents |
|---|---|---|
| `Linq2Dashboard` | `Linq2Dashboard` | The engine. net10.0, no dependencies. |
| `Linq2Dashboard.Blazor` | `Linq2Dashboard.Blazor` | Components that render a dashboard and turn clicks into selections. |

`Linq2Dashboard.Blazor` depends on `Linq2Dashboard`, so a Blazor app adds only the Blazor package; add the engine alone for a host without the components. Both are prerelease on NuGet while the API settles: `dotnet add package Linq2Dashboard.Blazor --prerelease`.

## Wiring checklist

1. **Build once.** `Dashboard.Create(rows, b => { ... })` indexes the collection. Facets, metrics and sort order are fixed here. It takes about a second per million rows, synchronously and without cancellation: run it on a background thread or in a hosted service if startup must stay responsive.
2. **Register as a singleton.** The dashboard is immutable and thread-safe; one instance serves every user. Either the dashboard itself, `builder.Services.AddSingleton<Dashboard<Order>>(_ => Dashboard.Create(...))`, or a singleton service that loads the rows and builds it on first request and caches the instance. `Dashboard<T>` is marked `[ImmutableObject(true)]`, so `HybridCache` stores the instance itself; set `HybridCacheEntryFlags.DisableDistributedCache`, since a dashboard cannot be serialised. The [five-minute walkthrough](https://joadan.github.io/Linq2Dashboard/five-minutes) shows such a service.
3. **Add both usings** to `_Imports.razor`: `@using Linq2Dashboard` and `@using Linq2Dashboard.Blazor`, plus your grid's (`@using Microsoft.AspNetCore.Components.QuickGrid` below).
4. **Reference the app's scoped-CSS bundle** in the host page, `YourApp.styles.css`. The components' styles are bundled into it. No other stylesheet or script is needed.
5. **Wrap the page in `DashboardView`**, inject the dashboard, bind `Selections`, name the context and place components inside. Every component takes `T`, the row type, and a `Key` from the builder. The rows go to your grid through `dash.Items`.

```razor
@inject Dashboard<Order> Dashboard

<DashboardView T="Order" Context="dash" Dashboard="Dashboard" @bind-Selections="selections">
    <ValueFacet T="Order" Key="Country" />
    <Metric T="Order" Key="orders" />
    <QuickGrid Items="dash.Items" Virtualize="true">   @* any grid; dash.Items is one IQueryable<T> per state *@
        <PropertyColumn Property="o => o.Id" Sortable="true" />
    </QuickGrid>
</DashboardView>

@code {
    private Selections selections = Selections.Empty;
}
```

## The builder

```csharp
var dashboard = Dashboard.Create(orders, b =>
{
    b.Where(x => x.CompanyId == 42);                 // fixed filter; defines the dataset

    b.ValueFacet(x => x.Country);                    // key "Country", from the member name
    b.ValueFacet("Customer", x => x.CustomerId)      // explicit key; count and select by id...
     .Label(x => x.CustomerName)                     // ...show and search by name
     .Name("Customer").Top(20).Searchable();
    b.BooleanFacet(x => x.IsActive);
    b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);   // or .AutoBuckets(10): round edges derived from the data (the default)
    b.DateFacet(x => x.OrderDate)
     .TimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm"))
     .Granularity(DateGranularity.Month)
     .Presets(DatePreset.Last30Days, DatePreset.ThisYear);
    b.TextFacet("search", (x, text) => x.CustomerName.Contains(text, StringComparison.OrdinalIgnoreCase));

    b.CountMetric("orders");
    b.SumMetric("revenue", x => x.Amount).Name("Revenue");
    b.AverageMetric("average", x => x.Amount);
    b.MinMetric("smallest", x => x.Amount);
    b.MaxMetric("largest", x => x.Amount);
    b.DistinctMetric("customers", x => x.CustomerId);
    b.CalculatedMetric("perCustomer", m => m["revenue"] / m["customers"]);

    b.OrderByDescending(x => x.OrderDate).ThenBy(x => x.Id);
    b.EnableParallelCounting();                      // off by default; helps text facets most
});
```

Rules of the builder:

- A facet declared from a member expression takes the member's name as its key. Anything else needs an explicit key. Keys are case-sensitive and must be unique among facets and among metrics.
- Range facets accept any numeric type or its nullable form. Date facets accept `DateTime`, `DateTimeOffset`, `DateOnly` or their nullable forms.
- Value facet options: `Name`, `Top(n)` with an "Other" remainder, `RankBy(RankMode.TotalCount)` for a stable list, `Searchable()`, `Label(row => text)`, `Comparer(...)`, `Serialize(format, parse)` for value types JSON cannot round-trip by default.
- Range facet options: `Name`, `Buckets(cuts...)` strictly ascending, or `AutoBuckets(count)` (the default, with 10) for about `count` equal buckets on round edges over the body of the data, with an open bucket at each end for outliers.
- Date facet options: `Name`, `TimeZone`, `Granularity` (Year, Month, ISO Week, Day), `Presets` (Today, Yesterday, Last7Days, Last30Days, ThisWeek, LastWeek, ThisMonth, LastMonth, ThisYear, LastYear, YearToDate), `SkipEmptyPresets()` to leave out a preset no row falls in (off by default; re-decided at each calculation, and a selected empty preset is left out too).
- `CalculatedMetric` reads earlier metrics by key through `m["key"]`. It gives no value when any input has none or the result is not finite. Define its inputs before it.
- `UseTimeProvider` supplies "now" for relative presets, for tests.
- Mistakes surface inside `Create`, not at first use: an unknown metric key in a formula, a non-numeric range selector, non-ascending cuts, a duplicate key.

## Selections

The UI owns the selections. The dashboard holds none. `Selections` is an immutable map from facet key to `Selection`; every method returns a new instance, so always use the result.

```csharp
var s = Selections.Empty
    .Toggle("Country", "SE")                                  // add if absent, remove if present
    .With("Amount", RangeSelection.Between(100, 1000))        // replace the facet's selection
    .ToggleInterval("Amount", RangeInterval.AtLeast(5000))    // add or remove one interval: the click on a bar
    .With("OrderDate", DateSelection.Relative(DatePreset.Last30Days))
    .With("search", new TextSelection("acme"))
    .Clear("Country");                                        // remove one facet's selection
```

| Facet kind | Selection type | Constructors |
|---|---|---|
| Value, Boolean | `ValueSelection` | `ValueSelection.Of(a, b)`, `.Add`, `.Remove`; a `null` value selects the null facet value |
| Range | `RangeSelection` | a set of `RangeInterval`s, OR-ed: `new RangeSelection([a, b])`, `.Toggle(interval)`, `.ToggleNull()`; `Between(from, to)`, `AtLeast(from)`, `AtMost(to)` for one; `OnlyNull`; `IncludeNull` adds the null rows |
| Date | `DateSelection` | a set of `DateInterval`s, OR-ed, each `DateInterval.Between(from, to)` (half-open instants) or `DateInterval.Relative(preset)`; `Between`, `Relative` for one; `OnlyNull` |
| Text | `TextSelection` | `new TextSelection(text)`; whitespace-only clears |

Bookmarks: `dashboard.Serializer.ToJson(selections)` and `FromJson(json)`. Reading is lenient. Unknown facets and unreadable values are dropped, so a stale bookmark gives fewer selections, never an error.

URLs: `ToQueryString(selections)` and `FromQueryString(query)` use one readable parameter per facet, named by the facet key, that a person or another page can write by hand. Reading is as lenient as JSON and accepts a whole URL.

```text
?Country=SE,NO&Amount=[100..500)&OrderDate=last30Days&Shipped=2026-03-01T00:00+01:00..&Discount=null&search=acme
```

| Facet kind | Form | Notes |
|---|---|---|
| Value, Boolean | `SE,NO,null` | `null` is the null value; `\,` a literal comma; `""` the empty string |
| Range | `100..500`, `100..`, `..500`, `[100..500)`, `[..100),1000..` | a comma-separated list of intervals; inclusive without brackets; `(` or `)` marks an exclusive end; `null` as an item adds the null rows; `null` alone is only the null rows |
| Date | `last30Days`, `2026-03-01T00:00+01:00..2026-04-01T00:00+02:00`, `thisMonth,last7Days` | a comma-separated list of parts; preset names as in `DatePreset`, case-insensitive; `null` as for ranges |
| Text | `acme` | the text itself |

Both take a `prefix` so two dashboards on one page, or a page's own parameters, do not collide: `ToQueryString(selections, "o.")` writes `o.Country=SE`. `ToQueryString` also takes the existing query or URL and keeps every parameter in it that is not one of the dashboard's facets. `ToQuery` gives the parameters as a dictionary with `null` for unselected facets, the shape `NavigationManager.GetUriWithQueryParameters` takes. In Blazor, `SyncUrl` on `DashboardView` does all of this for you.

## The state

`dashboard.Calculate(selections)` returns a `DashboardState<T>`: a consistent, immutable snapshot. Same selections, same state, and the result is cached by selections.

```csharp
DashboardState<Order> state = dashboard.Calculate(selections);

state.TotalCount;                                 // rows after fixed filters
state.MatchingCount;                              // rows matching every selection
state.Metric("revenue").Value;                    // double?, null when no row contributed
state.Metric("revenue").Share;                    // fraction of the total, for Count, Sum and Distinct

var country = (ValueFacetState)state.Facet("Country");
country.Values;                                   // FacetValue: Value, Label, TotalCount, FilteredCount, Selected
country.Other;                                    // counts Top N left out, or null
country.Search("swe", max: 20);                   // a UI operation, not a selection

var amount = (RangeFacetState)state.Facet("Amount");
amount.Buckets[1].ToInterval();                   // exactly the interval a click on that bar toggles; ToSelection() is that interval alone

var date = (DateFacetState)state.Facet("OrderDate");
date.Presets[0].ToInterval();                     // DateInterval.Relative(preset); buckets give their [From, To)

state.Items;                                      // IReadOnlyList<T>: every matching row in order, counted and indexable; give it, or Items.AsQueryable(), to your grid
state.GetItems(skip: 200, take: 50);              // a slice, the same rows as Items.Skip(200).Take(50)
```

Cast `state.Facet(key)` by the facet's kind: `ValueFacetState` for value and boolean facets, `RangeFacetState`, `DateFacetState`, `TextFacetState`. `FacetState.Kind` says which.

## Scoping

```csharp
Dashboard<Order> nordic = dashboard.ScopeTo(x => x.Region == "Nordic");   // same definitions, a subset of the rows
Dashboard<Order> view = dashboard.ScopeTo(selections);                      // the rows the selections match, resolved by the facets
```

A scoped dashboard is the cheap way to show the same dashboard over a subset: one tab per region, one page per customer, one dashboard per tenant. It shares the parent's indexes and costs milliseconds, not a rebuild. It behaves exactly like a dashboard built with the predicate as a fixed filter: `TotalCount`, total counts, "Other" and metric shares are against the subset, and a value no row in the subset has is not listed. Range and date buckets stay the parent's, so every scope has the same axes. The scope is not a selection: nothing shows it and JSON does not carry it. The same `Selections` and the same `Serializer` work for every scope, and scopes compose. `ScopeTo(selections)` scopes in the facets' own terms, so the current view or a saved bookmark can become a dashboard of its own: it starts with nothing selected, its facets list only the values in scope, and a relative date preset is frozen at that moment. Give the scoped dashboard to `DashboardView` as its `Dashboard`; switching the parameter recalculates every component inside with the same selections. Keep the scoped dashboards you switch between, since each caches its own states.

## The components

All live inside `DashboardView<T>`, read the cascaded state and never count anything themselves.

| Component | Renders | Notable parameters |
|---|---|---|
| `DashboardView` | Owns selections and state, cascades them; its content is a template over the context. | `Dashboard`, `Context`, `@bind-Selections`, `StateChanged`, `Formatter`, `Key`, `SyncUrl` |
| `ValueFacet` | Values with counts, the null value, "Other", search. | `Key`, `Name`, `Sort` (`Rank`, `Label`, `Value`), `SortDescending`, `ShowTotals`, `HideZeroCounts`, `Collapsible`, `@bind-Collapsed`, `HeaderTemplate`, `ValueTemplate`, `InputClass` |
| `RangeFacet` | Fixed buckets as histogram or list, optional slider. | `Key`, `Name`, `Layout`, `ShowSlider`, `ShowSliderInputs`, `SliderStep`, `ShowBounds`, `InputClass` |
| `DateFacet` | Presets with counts, one bar per period. | `Key`, `Name`, `Layout`, `ShowPresets` |
| `TextFacet` | A debounced input; the text becomes a `TextSelection`. | `Key`, `Name`, `DebounceMilliseconds`, `Placeholder`, `InputClass` |
| `ActiveSelections` | One chip per facet with every part removable (a value, an interval, a preset, the null value), clear all. | `ShowFacetName`, `GroupValues` |
| `Metric` | One tile by key with its share of the total; a dash when there is no value. A `CountMetric` is the matching row count. | `Key`, `Name`, `MetricTemplate` |
| `StateSummary` | Everything in the state as plain clickable lists: counts, metrics, every facet. The default content of `DashboardView`, for a first look before laying out a page. | the texts |

- **Rows.** The library renders no rows; they go to the grid you already use. `Context="dash"` names the view's context in your markup and `dash.Items` is one `IQueryable<T>` per state: its reference changes exactly when the state does, so QuickGrid re-queries after every click and never in between. Behind it `state.Items` is a counted, indexable list, so paging, virtualising, sorting and counting cost the slice, not a pass over every row; a grid that takes a list gets `dash.State.Items`. The view re-renders its content after every click, so no callback is needed. Naming the context is required only when a template inside would otherwise reuse the implicit `context`.
- **Names.** The `Name` given in the builder is the default display name and travels with the state, so plain-C# consumers, `ActiveSelections` and `StateSummary` have a name for every key. Each facet component and `Metric` take a `Name` parameter that replaces it in that component only, for example with a localised string, so one dashboard serves every language.
- **Tile templates.** `MetricTemplate` on `Metric` receives a `MetricTileContent`: the formatted `Name`, `Value` and `Share` (null when the metric has none), `IsEmpty`, and the raw `Metric` state. The template replaces the whole tile: the library renders no wrapping element, so your markup is the root and carries its own classes and hooks (`Class` and extra attributes apply to the default tile only). A Bootstrap `card` or any framework tile therefore has nothing of the library's to override.
- **Formatting** goes through one `IDashboardFormatter` cascaded from `DashboardView`. Derive from `DefaultDashboardFormatter` to change culture, number formats, the null label, preset names or the order of labels when a facet sorts by label; culture enters the UI there and nowhere else, so the same page renders and sorts the same on every machine. Pass a fixed culture in tests.
- **Styling** is plain CSS. Every `--l2d-*` custom property is declared on `.l2d-dashboard`; override them on that element or an ancestor. Every component takes `Class` and passes unknown attributes to its root element. The text input, the value facet's search box and the range slider's number inputs take `InputClass`: when set, it replaces the library's default input look (`l2d-input`) with your classes, so `InputClass="form-control"` gives a Bootstrap input with nothing to override; the hook classes `l2d-text-input`, `l2d-facet-search` and `l2d-slider-input-from`/`-to` stay. State classes `l2d-selected`, `l2d-zero`, `l2d-null`, `l2d-collapsed` and `l2d-metric-empty` are stable hooks. In range and date facets the null bar is striped and set apart by a gap so it does not read as one more interval; `--l2d-bar-null` changes its fill.
- **Narrow histograms.** A histogram bar drops its label and count when its column is narrower than `4rem`, leaving the bar and its tooltip, because part of a number reads as a smaller number. The facet never widens the column you put it in, however many buckets it has. For readable labels in a narrow column use `Layout="BucketLayout.List"`, which gives one row per bucket.
- **Callbacks.** `SelectionsChanged` fires on every click, for bookmarking. `StateChanged` hands the host each new `DashboardState<T>`, the initial one included, for a chart of its own.
- **Selections in the URL.** `SyncUrl="true"` keeps the selections in the page URL in the query form above, restores them on load, and follows back, forward and links within the page. On first render the URL wins when it has selections for the view, and `SelectionsChanged` reports them; otherwise `Selections` applies and is written. Every change replaces the URL in place and keeps the page's other parameters. Give each view a `Key` when a page has two; its parameters are then `key.facet`. `Key` also renders as `data-key` on the root. Prerendering reads the URL and never writes it.
- **Hosting.** Blazor Server is the primary target. WebAssembly works unchanged within the browser's memory.

## Rules that surprise

These are decisions from the concept, not options.

- **OR within a facet, AND across facets.** Sweden or Norway, and status Open. The same for bars: January or March, and status Open.
- **A facet's own selection is excluded from its own counts.** Under Country, with Sweden selected, Norway still shows what selecting it would add.
- **Two counts per value.** `TotalCount` over the dataset, `FilteredCount` under the other facets' selections. Filtered counts always sum to the facet's `ContextCount`.
- **Null is a value.** It is listed, counted and selectable. Never drop it.
- **Zero-count values stay in the state.** Hiding them is the UI's choice (`HideZeroCounts`).
- **Ranking picks the values, the UI orders them.** `RankBy` in the builder decides which values Top N presents; `Sort` on `ValueFacet` decides the order on screen: by rank (default), label or value, optionally reversed. Null stays last.
- **Buckets are fixed at build.** Only their counts change. A bucket click toggles exactly its interval, so bars select like values: "below 100 or 1 000 and above" is one selection, and the null bar toggles beside them.
- **Searching within a facet is not a selection.** It narrows the list shown, nothing else.
- **Metrics skip null.** Averages divide by rows that have a value. Distinct counts non-null values.
- **The data is fixed at creation.** New data means a new dashboard; selections carry over through JSON. A subset of the data is not new data: `dashboard.ScopeTo(...)` scopes without a rebuild.
- **A text facet is the one expensive operation.** A new text calls your predicate once per row. Keep it cheap and pure; it may run on several threads.

## Mistakes to avoid

- Counting or filtering in the UI. Everything comes from the state.
- Creating a dashboard per request or per user. Build once; register a singleton. Per-user or per-tenant subsets are scopes of it, `dashboard.ScopeTo(...)`, kept and reused.
- Discarding the result of a `Selections` method. Every call returns a new instance. `==` compares two instances by value.
- Using a `TextFacet` to search a value list. `ValueFacet` with `Searchable()` does that without a scan.
- Mutating the source collection after `Create`. The dashboard indexed a snapshot.
- A `Key` that does not match the builder, or a facet component of the wrong kind for its key. Both throw at render time and name the key. Leaving out `T="Order"` is a compile error.
- Placing a component outside `DashboardView`. It throws on initialisation.
- Hard-coding colours or sizes in a stylesheet that targets the components' markup. Use the `--l2d-*` properties.
