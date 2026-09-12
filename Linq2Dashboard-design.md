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

    b.Count("orders");
    b.Sum("revenue", x => x.Amount);
    b.Average("average", x => x.Amount).Title("Average order");

    b.OrderByDescending(x => x.OrderDate)                      // application-defined sort (§C8)
     .ThenBy(x => x.Id);

    b.UseTimeProvider(TimeProvider.System);                    // default; tests pass a fake
});
```

- `Dashboard.Create` enumerates the source exactly once, applies fixed filters, and builds every column and index. After it returns, the dashboard is immutable and thread-safe.
- Every builder method validates eagerly. Duplicate keys, a non-member selector without an explicit key, or a range facet over a non-numeric property throw at `Create`, not at first use. The builder and every facet builder refuse further configuration once the dashboard is built.
- Metrics are builder methods (`Count`, `Sum`, `Average`, `Min`, `Max`) rather than a separate `Metric` factory, because C# cannot infer `T` for `Metric.Sum(x => x.Amount)` outside the builder.
- Range and metric selectors accept any numeric property, nullable or not; the conversion to `double` is compiled into the selector. Date selectors accept `DateTime`, `DateTimeOffset`, `DateOnly` and their nullable forms; anything else is rejected at `Create`.
- `Buckets(100, 500, 1000)` names cut points, not edges: it yields "below 100", "100 to 500", "500 to 1000" and "1000 and above", so every value lands in a bucket.
- All facet builders return a typed builder so kind-specific options are discoverable; the shape above is the whole configuration surface for the first version.

### 2.2 Selections

`Selections` is an immutable, non-generic map from facet key to a selection. It is the only thing the UI sends back.

```csharp
var selections = Selections.Empty
    .With("Country", ValueSelection.Of("SE", "NO"))
    .With("Amount",  RangeSelection.Between(100, 500))
    .With("OrderDate", DateSelection.Relative(DatePreset.Last30Days));

// convenience for the click case
selections = selections.Toggle("Country", "DK");     // add if absent, remove if present
selections = selections.Clear("Amount");
```

Selection types (§C5):

```csharp
abstract record Selection;

sealed record ValueSelection : Selection            // ValueSelection.Of("SE", null)
{
    IReadOnlyList<object?> Values;                  // a set: order does not matter for equality
    //  null in Values selects the null facet value (§C4.8)
}

sealed record RangeSelection(
    double? From, double? To,
    bool FromInclusive = true, bool ToInclusive = true,
    bool IncludeNull = false) : Selection;
//  null bound = unbounded on that side
//  RangeSelection.Between / AtLeast / AtMost; RangeSelection.OnlyNull selects the null rows alone

sealed record DateSelection : Selection
{
    // exactly one form: absolute (From/To) or relative (Preset); built via factories
    DateTimeOffset? From;    // inclusive        DateSelection.Between(from, to)
    DateTimeOffset? To;      // exclusive
    DatePreset?     Preset;  // resolved with TimeProvider at Calculate (§C5)   DateSelection.Relative(preset)
    bool IncludeNull;
    //  DateSelection.OnlyNull selects the null rows alone
}
```

`Selections` is keyed by facet key, compares by value, and treats an empty `ValueSelection` as "clear". `Toggle` uses default equality on the boxed value; the facet's comparer applies when values are mapped to codes, so `"se"` and `"SE"` may both sit in a selection and still select the same rows.

Selection values reaching a value facet are brought to the facet's value type: an exact type match passes through, and primitives, decimals, strings and enums are converted, so a value that arrived as `long` or as a string from JSON still selects an `int` or enum facet value. A value that does not occur in the dataset selects nothing rather than failing (a stale bookmark degrades to fewer rows). A value that cannot be converted is an error.

Interval conventions:

- Numeric intervals default to closed on both ends because a slider showing 250 to 750 means both are included. A bucket click produces `[lo, hi)` by setting `ToInclusive = false`, so buckets `0-100` and `100-500` never both claim 100.
- Date intervals are always `[From, To)`. Calendar buckets are naturally half-open, and a UI that lets the user pick an inclusive end day sends the following midnight as `To`.

Values inside `ValueSelection` are the facet's real value type, boxed. A selection for an `int` facet holds boxed ints, not strings. Serialisation (§2.5) is where strings enter.

### 2.3 Calculating

```csharp
DashboardState<T> state = dashboard.Calculate(selections);
DashboardState<T> initial = dashboard.Calculate();          // nothing selected
```

Pure, synchronous, thread-safe. Two calls with equal selections give equal states; recent ones return the same instance from the state cache (§5). A selection for a facet key the dashboard does not have is an error here, unlike in the serializer (§2.5), which drops it. Strict in code, lenient at the boundary where stale data arrives.

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
    IReadOnlyList<RangeBucket> Buckets { get; } // fixed at build; only counts change
    FacetValue Null { get; }                    // the null value beside the buckets (§C4.8)
}

sealed class DateFacetState : FacetState
{
    DateGranularity Granularity { get; }  TimeZoneInfo TimeZone { get; }
    IReadOnlyList<DateBucket> Buckets { get; }  // one per period present in the dataset
    IReadOnlyList<PresetState> Presets { get; } // each resolved to its interval as of this calculation and counted
    FacetValue Null { get; }
}

sealed record FacetValue(object? Value, int TotalCount, int FilteredCount, bool Selected);
sealed record FacetCount(int TotalCount, int FilteredCount);                       // "Other"
sealed record RangeBucket(double From, double To, int TotalCount, int FilteredCount, bool Selected)
    { RangeSelection ToSelection(); }                                                // [From, To), last bucket closed
sealed record DateBucket(DateTimeOffset From, DateTimeOffset To, DateTime PeriodStart, int TotalCount, int FilteredCount, bool Selected)
    { DateSelection ToSelection(); }                                                 // [From, To) instants; PeriodStart local, for labels
sealed record PresetState(DatePreset Preset, DateTimeOffset From, DateTimeOffset To, int TotalCount, int FilteredCount, bool Selected);
sealed record MetricState(string Key, string Title, Aggregation Aggregation, double? Value);   // null = no value (§C4.4)
sealed record ResultPage<T>(IReadOnlyList<T> Items, int PageIndex, int PageSize, int MatchingCount);
```

`Value` is `object?` on purpose. The UI formats it; the core does not know about cultures or labels, which is also why buckets carry bounds rather than label strings. Boolean facets reuse `ValueFacetState`. `FacetKind` has `Value`, `Boolean`, `Range` and `Date` in the first version.

A bucket is `Selected` when the current interval fully covers it, so a wide interval lights up several buckets and a partial one lights up none. A preset is `Selected` when the selection is that preset or an absolute interval exactly equal to the preset's interval (clicking the March bar lights "This month"); coverage would light every preset inside a wide selection, which reads wrong. `ToSelection()` on a bucket gives exactly the selection a click should produce, so the UI never constructs interval bounds itself.

### 2.5 Serialising selections

```csharp
string json = dashboard.Serializer.ToJson(selections);
Selections restored = dashboard.Serializer.FromJson(json);
```

```json
{
  "Country":   { "values": ["SE", "NO"] },
  "Amount":    { "from": 100, "to": 500, "toInclusive": false },
  "OrderDate": { "preset": "last30Days" },
  "Shipped":   { "from": "2026-03-01T00:00:00.0000000+01:00", "includeNull": true },
  "Discount":  { "onlyNull": true },
  "status":    { "values": [null, "Open"] }
}
```

- JSON is the only format in the first version. Applications that want selections in a URL encode the JSON themselves; a dedicated query-string format can be added later without changing the JSON.
- Each facet writes and reads its own shape. Value facets write values as JSON primitives: strings, booleans and numbers as themselves, enums by name, `Guid` and the date and time types as ISO 8601 strings. Reading brings a primitive back to the facet's value type, so `"5"` or `5.0` reads into an `int` facet and `"store"` into an enum facet; booleans and numbers do not convert into each other. The builder's `Serialize(format, parse)` replaces the default with an application-supplied string form for a facet.
- Null in a value selection is JSON `null`. Omitted interval bounds mean unbounded. Omitted flags take the defaults from §2.2, so the common case reads cleanly. Presets are written in camelCase and read case-insensitively. Instants keep their offset.
- Unknown facet keys, values that cannot be read, and shapes that do not fit the facet's kind are dropped, not thrown. A stale bookmark degrades to "fewer selections", never to an error page. Only text that is not JSON at all throws.
- The serializer is built by the dashboard because parsing needs each facet's value type. It is otherwise stateless and exposed as `dashboard.Serializer`. `ToJsonObject` and `FromJsonObject` work on `System.Text.Json.Nodes` for callers that embed selections in a larger document.

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

- Equality of values uses `EqualityComparer<TValue>.Default` unless the builder is given a comparer. For `string` facets the default is `StringComparer.OrdinalIgnoreCase`, so `"Sweden"` and `"sweden"` are one facet value. The dictionary keeps the first-seen spelling, and that is the spelling the state presents and the serializer writes. A case-sensitive string facet is a builder option.
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

**Measured** (see §8): 9.4 ms warm and 19.9 ms cold for three selected facets, single-threaded. The estimate held; the cold half is dominated by the three selection scans, which cost about 3 ms each rather than 1 ms. Counting is cheaper than estimated because its cost is proportional to the context size, not the row count, and every facet without a selection uses the matching set, which shrinks with each added selection. That is also why five selected facets calculate faster than one.

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

Custom facets (§C5) are not part of the first version. The four built-in kinds are implemented against two **internal** abstract classes so that the calculation pipeline (§4) treats every facet the same way:

```csharp
internal abstract class FacetDefinition<T>          // mutable while the builder runs, frozen at Create
{
    string Key; string Title; FacetKind Kind;
    abstract FacetIndex Build(T[] items, TimeProvider timeProvider);   // once, at Create
}

internal abstract class FacetIndex                  // immutable, non-generic
{
    string Key; string Title; FacetKind Kind; int RowCount;
    abstract RowSet RowsMatching(Selection selection);                 // §4.1
    // to come with Calculate:
    // FacetState Present(RowSet context, Selection? selection);       // §4.3–4.5
    // JsonValue Format(object? value); object? Parse(JsonValue value); // §2.5
}
```

Keeping these internal means the built-ins can reshape them freely while they settle. When custom facets are added, the plan is to make them public as they stand then, together with a `FacetKind.Custom` value and a builder entry point. Nothing in the public API of the first version needs to change for that.

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

The existing `Linq2Dashboard.Core` project is renamed to `Linq2Dashboard` and moved under `src/` so that project, package and root namespace agree. This is the first code change of the implementation.

### The interaction loop, end to end

Concept §2 describes the engine as a function and the UI as a loop around it. With the API as built, the whole loop for any host, Blazor or otherwise, is:

```csharp
// once, at startup or when new data arrives (§C4.9)
Dashboard<Order> dashboard = Dashboard.Create(orders, Configure);

// per user session: the UI owns the selections, nothing else is mutable
Selections selections = dashboard.Serializer.FromJson(bookmarkOrEmpty);
DashboardState<Order> state = dashboard.Calculate(selections);

// a click on a value facet
selections = selections.Toggle("Country", value.Value);
// a click on a range or date bucket, a preset, or a clear
selections = selections.With("Amount", bucket.ToSelection());
selections = selections.With("OrderDate", preset.ToSelection());
selections = selections.Clear("Amount");

// every click ends the same way
state = dashboard.Calculate(selections);
Render(state);                                  // Facets, Metrics, MatchingCount, GetPage(...)
bookmark = dashboard.Serializer.ToJson(selections);
```

What the UI reads from the state, per facet kind:

| Kind | Renders | Click produces |
|---|---|---|
| `ValueFacetState` | `Values` (value, total, filtered, selected), `Other`, `Search(text)` | `Toggle(key, value.Value)` |
| `RangeFacetState` | `Buckets` as a histogram, `Null` beside it, `Min`/`Max` for a slider | `bucket.ToSelection()`, or `new RangeSelection(from, to)` from a slider |
| `DateFacetState` | `Buckets` per period, `Presets` with counts, `Null` | `bucket.ToSelection()`, `preset.ToSelection()` |
| any | `ContextCount`, `HasSelection`, `Title` | `Clear(key)` |

The UI never computes a count, never constructs an interval from bucket bounds, and never needs to know a facet's value type: `FacetValue.Value` is boxed and formatted by the UI, and goes straight back into `Toggle`.

### Blazor flow

```text
User clicks "Sweden"
    → component: selections = selections.Toggle("Country", "SE")
    → state = dashboard.Calculate(selections)
    → StateHasChanged()
```

The component owns `Selections` and the current `DashboardState<T>`. `Calculate` runs inline. At the measured cost (§4.8, 5 to 20 ms) that is acceptable on Blazor Server.

**Blazor Server is the primary target**; the sample application runs there. **WebAssembly works unchanged** and is supported: the core has no dependencies, needs no threads with parallel counting off, and time zone data is available in the browser. The limits are the browser's: the indexes (§8, 78 MB per million rows) plus the source objects share the WebAssembly heap, and `Create` runs several times slower interpreted than on the server JIT, though ahead-of-time compilation recovers most of that and enables the SIMD path in `RowSet`. A few hundred thousand rows is comfortable in the browser; a million is the host's call.

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

### Measured, first version

Intel Xeon W-2223 (4 cores), .NET 10, BenchmarkDotNet short job, 1 000 000 rows. Run with
`dotnet run -c Release --project benchmarks/Linq2Dashboard.Benchmarks -- --job short --filter *` and `-- --memory`.

| Scenario | Sequential | Parallel counting | Target |
|---|---|---|---|
| `Create`, full dashboard | 1 040 ms | | < 2 s ✓ |
| `Create` without sort order | 563 ms | | |
| `Create`, date facet only | 189 ms | | |
| `Create`, customer facet only (100 000 values) | 157 ms | | |
| `Calculate`, no selection | 3.6 ms | 2.3 ms | < 50 ms ✓ |
| `Calculate`, 1 facet, warm | 9.3 ms | 5.4 ms | ✓ |
| `Calculate`, 3 facets, warm | 9.4 ms | 4.9 ms | ✓ |
| `Calculate`, 5 facets, warm | 5.2 ms | 3.4 ms | ✓ |
| `Calculate`, range + date interval, warm | 9.2 ms | 5.2 ms | ✓ |
| `Calculate`, 3 facets, cold caches | 19.9 ms | 15.4 ms | ✓ |
| `Calculate`, range + date, cold caches | 22.2 ms | 16.5 ms | ✓ |
| A click: 3 facets cold, then one value toggled | 33.4 ms | 24.0 ms | ✓ (the click alone is the difference, ~13 ms) |
| `Search` over 100 000 customer labels | 4.3 ms | | |
| `GetPage`, first / middle / last of 2 200 pages | 0.001 / 2.1 / 4.0 ms | | |

| Memory above the 7.8 MB row array | |
|---|---|
| Value or boolean facet, any cardinality up to 2 000 | 3.8 MB |
| Customer facet, 100 000 values, searchable | 9.1 MB |
| Range facet | 11.6 MB |
| Date facet | 11.6 MB |
| Metric column | 7.6 MB |
| Sort order | 3.8 MB |
| Full dashboard, 8 facets, 3 metrics, sort | 78 MB |

Every target is met with margin, so per-value bitmaps stay out (§3.4). What the numbers say about where time goes:

- **The sort order is half of the build.** 480 ms of the 1 040 ms is `Array.Sort` over a million row ids through a delegate comparison. A key-specialised sort (materialise the key into a primitive array and sort indices by it) would likely halve that. Not needed for the target; first candidate if build time matters.
- **The date facet is the next build cost** at about 190 ms, from one time zone conversion per row. Caching the offset per calendar day would remove most of it.
- **Selection scans cost about 3 ms each**, three times the estimate, because the value scan sets bits one at a time through a range-checked builder. A word-at-a-time scan would bring it to the estimate. Only cold calculations pay this.
- **Date presets are rescanned on every calculation**, about 1 ms per preset, because their interval depends on "now". Caching the preset row set keyed by its resolved interval would make them free until midnight.
- **Parallel counting gives 1.5 to 2× on four cores** for warm calculations. The default stays off as decided; the option is worth turning on for a desktop or single-user host.
- **The no-selection state is unusually cheap** because a full context copies the precomputed totals instead of counting. It is not representative of a click.

---

## 9. Blazor package plan

The package renders a `DashboardState<T>`, turns clicks into `Selections`, and never counts anything (§C7). It starts from the decisions below.

### Projects

```text
src/Linq2Dashboard.Blazor/           Razor class library, net10.0; depends on the core and Microsoft.AspNetCore.Components.Web only
samples/Linq2Dashboard.Sample/       Blazor Server app over the benchmark generator's data; the manual test bed
tests/Linq2Dashboard.Blazor.Tests/   bUnit: each component renders a given state and raises the right selection change
```

### Decisions

- **Blazor Server is the primary target**; WebAssembly works unchanged with a smaller dataset (§7).
- **Plain CSS with custom properties.** One scoped stylesheet, no CSS framework dependency. Colours, spacing and fonts are custom properties on the root component so a host restyles without overriding markup.
- **One formatter service.** A single `IDashboardFormatter` cascaded from the root component turns boxed facet values, bucket bounds, instants, counts and metric values into text, and names the null value. Culture enters here and nowhere else; the core stays culture-free. A default implementation uses the current culture.
- **Paging only.** `Results<T>` uses `GetPage`. Virtualisation is a later addition; `Items` is already lazy, so nothing in the core needs to change for it.

### Components, in build order

1. **`DashboardView<T>`** (named so because `Dashboard<T>` is the core type) takes the `Dashboard<T>` and a bindable `Selections`. Owns the current selections and state, raises `SelectionsChanged` so a host can bookmark through the serializer, and cascades a `DashboardContext<T>` holding dashboard, selections, state and formatter. Every child derives from `DashboardComponentBase<T>`, which receives the context and re-renders on its `StateChanged` event; the context's `ToggleAsync`, `SelectAsync`, `ClearAsync` and `ClearAllAsync` are the only ways a click changes anything. **Done:** the default child is `StateSummary<T>`, a raw but complete rendering of counts, metrics and every facet with clickable values, buckets, presets and the null value, which exercises the whole loop. The sample app runs it over 200 000 rows.
2. **`ValueFacet`**: `Values` with counts and the selected flag, the null value through the formatter, `Other`, a search box when `IsSearchable`. Click → `Toggle`. Clear link → `Clear`. **Done.** Takes the facet `Key`; boolean facets use the same component. Options: `HideZeroCounts` (off, per §C4.3 the core keeps them and the UI decides; a selected zero-count value always stays), `ShowTotals` ("filtered (total)"), `SearchLimit`, and the texts for clear, other, no matches and the search placeholder. Search text is component-local UI state, which is allowed: it is not a selection (§C4.5). Zero-count, null and selected values carry classes so the stylesheet can treat them. Ships with a scoped stylesheet reading `--l2d-*` custom properties, which `DashboardView` declares with defaults; the library bundle is imported by the host's scoped CSS bundle automatically.
3. **`ActiveSelections`**: the chip row of everything selected, each removable. Cheap and heavily used. **Done.** One chip per selected value (removal toggles that value off) and one per range or date selection (removal clears the facet), in facet order, plus clear-all. A range or date chip uses the bucket's or preset's own label when the selection is exactly that bucket or preset, and otherwise the formatter's interval text through two formatter methods added for this, `FormatRangeSelection` and `FormatDateSelection`. Options: `HideWhenEmpty`, `ShowFacetTitle`, and the texts. **Grouped values (added later):** by default a value facet's selected values share one chip, since they are one OR group (§C4.1); each value is a removable token inside it and the chip's own remove clears the facet. `ValueSeparator` sets the text between tokens (", " default, " or " states the semantics); `GroupValues="false"` restores one chip per value.
4. **`Results<T>`**: paged list over `GetPage` with a required `RowTemplate`, since the core cannot know what a row looks like. **Done.** Two layouts: a plain list of row fragments, or a table with `HeaderTemplate` in `thead` and rows in `tbody`. A summary line ("Showing 1–50 of 111 064"), a first/previous/next/last pager shown only when there is more than one page, an empty text or template, and a bindable `PageIndex`. The page index is component-local UI state that returns to zero whenever the selections change, through a hook on `DashboardComponentBase` that runs before re-rendering; it is also clamped when the page count shrinks.
   **Virtualisation (added after the first version):** `Virtualize="true"` replaces paging with Blazor's `Virtualize` in a fixed-height scroll container (`--l2d-results-height`), in either layout; the table variant uses `tr` spacers and a sticky header. The items provider calls the new `DashboardState<T>.GetItems(skip, take)`, which `GetPage` now shares. The `Virtualize` element is keyed on the selections, so a new selection recreates it and it queries afresh from the top, the virtualised equivalent of paging returning to page one; an in-place `RefreshDataAsync` did not reliably re-render under bUnit and the key is simpler anyway. Rows need a roughly constant height, given by `ItemSize`.
5. **`Metrics`**: one tile per `MetricState`; no value renders as a dash. **Done.** A grid of tiles in definition order, or the subset and order given by `Keys`. A tile without a value carries a class and shows whatever the formatter returns for it, a dash by default. `IncludeMatchingCount` prepends a tile with the matching row count, which is not a metric in the core but is what most dashboards put first. **Per-metric components (added later):** `Metric` renders one tile by key like the facet components do, with an optional `Title` override and `MetricTemplate`, so tiles can sit anywhere in a layout; `MatchingCount` is the row-count tile on its own. Both render through a shared `MetricTile`, and `Metrics` is now a grid of those, so there is one tile implementation and one stylesheet. Defining a metric stays a builder call, since its column is built at `Create`.
6. **`RangeFacet`**: histogram of `Buckets` with the null value beside it; bucket click → `ToSelection()`. Min and max shown; a slider is a later iteration. **Done.** Each bar draws the total count faintly behind the filtered count, both scaled to the largest total, so the histogram keeps its shape (§C5) while the filled part follows the context. Two layouts via `BucketLayout`: vertical bars with labels beneath, or rows with an inline bar for many buckets or long labels. A click selects the bucket's interval; a second click on the selected bucket clears the facet; a wider interval marks every bucket it covers. The null value is a bucket of its own that toggles `RangeSelection.OnlyNull`. Bar sizes are passed to CSS as custom properties on each bucket, so the markup carries no pixel values.
   **Slider (done):** `ShowSlider` adds a `RangeSlider` beneath the bars, two overlapping native range inputs whose thumbs take the pointer, plus number inputs for precise entry. Handles preview while dragging and apply on release as a closed interval, so the dashboard recalculates once per gesture; both handles at the ends clears the facet; handles cannot cross and follow the current selection, so a bucket click moves them. The default step is a power of ten near a hundredth of the range. No JavaScript is involved.
7. **`DateFacet`**: period buckets and the preset list with counts; clicks → `ToSelection()`. **Done.** Presets render as a row of pills above the periods, each with its count as of this calculation; a preset click selects the relative preset, a second click clears. Periods render through the shared `BucketBars` component, extracted from `RangeFacet` so both facets draw bars the same way, with the null value as its own bar. A selected absolute interval marks every period and preset it covers, so clicking the March bar also lights "This month" when they coincide.
8. **Templates and styling last**, once the defaults work (§C7): optional `ValueTemplate` and `HeaderTemplate` per facet component, then the stylesheet. **Done.**
   - Templates: `HeaderTemplate` on `ValueFacet`, `RangeFacet` and `DateFacet` receives the facet state and replaces the default header; `ValueTemplate` on `ValueFacet` receives the `FacetValue` and replaces the label and count inside the button, so the click stays; `MetricTemplate` on `Metrics` receives the `MetricState` and replaces the tile's content. `Results` already had `RowTemplate`, `HeaderTemplate` and `EmptyTemplate`.
   - The default header is one shared `FacetHeader` component (title, optional extra content such as range bounds, clear link), so the three facets look and behave the same and their stylesheets shrank to what is theirs.
   - Styling: every `--l2d-*` custom property is declared once on `.l2d-dashboard` in `DashboardView.razor.css`, grouped as layout, type and colour, with a `prefers-color-scheme: dark` block adjusting the neutrals. Components read the properties with fallbacks. A host restyles by setting properties on the dashboard element or an ancestor; nothing in the markup needs overriding.

   | Property group | Properties |
   |---|---|
   | Layout | `--l2d-gap`, `--l2d-section-gap`, `--l2d-radius`, `--l2d-chip-radius`, `--l2d-bar-height`, `--l2d-bucket-label-width`, `--l2d-metric-min-width`, `--l2d-metric-padding` |
   | Type | `--l2d-font-size`, `--l2d-title-size`, `--l2d-small-size`, `--l2d-tiny-size`, `--l2d-metric-size` |
   | Colour | `--l2d-title-color`, `--l2d-muted`, `--l2d-border`, `--l2d-accent`, `--l2d-hover`, `--l2d-selected-background`, `--l2d-selected-color`, `--l2d-bar`, `--l2d-bar-total`, `--l2d-bar-track`, `--l2d-input-background`, `--l2d-metric-background` |

   State classes the stylesheet and hosts can hook: `l2d-selected`, `l2d-zero` (filtered count zero), `l2d-null` (the null value), `l2d-metric-empty` (no value), `l2d-collapsed` (facet body hidden).

9. **Collapsing (added after the first version).** Every facet component takes `Collapsible`, which renders the title as a toggle with a chevron in the shared header, and a bindable `Collapsed` value with `CollapsedChanged`, so a host can set or remember which facets are open. Collapsed hides the body only; the header, including the clear link and a range facet's bounds, stays. The value is component-local UI state synced from the parameter the way the results page index is, so it survives state changes and works without a binding. A header template replaces the toggle, leaving `Collapsed` as the only control.

### Code layout

Every component is a `.razor` file holding markup and directives only, with a `.razor.cs` partial class beside it holding parameters, state and methods. No `@code` blocks anywhere in the repository.

### State flow

`Calculate` runs inline in the click handler, then `StateHasChanged`. The component keeps no derived data of its own, so nothing can drift from the state. A back click is a state-cache hit and costs nothing.

---

## 10. Open questions

1. **Facets and metrics declared in Blazor markup.** Should `<Metric>` and the facet components be able to define what they show (aggregation and selector, or facet kind and selector), not only place something defined in the builder? Two routes were weighed on 2026-09-12: computing in the component over the matching rows (pure, but the UI calculates and pays per render; for facets that would be a second counting engine, which §C7 rules out), or registering the column lazily on the dashboard on first render (keeps the speed, but mutates a dashboard the design promises is immutable after `Create`, misses states already cached, and makes a page's markup part of the dashboard's definition). A third route is a markup-driven builder: a `DashboardDefinition` component whose children declare facets and metrics, run once to produce the `Dashboard<T>` before the view renders, so the definition lives on the page but still happens at `Create`. Kept as is for now: definition in the builder, placement in Blazor. To be revisited.

### Decided

- **Parallel counting is off by default.** Counting facets one after another costs roughly 10 to 20 ms at the target; counting them on separate cores could cut that to a few milliseconds but occupies several cores per click, which can hurt a busy Blazor Server. Supported as a builder option, off unless the application turns it on. See §4.3.

- **String facets are case-insensitive by default.** `OrdinalIgnoreCase`; first-seen spelling is presented. Case-sensitive is a builder option. See §3.3.
- **Project renamed to `Linq2Dashboard` under `src/`.** First code change of the implementation. See §7.
- **Selections serialise to JSON only.** No query-string format in the first version; applications encode the JSON for URLs themselves. See §2.5.
- **Custom facets come later.** The facet interfaces are internal in the first version and become public when custom facets are added. See §6.
- **Blazor: Server primary, WebAssembly supported with smaller datasets.** See §7.
- **Blazor: plain CSS with custom properties, one cascaded formatter service, paging only.** See §9. Paging remains the default; virtualisation was added afterwards as an opt-in on `Results`, and a slider as an opt-in on `RangeFacet`.

- **Columnar only in the first version.** No per-value bitmaps. Same strategy for every cardinality; bitmaps are a later, benchmark-justified addition. See §1, §3.4, §4.1.
- **Range bounds are `double`.** The precision trade for `decimal` properties is accepted; range values are used only for filtering and bucketing, never for metrics. See §2.2, §3.3.
- **The dashboard is stateless.** `Calculate(selections)` is the only entry point; the UI owns the current `Selections`. See §2.3, §7.
