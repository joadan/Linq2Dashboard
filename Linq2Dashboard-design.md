# Linq2Dashboard – Design

> Status: draft, being built iteratively. This document describes *how* the behaviour defined in
> [Linq2Dashboard-concept.md](Linq2Dashboard-concept.md) is realised: public API, types, internal data model,
> calculation, caching, project layout and benchmarks. Section references of the form §C4.2 point into the concept document.
>
> Where this document and the concept disagree, the concept wins and this document is wrong.

---

## 1. Design goals

- **One million rows, ten facets, one high-cardinality facet, interactive.** A full recalculation after a click should land well under 50 ms on a developer laptop. Building the dashboard may take a couple of seconds.
- **The engine is a pure function.** `Dashboard<T>` is immutable after construction. `Calculate(selections)` returns an immutable `DashboardState<T>`. Nothing about a dashboard changes when a user clicks (§C4.9).
- **Columnar only.** Every facet is stored as one integer code per row. Counting is a single pass over the rows in context. Per-value bitmaps are not part of the first version; they are a later optimisation to be justified by a benchmark scenario that needs them (§3.4).
- **No surprises at the boundary.** Everything the UI touches is a plain, serialisable, non-generic type.

---

## 2. Public API

### 2.1 Building a dashboard

```csharp
var dashboard = Dashboard.Create(orders, b =>
{
    b.Where(x => x.CompanyId == 42);                           // fixed filter (§C3)

    b.ValueFacet(x => x.Country)                               // key "Country"
     .Title("Country")
     .Top(20);

    b.ValueFacet("status", x => x.Status);                     // explicit key (§C7)

    b.ValueFacet(x => x.CustomerName)
     .Top(20)
     .RankBy(RankMode.TotalCount)                              // §C6
     .Searchable();

    b.BooleanFacet(x => x.IsActive);

    b.RangeFacet(x => x.Amount)
     .Buckets(0, 100, 500, 1000);                              // explicit boundaries, or .AutoBuckets(10)

    b.DateFacet(x => x.OrderDate)
     .TimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm"))
     .Granularity(DateGranularity.Month)
     .Presets(DatePreset.Today, DatePreset.Last7Days, DatePreset.ThisYear);

    b.Metric("orders",  Metric.Count());
    b.Metric("revenue", Metric.Sum(x => x.Amount));
    b.Metric("average", Metric.Average(x => x.Amount));

    b.OrderByDescending(x => x.OrderDate);                     // application-defined sort (§C8)

    b.TimeProvider(TimeProvider.System);                       // default; tests pass a fake
});
```

- `Dashboard.Create` enumerates the source exactly once, applies fixed filters, and builds every column and index. After it returns, the dashboard is immutable and thread-safe.
- Every builder method validates eagerly. Duplicate keys, a non-member selector without an explicit key, or a range facet over a non-numeric property throw at `Create`, not at first use.
- All facet builders return a typed builder so kind-specific options are discoverable; the shape above is the whole configuration surface for the first version.

### 2.2 Selections

`Selections` is an immutable, non-generic map from facet key to a selection. It is the only thing the UI sends back.

```csharp
var selections = Selections.Empty
    .With("Country", ValueSelection.Of("SE", "NO"))
    .With("Amount",  RangeSelection.Between(100, 500))
    .With("OrderDate", DateSelection.Preset(DatePreset.Last30Days));

// convenience for the click case
selections = selections.Toggle("Country", "DK");     // add if absent, remove if present
selections = selections.Clear("Amount");
```

Selection types (§C5):

```csharp
abstract record Selection;

sealed record ValueSelection(IReadOnlyList<object?> Values) : Selection;
//  null in Values selects the null facet value (§C4.8)

sealed record RangeSelection(
    double? From, double? To,
    bool FromInclusive = true, bool ToInclusive = true,
    bool IncludeNull = false) : Selection;
//  null bound = unbounded on that side

sealed record DateSelection : Selection
{
    // exactly one of these is set
    public DateTimeOffset? From { get; init; }   // inclusive
    public DateTimeOffset? To   { get; init; }   // exclusive
    public DatePreset?     Preset { get; init; } // resolved with TimeProvider at Calculate (§C5)
    public bool IncludeNull { get; init; }
}
```

Interval conventions:

- Numeric intervals default to closed on both ends because a slider showing 250 to 750 means both are included. A bucket click produces `[lo, hi)` by setting `ToInclusive = false`, so buckets `0-100` and `100-500` never both claim 100.
- Date intervals are always `[From, To)`. Calendar buckets are naturally half-open, and a UI that lets the user pick an inclusive end day sends the following midnight as `To`.

Values inside `ValueSelection` are the facet's real value type, boxed. A selection for an `int` facet holds boxed ints, not strings. Serialisation (§2.5) is where strings enter.

### 2.3 Calculating

```csharp
DashboardState<T> state = dashboard.Calculate(selections);
```

Pure, synchronous, thread-safe. Two calls with equal selections give equal states. The dashboard may cache internally (§5), but that is invisible.

### 2.4 Reading the state

```csharp
sealed class DashboardState<T>
{
    Selections Selections { get; }
    int TotalCount { get; }                 // dataset size after fixed filters
    int MatchingCount { get; }

    IReadOnlyList<FacetState> Facets { get; }
    FacetState Facet(string key);

    IReadOnlyList<MetricState> Metrics { get; }
    MetricState Metric(string key);

    ResultPage<T> GetPage(int pageIndex, int pageSize);
    IEnumerable<T> Items { get; }           // all matching rows in sort order, lazy; for export
}
```

Facet state is one abstract type with one subtype per kind, all non-generic:

```csharp
abstract class FacetState
{
    string Key { get; }
    string Title { get; }
    FacetKind Kind { get; }
    Selection? Selection { get; }
    int ContextCount { get; }               // rows in this facet's own counting context (§C4.2)
}

sealed class ValueFacetState : FacetState
{
    IReadOnlyList<FacetValue> Values { get; }   // presented values, in rank order, selected pinned (§C6)
    FacetCount? Other { get; }                  // present only when Top N truncated the list
    int DistinctCount { get; }                  // all values, including those not presented
    bool IsSearchable { get; }
    IReadOnlyList<FacetValue> Search(string text, int max = 20);   // §C4.5, UI operation
}

sealed class RangeFacetState : FacetState
{
    double Min { get; }  double Max { get; }    // dataset bounds, fixed (§C5)
    IReadOnlyList<Bucket> Buckets { get; }
    FacetCount Null { get; }
}

sealed class DateFacetState : FacetState
{
    DateGranularity Granularity { get; }
    IReadOnlyList<Bucket> Buckets { get; }      // one per period present in the dataset
    IReadOnlyList<PresetState> Presets { get; } // each resolved to its interval and counted
    FacetCount Null { get; }
}

sealed record FacetValue(object? Value, int TotalCount, int FilteredCount, bool Selected);
sealed record FacetCount(int TotalCount, int FilteredCount);
sealed record Bucket(double From, double To, string Label, int TotalCount, int FilteredCount, bool Selected);
sealed record MetricState(string Key, string Title, double? Value);   // null = no value (§C4.4)
sealed record ResultPage<T>(IReadOnlyList<T> Items, int PageIndex, int PageSize, int MatchingCount);
```

`Value` is `object?` on purpose. The UI formats it; the core does not know about cultures or labels. Boolean and custom facets reuse `ValueFacetState`.

### 2.5 Serialising selections

```csharp
string query = dashboard.Serializer.ToQueryString(selections);
//  Country=SE&Country=NO&Amount=100..500&OrderDate=last30days

Selections restored = dashboard.Serializer.FromQueryString(query);
```

- Each facet definition owns a `Format(object?) → string` and `Parse(string) → object?` pair for its value type. Defaults cover primitives, enums, strings, `Guid`, and the date types with invariant culture; the builder allows an override.
- Null is written as an empty segment (`Country=`). Range and date intervals use `from..to` with `[`/`(` prefixes only when a bound is non-default, so the common case reads cleanly.
- Unknown keys and unparseable values are dropped, not thrown. A stale bookmark should degrade to "fewer selections", never to an error page.
- A JSON form with the same semantics is provided for storage; the query form is for URLs.

---

## 3. Internal data model

### 3.1 Rows

```text
source  --Where(fixed filters)-->  T[] _items      row id = array index, 0..N-1
```

Rows that fail a fixed filter are dropped at build time, so ids are dense over the dataset and every count in the system is relative to the dataset (§C4.3). The dashboard keeps `_items` for paging and export only. Nothing else touches the objects after build.

### 3.2 Row sets

```csharp
sealed class RowSet          // fixed length N, immutable once built
{
    readonly ulong[] _words; // ceil(N / 64)
    int Count { get; }       // cached popcount
    RowSet And(RowSet other);
    RowSet Or(RowSet other);
    IEnumerable<int> Rows(); // ascending, via TrailingZeroCount
}
```

Uncompressed. At 1 M rows a set is 125 KB. AND over two sets is 15 625 word operations and vectorises. A compressed representation (Roaring) is a possible later swap behind this type; nothing outside `Indexing/` sees the words.

### 3.3 Columns

Every facet and every metric is materialised into a column at build. Selectors are compiled once and run once per row. After build, no expression is ever evaluated again.

**Value column** (value and boolean facets, also the bucket side of range and date facets):

```text
int[]     codes        one per row; 0 = null, 1..V = dictionary index + 1
TValue[]  dictionary   distinct non-null values, in first-seen order
string[]  labels       Format(dictionary[i]), built lazily for searchable facets
int[]     totalCounts  per code, computed once
```

- Equality of values uses `EqualityComparer<TValue>.Default` unless the builder is given a comparer. Case-insensitive strings are a builder option, not a default.
- Reading `codes[row]` in ascending row order is a sequential memory scan. This is what makes the counting pass in §4.3 fast regardless of cardinality.

**Range column**:

```text
double[]  values       converted once with generic math; NaN where null
RowSet    nulls
int[]     bucketCodes  0 = null, 1..B = bucket index + 1, fixed at build (§C5)
double    min, max     dataset bounds
```

Bounds arrive from `RangeSelection` as `double`. Comparisons happen in `double`. For `decimal` properties this is a deliberate precision trade: a UI slider does not carry more than double precision, and the converted values are used only for filtering and bucketing, never for metrics (which read their own column).

**Date column**:

```text
long[]    ticks        UTC ticks after conversion into the facet's zone rules (§C5); long.MinValue where null
RowSet    nulls
int[]     bucketCodes  period index at the configured granularity, 0 = null
long[]    bucketStarts start tick of each period present in the dataset
```

Period boundaries are computed in the facet's time zone once, at build. A preset such as `Last7Days` is resolved at `Calculate` by asking the `TimeProvider` for now, converting into the facet zone, and snapping to day boundaries in that zone.

**Metric column**:

```text
double[]  values       NaN where null
```

Count needs no column. Sum, average, min and max read their column over the matching set, skipping NaN (§C4.4).

**Sort order**:

```text
int[]  sortedRows      row ids ordered by the application-defined sort, computed once at build
```

If no sort is defined, this is the identity and is not allocated.

### 3.4 Cardinality

The first version uses one strategy for every value facet regardless of cardinality: the `codes` column, a scan to turn a selection into a row set, and a counting pass over the context rows. Cost is linear in the number of rows and independent of the number of distinct values.

| Facet | Storage at 1 M rows | Per calculation |
|---|---|---|
| Country, 20 values | 4 MB of codes | 21-entry counter array |
| Customer, 100 000 values | 4 MB of codes | 400 KB counter array, partial sort for top N |

Per-value bitmaps (one `RowSet` per distinct value, selection as an OR of sets, counting as popcount of AND) are a known later optimisation for low-cardinality facets with frequent selection changes. They cost `V × N / 8` bytes per facet and would be gated by a cardinality threshold. They are deliberately left out until the benchmark suite (§8) shows a scenario where the columnar path misses its target. Nothing outside `Indexing/` depends on which strategy a facet uses, so adding them later is local.

---

## 4. Calculation

`Calculate(selections)` does the following, in order. Nothing here mutates the dashboard.

### 4.1 Selection row sets

For each facet `f` with a non-empty selection, produce `R_f`:

- Value: scan `codes` and set a bit where `codes[row]` is in the selected code set. The selected codes are looked up in a `bool[V + 1]` mask built once per selection, so the inner loop is one array read and one branch per row.
- Range: scan `values`; set a bit where the value is inside the interval. OR in `nulls` if `IncludeNull`.
- Date: resolve preset to `[from, to)` ticks if needed; scan `ticks`. OR in `nulls` if `IncludeNull`.

Each `R_f` is cached by `(facetKey, selection)` (§5). The scans are `O(N)` with sequential access, roughly 1 ms per million rows.

### 4.2 Matching set and per-facet contexts

Let the facets with selections be `R_1 … R_k`.

```text
M    = R_1 AND R_2 AND … AND R_k            (the full dataset set if k = 0)

C_f  = AND of all R_g with g ≠ f            for a facet f with a selection
C_f  = M                                    for a facet f without a selection
```

Computing every `C_f` naively costs `O(k²)` ANDs. Instead:

```text
prefix[i] = R_1 AND … AND R_{i-1}
suffix[i] = R_{i+1} AND … AND R_k
C_i       = prefix[i] AND suffix[i]
```

That is `3k` ANDs in total, each 15 625 words at 1 M rows. For ten selected facets this is under a millisecond.

### 4.3 Counting

For each facet `f`, with context `C_f` and its `codes` (or `bucketCodes`) column:

```csharp
Span<int> counts = stackalloc or pooled, length V + 1, zeroed;
foreach (int row in C_f.Rows())
    counts[codes[row]]++;
```

One sequential pass over the rows in context. Cost is proportional to `|C_f|`, not to `V`.

`counts[0]` is the null value's filtered count (§C4.8). Total counts come from the column and are never recomputed.

Facets are independent at this stage and may be counted in parallel. Parallelism is a builder option, off by default until benchmarks say otherwise.

### 4.4 Presentation of a value facet

From `counts`, build the presented list (§C6):

1. Start with every selected value, regardless of rank.
2. Fill up to `N` with the highest-ranked remaining values, ranking by filtered count or total count per the facet's `RankMode`. Zero-count values are eligible and are included if they rank (§C4.3).
3. Ties break by total count, then by dictionary order, so the order is deterministic.
4. `Other.FilteredCount = |C_f| − Σ presented filtered`, `Other.TotalCount = N_dataset − Σ presented total`. Omitted when nothing was truncated.

Selecting the top `N` from `V = 100 000` counts is a partial sort, `O(V)` expected. The full `counts` array is retained inside the state to serve `Search` without recounting.

### 4.5 Range and date facets

Buckets are counted through `bucketCodes` exactly like value facets. Each `Bucket` gets `Selected = true` when the current interval fully covers it. `Null` reports `counts[0]`. For date facets, each configured preset is resolved and counted against `C_f` as well, so the UI can show "Last 7 days (312)" without a round trip.

### 4.6 Metrics

One pass over `M.Rows()` per metric column, accumulating sum, count-of-values, min and max in a single loop when several metrics share a column. Count is `M.Count`. A metric with zero contributing values reports `null` (§C4.4).

### 4.7 Result page

```csharp
ResultPage<T> GetPage(int pageIndex, int pageSize)
```

Walk `sortedRows` in order, test membership in `M`, skip `pageIndex × pageSize` hits, take `pageSize`. Worst case one pass over `N` bit tests, about 1 ms at 1 M rows. Page requests are answered from the state without touching the dashboard. `Items` does the same walk lazily without skipping.

When `sortedRows` is identity, `M.Rows()` is enumerated directly instead.

### 4.8 Cost model at the target

For 1 M rows, ten facets, three of them with selections:

| Step | Work | Estimate |
|---|---|---|
| Selection row sets (3, cache-miss) | 3 sequential scans | ~3 ms |
| Contexts | ≤ 9 ANDs of 15 625 words | < 1 ms |
| Counting (10 facets) | ≤ 10 passes over ≤ 1 M rows | ~10–20 ms |
| Top N + Other | partial sorts | < 1 ms |
| Metrics (3, one column) | 1 pass over M | ~1 ms |
| First page | 1 walk | ~1 ms |

Roughly 20 to 30 ms single-threaded, before any parallel gains. This is the number the benchmark suite must confirm or refute.

---

## 5. Caching and concurrency

- **Immutable after build:** `_items`, all columns, all indexes, total counts, `sortedRows`. No locks needed to read.
- **Selection row-set cache:** `(facetKey, Selection) → RowSet`, bounded LRU, default 256 entries, inside the dashboard. Selections are records with value equality, so they are their own cache keys. Toggling a value in one facet re-uses every other facet's cached set.
- **State cache:** `Selections → DashboardState<T>`, bounded LRU, default 8 entries. Covers back/forward and "undo last click" for free.
- Both caches are safe for concurrent readers and writers. A miss computed twice is harmless because results are immutable and equal.
- **States are immutable** and hold their own arrays. Rendering one state while the next is calculated is safe. A state keeps a reference to the dashboard for `GetPage` and `Search`; it never mutates it.
- **Scratch memory** during `Calculate` comes from `ArrayPool<T>` and is returned before the state is published. Arrays that the state keeps (`counts` for searchable facets, `M`) are allocated for it.

---

## 6. Extensibility: custom facets

The four built-in kinds are implemented against one internal interface. It is exposed so a custom facet (§C5) can be written without touching the core:

```csharp
public interface IFacetDefinition<T>
{
    string Key { get; }
    string Title { get; }
    FacetKind Kind { get; }
    IFacetIndex<T> Build(ReadOnlySpan<T> rows);        // once, at Create
}

public interface IFacetIndex<T>
{
    RowSet RowsMatching(Selection selection);           // §4.1
    FacetState Present(RowSet context, Selection? selection);   // §4.3–4.5
    string Format(object? value);                        // §2.5
    object? Parse(string text);
}
```

A custom facet gets the same context set as everyone else and is subject to every rule in §C4. The first version ships the interface and the four built-ins; it does not promise API stability for the interface until a second custom facet exists outside the repo.

---

## 7. Projects and layout

```text
Linq2Dashboard.slnx
src/
    Linq2Dashboard/                     core, package id Linq2Dashboard, net10.0, no dependencies
        Dashboard.cs                    Dashboard.Create, Dashboard<T>
        DashboardBuilder.cs
        DashboardState.cs
        Selections.cs                   Selections, Selection records
        Facets/                         definitions, builders, FacetState types
        Metrics/
        Indexing/                       RowSet, columns, caches (internal)
        Serialization/
    Linq2Dashboard.Blazor/              Razor class library, depends on core only
        Dashboard.razor
        Facets/  Metrics/  Results/
tests/
    Linq2Dashboard.Tests/               xUnit; rules in §C4 each get a named test
benchmarks/
    Linq2Dashboard.Benchmarks/          BenchmarkDotNet, see §8
```

The existing `Linq2Dashboard.Core` project is renamed to `Linq2Dashboard` and moved under `src/` so that project, package and root namespace agree.

### Blazor flow

```text
User clicks "Sweden"
    → component: selections = selections.Toggle("Country", "SE")
    → state = dashboard.Calculate(selections)
    → StateHasChanged()
```

The component owns `Selections` and the current `DashboardState<T>`. `Calculate` runs inline. At the target cost (§4.8) that is acceptable on Blazor Server. On WebAssembly a million rows in the browser is a memory question before it is a speed question, and is not a first-version target.

---

## 8. Benchmark plan

The benchmark project is part of the first version, not an afterthought. It generates a deterministic dataset and measures every step in §4.

**Dataset** (seeded generator, 1 000 000 rows):

| Facet | Kind | Cardinality |
|---|---|---|
| Country | value | 20 |
| Status | value | 5 |
| Category | value | 200 |
| Brand | value | 2 000 |
| Customer | value, searchable, top 20 | 100 000 |
| IsActive | boolean | 2 + null |
| Amount | range, 8 buckets | continuous, 5 % null |
| OrderDate | date, month | 3 years, 2 % null |

**Scenarios**:

1. `Create` from an in-memory list.
2. `Calculate` with no selections, cold and warm cache.
3. `Calculate` after toggling one value in Country (1, 3, 5 active facets).
4. `Calculate` with a range and a date interval active.
5. `Search("acme")` on Customer.
6. `GetPage` first, middle and last page.
7. Everything above with parallel counting on and off.

**Targets**: `Create` under 2 s. Warm `Calculate` under 50 ms in every scenario. Peak managed memory reported per facet kind. If a scenario misses its target, the per-facet timings say whether per-value bitmaps (§3.4) would help before any are added.

---

## 9. Open questions

1. **Value equality.** Default comparer with an opt-in for case-insensitive strings is the proposal. Should string facets be case-insensitive by default instead?
2. **Parallel counting default.** Off by default is the proposal until the benchmarks show a clear win on a typical server core count.
3. **Project rename.** `Linq2Dashboard.Core` to `Linq2Dashboard` under `src/`. Any reason to keep the `.Core` suffix?
4. **Selection serializer shape.** Query string plus JSON as proposed, or JSON only for the first version?
5. **Custom facet interface exposure.** Public from day one as proposed, or internal until the built-ins have settled?

### Decided

- **Columnar only in the first version.** No per-value bitmaps. Same strategy for every cardinality; bitmaps are a later, benchmark-justified addition. See §1, §3.4, §4.1.
- **Range bounds are `double`.** The precision trade for `decimal` properties is accepted; range values are used only for filtering and bucketing, never for metrics. See §2.2, §3.3.
- **The dashboard is stateless.** `Calculate(selections)` is the only entry point; the UI owns the current `Selections`. See §2.3, §7.
