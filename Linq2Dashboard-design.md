# Linq2Dashboard

## Overview

**Linq2Dashboard** is a .NET library for exploring large in-memory collections through filtering, faceting, aggregation, grouping, and interactive dashboard state.

The initial target is approximately **1 million rows of flattened objects**, with frequent filter changes and recalculation of facet counts.

The library is inspired by LINQ and is intended to fit alongside existing projects such as:

- `Linq2OData`
- `Linq2GraphQL`

The core library should be completely independent of Blazor or any other UI technology.

A separate **Linq2Dashboard.Blazor** package provides the UI layer.

---

# Goals

## Primary goals

- Accept an `IEnumerable<T>` as the source.
- Efficiently work with large in-memory datasets.
- Support multiple facet types.
- Support interactive filtering.
- Calculate both total and filtered facet counts.
- Recalculate all facets when filters change.
- Avoid repeatedly scanning the complete dataset for every facet.
- Provide a clean API based on expressions and LINQ concepts.
- Keep the core engine independent from UI frameworks.
- Provide a reusable Blazor component on top of the engine.

## Non-goals

The project is not intended to become a full BI platform.

It should focus on:

> Efficient interactive exploration of large in-memory collections.

---

# High-level architecture

```text
                     Linq2Dashboard
                            |
                    +-------+-------+
                    |               |
              Dashboard<T>      Indexes
                    |               |
                    +-------+-------+
                            |
                    DashboardState
                            |
                            v
                  Linq2Dashboard.Blazor
                            |
              +-------------+-------------+
              |             |             |
           Facets         Metrics       Results
              |             |             |
          Filter UI      KPI UI        Grid/List
```

The core engine owns the meaning and calculation of the dashboard.

The Blazor layer owns presentation and user interaction.

---

# Core API

A typical usage should look approximately like:

```csharp
var dashboard = new Dashboard<Order>(orders);

dashboard
    .AddFacet(x => x.Country)
    .AddFacet(x => x.Status)
    .AddRangeFacet(x => x.Amount)
    .AddDateFacet(x => x.OrderDate);
```

Filters can then be applied:

```csharp
dashboard.SetFilter(countryFacet, "SE");
dashboard.SetFilter(statusFacet, "Open");

var state = dashboard.Calculate();
```

The exact API is intentionally not fixed yet.

---

# Why IEnumerable<T>

The source API should be:

```csharp
IEnumerable<T>
```

rather than `IQueryable<T>`.

The data is explicitly assumed to be in memory.

`IQueryable<T>` does not provide a meaningful performance advantage for this scenario and introduces semantics associated with query providers and expression translation.

The dashboard should instead own an internal optimized representation of the data.

The source may be materialized once:

```csharp
_items = source.ToArray();
```

or another compact representation may be created.

This is particularly important because the same source may otherwise be enumerated many times during facet calculations.

---

# Performance model

The target use case is:

```text
1,000,000 rows
    |
    +-- Country facet
    +-- Category facet
    +-- Brand facet
    +-- Status facet
    +-- Price facet
    +-- Date facet
    +-- ...
```

A naive implementation could perform a million-row scan for every facet whenever a filter changes.

That does not scale well.

The engine should instead build indexes that allow filtering and aggregation to operate on compact sets of row identifiers.

## Row IDs

Each source object can receive an integer row ID:

```text
0
1
2
...
999999
```

Indexes can then refer to rows rather than repeatedly evaluating object predicates.

## Bitsets / bitmaps

A value facet can maintain a bitmap for each value:

```text
Country

Sweden  -> bitmap
Norway  -> bitmap
Denmark -> bitmap
```

A selection such as:

```text
Country = Sweden
```

becomes a bitmap operation.

Multiple filters can be combined using AND/OR operations.

This is potentially much cheaper than evaluating predicates against every object repeatedly.

The exact bitmap implementation should remain an internal implementation detail.

---

# Facets

A facet represents one dimension by which the user can explore the data.

The core should support different facet types rather than forcing everything into a single value-based model.

Potential facet types include:

## Value facet

```csharp
dashboard.AddFacet(x => x.Country);
```

Example:

```text
Country

Sweden       4200
Norway       1800
Denmark      1100
Germany       950
```

A value facet normally supports multi-selection.

Within one facet:

```text
Sweden OR Norway
```

Across facets:

```text
(Sweden OR Norway)
AND
(Open OR Pending)
```

---

## Range facet

For numeric values:

```csharp
dashboard.AddRangeFacet(x => x.Amount);
```

Possible UI:

```text
Amount

0 - 100          124
100 - 500        352
500 - 1000       218
1000+             76
```

It can also support a continuous range:

```text
Amount
[ 250 ---------------- 750 ]
```

The engine should support range filtering without requiring a bucketed UI.

---

## Date facet

Dates deserve specialized handling.

Possible options:

```text
Today
Yesterday
Last 7 days
Last 30 days
This year
```

or time buckets:

```text
2026
  Jan
  Feb
  Mar
  Apr
```

Date facets may also support arbitrary date ranges.

---

## Boolean facet

Example:

```text
Active

Yes       4212
No         891
```

This can be implemented as a specialized value facet but may deserve a specialized API/UI.

---

## Custom facet

The architecture should allow custom facet implementations.

A custom facet should be able to define:

- how values are represented
- how selections are represented
- how selections filter rows
- how values/counts are calculated
- what information is exposed to the UI

---

# Facet counts

Facet counts are one of the central features.

For every facet value, there should normally be:

```text
TotalCount
FilteredCount
Selected
```

For example:

```text
Country

              Total      Filtered

Sweden         4200        1250
Norway         1800         780
Denmark        1100         420
```

## Total count

The total count is calculated against the original dataset.

It does not change when the user applies filters.

Therefore it can normally be calculated once when the dashboard/index is built.

## Filtered count

The filtered count is calculated using the current filters.

However, when calculating a facet, the facet's own filter must normally be excluded.

For example:

```text
Active filters:

Country = Sweden
Status = Open
Brand = ACME
```

For the Status facet:

```text
Country = Sweden
AND
Brand = ACME
```

The Status filter itself is excluded.

For the Country facet:

```text
Status = Open
AND
Brand = ACME
```

The Country filter itself is excluded.

This is what gives a normal faceted-search experience.

---

# Filter model

Filters should be separate from facet definitions.

A facet definition is relatively static:

```text
Country
  selector: Order.Country
  type: Value
```

Filter state is dynamic:

```text
Country = Sweden
Status = Open
```

This separation is important for both the engine and the Blazor layer.

Potential concepts:

```text
FacetDefinition
FacetSelection
FilterState
DashboardState
```

---

# Fixed filters vs interactive filters

It may be useful to support filters that are always applied and filters controlled by the user.

Example:

```text
Fixed filter:
    CompanyId == 42

Interactive filters:
    Country = Sweden
    Status = Open
```

This allows an application to scope a dashboard before exposing it to the user.

Potential terminology:

- Fixed filters / User filters
- Base filters / Interactive filters

The exact naming can be decided later.

---

# Aggregations and metrics

The same filtered row representation used by facets can support metrics.

Examples:

```csharp
dashboard.AddMetric("Orders", Aggregation.Count);

dashboard.AddMetric(
    "Revenue",
    x => x.Amount,
    Aggregation.Sum);

dashboard.AddMetric(
    "Average order",
    x => x.Amount,
    Aggregation.Average);
```

Potential aggregations:

- Count
- Sum
- Average
- Minimum
- Maximum
- Distinct count

Metrics should automatically reflect the current dashboard filters.

Example:

```text
Orders          12,483
Revenue       4.82 M
Average          386
```

---

# Grouping

Grouping can provide a more analytical view of the data.

Example:

```text
Country

Sweden
    Orders: 4213
    Revenue: 1.8 M

Germany
    Orders: 2982
    Revenue: 1.2 M

Norway
    Orders: 1842
    Revenue: 0.7 M
```

Potential future support:

- Group by value
- Hierarchical grouping
- Group + aggregation
- Group + sorting
- Top N groups

---

# Sorting

Sorting should apply both to result data and facet values.

Facet values might be sorted:

```text
By value
    Denmark
    Germany
    Norway
    Sweden
```

or:

```text
By count
    Sweden
    Germany
    Norway
    Denmark
```

Potential API:

```csharp
facet.SortByValue();
facet.SortByCount();
```

---

# Top N and Other

High-cardinality facets such as Customer may contain tens or hundreds of thousands of values.

The UI should not attempt to display all values.

Support concepts such as:

```csharp
dashboard.AddFacet(x => x.Customer)
         .Top(20);
```

Example:

```text
Customer

ACME             2421
Siemens          1982
Volvo            1754
...

Other            87421
```

The engine should distinguish between:

- all facet values
- values currently displayed
- search within facet

---

# Search within facets

High-cardinality facets should support searching.

Example:

```text
Customer
[ Search customer ]

ACME Sweden       124
ACME Germany       82
ACME Norway        51
```

Searching within the facet should normally be treated as a UI operation and not necessarily as a dashboard filter.

This distinction should be maintained.

---

# Date and numeric histograms

A facet can expose buckets that a Blazor component can render as a histogram.

For example:

```text
Price

       █
       █
   █   █
 █ █ █ █ █
──────────────
```

The core engine can provide bucket information without knowing how the chart is rendered.

This keeps visualization concerns out of the core library.

---

# Result data

The dashboard should expose filtered results.

For large datasets, the result API should support paging.

Example:

```csharp
var page = dashboard.GetPage(
    page: 2,
    pageSize: 50);
```

The browser should not receive all matching objects when only one page is needed.

Conceptually:

```text
1,000,000 source rows
        |
        v
37,421 matching rows
        |
        v
50 rows for current page
        |
        v
Blazor
```

Virtualization can later be supported by the Blazor layer.

---

# Selection vs filtering

Selection and filtering should be separate concepts.

Filtering:

```text
Country = Sweden
```

Row selection:

```text
Rows:
123
456
789
```

A future grid integration may use selection for:

- bulk operations
- export
- actions
- navigation

The core model should not assume that selected rows are filtered rows.

---

# Dashboard state

The engine should ideally produce a snapshot/state object.

Conceptually:

```csharp
DashboardState<T>
{
    TotalCount,
    FilteredCount,
    Items,
    Facets,
    Metrics
}
```

The state represents the calculated dashboard at a particular point in time.

An immutable snapshot would make the boundary between engine and UI particularly clean.

---

# Blazor layer

The Blazor package should be separate:

```text
Linq2Dashboard
Linq2Dashboard.Blazor
```

The core package should have no dependency on Blazor.

The Blazor package is responsible for:

- rendering facets
- rendering filter controls
- handling user interaction
- rendering metrics
- rendering result data
- paging/virtualization
- display formatting
- layout
- UI-specific state

It should consume the state produced by the core engine.

---

# Blazor component

The intended high-level API could look like:

```razor
<Linq2Dashboard Items="@orders">

    <Facets>
        ...
    </Facets>

</Linq2Dashboard>
```

Or the component could receive an already configured dashboard:

```razor
<Linq2Dashboard Dashboard="@dashboard" />
```

The final API is still to be decided.

The important architectural rule is that the Blazor component should not implement the faceting algorithms itself.

---

# UI facet model

The engine's internal facet types may be strongly typed:

```text
ValueFacet<T, TValue>
RangeFacet<T, TValue>
DateRangeFacet<T>
```

The Blazor layer should not need to know about expressions, indexes, or bitmap implementations.

Instead, it should consume a UI-friendly representation such as:

```text
FacetState

Id
Title
Type
Values
Selection
Configuration
```

For example:

```text
Range facet

Type = Range
Min = 0
Max = 10000
SelectedMin = 500
SelectedMax = 2500
```

This creates a clean boundary between calculation and presentation.

---

# Rendering customization

The Blazor package should provide sensible default renderers but allow applications to customize the UI.

Potential customization points:

- facet templates
- value templates
- range templates
- metric templates
- result templates
- empty states
- loading states

For example:

```razor
<FacetTemplate Context="facet">
    ...
</FacetTemplate>
```

The exact templating API should be designed after the basic component is working.

---

# Blazor state flow

A typical interaction should look like:

```text
User clicks "Sweden"
        |
        v
Blazor component
        |
        v
Dashboard.SetFilter(...)
        |
        v
Core engine recalculates
        |
        v
DashboardState
        |
        v
Blazor StateHasChanged()
        |
        v
Updated UI
```

The UI should not independently calculate facet counts.

---

# Performance considerations

The million-row target should influence the core architecture from the beginning.

Avoid this pattern:

```csharp
foreach (var facet in facets)
{
    var values = items
        .Where(...)
        .GroupBy(...)
        .ToList();
}
```

when it means scanning one million objects repeatedly.

Prefer:

```text
Source objects
     |
     v
Row IDs / compact storage
     |
     +---- facet indexes
     |
     +---- numeric/date indexes
     |
     v
Current matching row set
     |
     +---- facet counts
     +---- metrics
     +---- result page
```

Potential optimizations:

- materialize source once
- assign integer row IDs
- dictionary indexes for value facets
- bitmap/bitset row sets
- sorted indexes for numeric/date ranges
- cached filter combinations
- reuse intermediate row sets
- precompute immutable total counts
- avoid allocations during repeated calculations
- return only requested result pages

The implementation should be benchmarked with realistic datasets rather than optimizing prematurely around a specific data structure.

---

# Caching

Users often make incremental filter changes:

```text
{}
{Country=SE}
{Country=SE, Status=Open}
{Country=SE, Status=Completed}
```

Caching calculated row sets or intermediate results may significantly improve interactive performance.

Potential cache key:

```text
FilterState -> RowSet
```

The cache should have clear invalidation rules.

Because the source is assumed to be an in-memory snapshot, the simplest initial implementation may treat the source as immutable for the lifetime of the dashboard.

---

# Concurrency

The engine should ideally not require synchronization for every row operation.

A useful model is:

- immutable source/indexes
- mutable filter state
- calculation produces a new state/snapshot

This may also make it possible to calculate a new state without mutating the state currently being displayed by Blazor.

Concurrency requirements should be kept modest initially.

---

# Package structure

Possible structure:

```text
Linq2Dashboard/
    Dashboard.cs
    DashboardState.cs
    Facets/
        Facet.cs
        ValueFacet.cs
        RangeFacet.cs
        DateFacet.cs
    Filters/
    Aggregations/
    Indexing/
    Results/

Linq2Dashboard.Blazor/
    Linq2Dashboard.razor
    Facets/
        ValueFacet.razor
        RangeFacet.razor
        DateFacet.razor
    Metrics/
    Results/
```

The exact organization can evolve.

---

# Naming

The existing family strongly supports the name:

```text
Linq2OData
Linq2GraphQL
Linq2Dashboard
```

`Linq2Dashboard` can be interpreted as applying LINQ concepts to a dashboard/faceted exploration problem.

The Blazor package could be:

```text
Linq2Dashboard.Blazor
```

This keeps the core package UI-independent and makes the technology dependency explicit.

---

# Possible future functionality

The architecture should leave room for:

- saved filter states
- named views
- bookmarks
- export
- CSV/Excel export
- chart data
- cross-filtering
- hierarchical facets
- distinct counts
- percentage-of-total metrics
- calculated metrics
- custom aggregations
- drill-down
- row selection
- bulk actions
- server-side/remote data adapters in a separate package
- other UI adapters

These should not be required for the initial implementation.

---

# Suggested initial scope

A sensible first version could contain:

## Core

1. `Dashboard<T>`
2. `IEnumerable<T>` input
3. materialized immutable source
4. value facets
5. numeric range facets
6. date range facets
7. multi-select filters
8. total + filtered counts
9. filtered result set
10. paging
11. basic count/sum/average metrics
12. basic indexing
13. benchmark suite

## Blazor

1. main dashboard component
2. value facet component
3. range facet component
4. date facet component
5. result list/grid integration
6. metric display
7. paging
8. customizable templates
9. basic responsive layout

Then optimize the indexing implementation based on benchmarks.

---

# Core design principle

The most important architectural principle is:

> **The core library understands data, filters, facets, aggregations and state. The Blazor library understands presentation and interaction.**

The core should be useful without Blazor.

The Blazor layer should be replaceable without changing the core engine.

This makes `Linq2Dashboard` both a useful .NET library in its own right and a strong foundation for a reusable Blazor dashboard component.
