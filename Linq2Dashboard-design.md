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
     .Name("Country")
     .Top(20);

    b.ValueFacet("status", x => x.Status);                     // explicit key (§C7)

    b.ValueFacet("customer", x => x.CustomerId)                // counts and selects by id ...
     .Label(x => $"{x.CustomerName} ({x.City})")               // ... shows and searches by label (§C5)
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

    b.TextFacet("search", (x, text) =>                          // free text, matched by the application (§C5)
        x.CustomerName.Contains(text, StringComparison.OrdinalIgnoreCase)
     || x.Product.Contains(text, StringComparison.OrdinalIgnoreCase))
     .Name("Search");

    b.Count("orders");
    b.Sum("revenue", x => x.Amount);
    b.Average("average", x => x.Amount).Name("Average order");
    b.Distinct("customers", x => x.Customer);                  // different non-null values; optional comparer
    b.Calculated("perCustomer", m => m["revenue"] / m["customers"]);   // formula over earlier metrics; null in, null out

    b.OrderByDescending(x => x.OrderDate)                      // application-defined sort (§C8)
     .ThenBy(x => x.Id);

    b.UseTimeProvider(TimeProvider.System);                    // default; tests pass a fake
});
```

- `Dashboard.Create` enumerates the source exactly once, applies fixed filters, and builds every column and index. After it returns, the dashboard is immutable and thread-safe.
- Every builder method validates eagerly. Duplicate keys, a non-member selector without an explicit key, or a range facet over a non-numeric property throw at `Create`, not at first use. The builder and every facet builder refuse further configuration once the dashboard is built.
- Metrics are builder methods (`Count`, `Sum`, `Average`, `Min`, `Max`, `Distinct`, `Calculated`) rather than a separate `Metric` factory, because C# cannot infer `T` for `Metric.Sum(x => x.Amount)` outside the builder. `Distinct` takes any equatable property and an optional `IEqualityComparer<TProp>`, with the value facet default (case-insensitive strings) when none is given. `Calculated` takes a `Func<MetricValues, double?>`; `MetricValues` exposes the values and shares of the metrics defined before it by key and throws on any other key. The builder runs the formula once at definition with every input at null, so an unconditionally read wrong key fails in the builder call; a key first read inside a branch fails at the first `Calculate` that reaches it, with the key in the message.
- Range and metric selectors accept any numeric property, nullable or not; the conversion to `double` is compiled into the selector. Date selectors accept `DateTime`, `DateTimeOffset`, `DateOnly` and their nullable forms; anything else is rejected at `Create`.
- `Buckets(100, 500, 1000)` names cut points, not edges: it yields "below 100", "100 to 500", "500 to 1000" and "1000 and above", so every value lands in a bucket.
- `TextFacet` (§C5, added 2026-09-14) always takes an explicit key, since there is no selector to derive one from, and a `Func<T, string, bool>` that must be pure and thread-safe. The builder offers `Name` only; matching semantics live in the function.
- All facet builders return a typed builder so kind-specific options are discoverable; the shape above is the whole configuration surface for the first version.

Scoping a dashboard (§C4.10, added 2026-09-15):

```csharp
Dashboard<Order> nordic = dashboard.Where(x => x.Region == "Nordic");   // a scoped dashboard
Dashboard<Order> open = nordic.Where(x => x.Status == "Open");           // scopes compose
Dashboard<Order> view = dashboard.Where(selections);                      // the same, in the facets' own terms
```

- `Where` on a built dashboard returns a new `Dashboard<T>` over the rows that pass the predicate. It behaves exactly like a dashboard built with the predicate as one more fixed filter: `TotalCount`, every total count, "Other" and every metric share are measured against the subset, and a value no row in the subset has is not listed. The one difference is that range and date buckets, and a range facet's `Min` and `Max`, are the parent's.
- It costs one predicate call per row plus one count per facet and one aggregation per metric (§4.3, §4.6), not a build: rows, columns, indexes, the sort order, the serializer and the selection row-set cache are shared with the parent (§5). Measured in §8.
- The parent is unchanged. Both are immutable and thread-safe, take the same `Selections`, and share one `Serializer`, so a bookmark applies to any scope. A host that switches between scopes keeps the scoped dashboards it has made; each has its own state cache.
- `Where(Selections)` (added 2026-09-15) resolves each selection through its facet index and the shared row-set cache (§4.1, §5), ANDs the sets with the current scope and builds the same scoped dashboard. No predicate runs and no row object is read, so a scope the user has just clicked costs only the counts. The facets' matching rules apply, a relative date preset is resolved once, now, and an unknown key throws as in `Calculate`. The scoped dashboard starts from `Selections.Empty`; because the scope is part of "all rows" rather than a selection, a scoped facet lists only the values in scope and gets no own-facet exclusion.

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

sealed record TextSelection(string Text) : Selection;   // trimmed; whitespace-only is empty and clears the facet (§C5)
```

`Selections` is keyed by facet key, compares by value, and treats an empty `ValueSelection` or an empty `TextSelection` as "clear". `Toggle` uses default equality on the boxed value; the facet's comparer applies when values are mapped to codes, so `"se"` and `"SE"` may both sit in a selection and still select the same rows.

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
    string Name { get; }
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
    string? LabelOf(object? value);             // the builder's label for a value, presented or not; null when none (§C5)
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

sealed class TextFacetState : FacetState
{
    string? Text { get; }                       // the current text, null when unconstrained (§C5); nothing else to present
}

sealed record FacetValue(object? Value, int TotalCount, int FilteredCount, bool Selected, string? Label = null);   // Label only under a facet with a label selector (§C5)
sealed record FacetCount(int TotalCount, int FilteredCount);                       // "Other"
sealed record RangeBucket(double From, double To, int TotalCount, int FilteredCount, bool Selected)
    { RangeSelection ToSelection(); }                                                // [From, To), last bucket closed
sealed record DateBucket(DateTimeOffset From, DateTimeOffset To, DateTime PeriodStart, int TotalCount, int FilteredCount, bool Selected)
    { DateSelection ToSelection(); }                                                 // [From, To) instants; PeriodStart local, for labels
sealed record PresetState(DatePreset Preset, DateTimeOffset From, DateTimeOffset To, int TotalCount, int FilteredCount, bool Selected);
sealed record MetricState(string Key, string Name, Aggregation Aggregation, double? Value, double? Share);   // null = no value; Share = Value / total, count, sum and distinct only (§C4.4)
readonly struct MetricValues { double? this[string key]; double? Value(string key); double? Share(string key); }   // what a Calculated formula reads: earlier metrics by key
sealed record ResultPage<T>(IReadOnlyList<T> Items, int PageIndex, int PageSize, int MatchingCount);
```

`Value` is `object?` on purpose. The UI formats it; the core does not know about cultures, which is also why buckets carry bounds rather than label strings. The one string the core carries is the application's own label for a value facet's value (§C5), supplied by the builder's `Label` selector: it is data read from the rows, not formatting, and it is on `FacetValue.Label` for presented values and behind `LabelOf` for any value, so the chip for a selected value can be named from the selection alone. The default formatter shows it when present and formats the value otherwise. Boolean facets reuse `ValueFacetState`. `FacetKind` has `Value`, `Boolean`, `Range` and `Date` in the first version, and `Text` since 2026-09-14.

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
  "status":    { "values": [null, "Open"] },
  "search":    { "text": "acme" }
}
```

- JSON was the only format in the first version; applications that wanted selections in a URL encoded the JSON themselves. **Query-string form (added 2026-09-16).** `ToQueryString` and `FromQueryString` write and read one parameter per facet, named by the facet key, in a form that is short, readable and can be written by hand or by another page, so a customer page can link to `?Customer=123` without the library:

  ```text
  ?Country=SE,NO&Amount=[100..500)&OrderDate=last30Days&Shipped=2026-03-01T00:00+01:00..&Discount=null&status=null,Open&search=acme
  ```

  - A value facet is a comma-separated list; `null` is the null value. A backslash escapes the next character, so a literal comma or backslash is written `\,` and `\\`; the empty string is written `""`, and the literal texts `null` and `""` are written `\null` and `\""`. Values use the same text as the JSON form: strings as they are, numbers and booleans as their JSON text, enums by name, dates in ISO 8601, and the builder's `Serialize(format, parse)` when given.
  - A range facet is `from..to`; an empty bound is unbounded (`100..`, `..500`). Both ends are inclusive without brackets; when an end is exclusive the interval is written in bracket notation, `[100..500)`, which is what a bucket click produces. A single number is the closed interval at that value. `,null` after the interval adds the null rows; `null` alone is only the null rows.
  - A date facet is the preset name in camelCase (`last30Days`, read case-insensitively) or `from..to` in the shortest ISO 8601 text that round-trips: seconds and fraction only when present, `Z` for a zero offset. `,null` and `null` as for ranges.
  - A text facet is the text itself.
  - Encoding keeps the URL readable: only `& = + # %`, space, quotes and non-ASCII are percent-encoded, so `,` `..` `:` `[` `]` and `/` stay as they are. Reading accepts a query string with or without `?`, or a whole URL, decodes `+` as a space, and forgives a hand-written `+` in an offset or exponent that arrived as a space.
  - Reading is as lenient as JSON: a parameter that is not a facet key is ignored, a value that does not parse drops that facet, an empty value clears it, and the last of several parameters with the same name wins.
  - **Prefix.** Both methods take a prefix, prepended to every facet key, so two dashboards on one page (`o.Country`, `r.Country`) and a page's own parameters (`rows`, `tab`) never collide. The prefix is a plain string, not parsed, so a facet key may contain anything.
  - **Composition.** `ToQuery` returns every facet as a dictionary entry, `null` for an unselected one, the shape `NavigationManager.GetUriWithQueryParameters` takes. `ToQueryString` takes an optional existing query or URL and keeps, verbatim and in place, every parameter in it that is not one of the dashboard's facets under the prefix, so a page's parameters survive and the facets' are replaced or removed. `FromQuery` reads already decoded pairs.
  - The Blazor view's `SyncUrl` (§9.1) is a thin client of these methods; a host that owns its URL, or a non-Blazor host, calls them directly.
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

Rows that fail a fixed filter are dropped at build time, so ids are dense over the dataset and every count in the system is relative to the dataset (§C4.3). A scoped dashboard (§C4.10) keeps the same array and ids and carries a `RowSet` naming the rows in scope; its counts are relative to that set. The dashboard keeps `_items` for paging and export only. Nothing else touches the objects after build.

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
string?[] labels       the builder's Label selector on the first row per value, built once at Create; absent without a selector
string[]  searchLabels labels[i] ?? invariant text of dictionary[i], built lazily on the first Search
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

**Distinct column** (distinct-count metrics, added 2026-09-14):

```text
int[]     codes        one per row; 0 = null, 1..V = distinct value index + 1, first-seen order
int       distinctCount V
```

The same dictionary encoding as a value column, with the same default comparer, but the dictionary itself is dropped after the build: the metric only ever asks whether two rows hold the same value, never which value. Four bytes per row. A distinct metric over a property that is also a value facet builds its own column rather than sharing the facet's; sharing would need selector-expression equality and saves 4 bytes per row, so it waits for a case that needs it.

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
- Text (§C5): scan `_items` and set a bit where the application's function returns true for `(items[row], text)`. The only scan that touches row objects and runs application code, so it is the one scan whose cost the library does not control. When parallel counting is enabled it is split across cores over word-aligned chunks of 64 rows, which the contract (pure, thread-safe) allows; otherwise it runs serially like the other scans, so the option keeps its meaning of "this dashboard may use several cores per click". The result is cached like any other row set, so retyping a text or removing and re-adding it costs nothing.

Each `R_f` is cached by `(facetKey, selection)` (§5). The scans are `O(N)` with sequential access, roughly 1 ms per million rows. `Where(Selections)` (§C4.10) produces its scope from the same `R_f` sets through the same cache, so scoping by selections adds no scan of its own.

### 4.2 Matching set and per-facet contexts

Let the facets with selections be `R_1 … R_k`.

```text
M    = A AND R_1 AND R_2 AND … AND R_k      (A is the dashboard's row set: every row, or the scope for a scoped dashboard, §C4.10)

C_f  = AND of all R_g with g ≠ f            for a facet f with a selection
C_f  = M                                    for a facet f without a selection
```

Computing every `C_f` naively costs `O(k²)` ANDs. Instead:

```text
prefix[i] = A AND R_1 AND … AND R_{i-1}
suffix[i] = R_{i+1} AND … AND R_k AND A
C_i       = prefix[i] AND suffix[i]
```

`A` is a full set for a dashboard from the builder, so the ANDs with it are free; for a scoped dashboard it is the scope, and every context and the matching set stay inside it without any other change to the pipeline.

That is `3k` ANDs in total, each 15 625 words at 1 M rows. For ten selected facets this is under a millisecond.

### 4.3 Counting

For each facet `f`, with context `C_f` and its `codes` (or `bucketCodes`) column:

```csharp
Span<int> counts = stackalloc or pooled, length V + 1, zeroed;
foreach (int row in C_f.Rows())
    counts[codes[row]]++;
```

One sequential pass over the rows in context. Cost is proportional to `|C_f|`, not to `V`.

`counts[0]` is the null value's filtered count (§C4.8). Total counts come from the column and are never recomputed. A scoped index (§C4.10) carries its own totals array, counted once over the scope with this same loop when `Where` is called, and presents against it; the column is shared with the parent. A value facet also counts how many codes have a non-zero total, which is the scope's value count, and skips codes with a zero total when presenting and searching, so a value absent from the scope is never listed.

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

**Share of the total (§C4.4, added 2026-09-14).** `MetricColumn` already keeps the aggregate over every row from the build, so the share costs one division per metric: `M.Count / N` for count, `Sum(M) / Total.Sum` for sum. Average, min and max get `null`, as does a sum without contributing rows and any share whose total is zero. `MetricIndex.Present(matching)` produces the whole `MetricState`, so the value and its share cannot disagree. A scoped metric index (§C4.10) holds the aggregate, row count and distinct count over the scope, computed once by `Where` with the same passes a calculation makes, so shares in a scope are against the scope.

**Distinct (§C4.4, added 2026-09-14).** One pass over `M.Rows()` reading the distinct column's code per row and marking it in a bit set of V bits; the count of newly marked bits is the answer, and the full dataset answers with V without a scan. The share is that count over V. With no non-null value in the matching rows both are `null`. At a million rows this is the same sequential scan as facet counting, so it sits inside the per-click budget; the bit set is 12.5 KB for a 100 000-value customer column and is allocated per calculation.

**Calculated (§C4.4, added 2026-09-14).** Metrics are presented in definition order into one `MetricState[]`; a calculated metric receives a `MetricValues` over the states filled so far (a struct holding the array and the count, no allocation) and applies its formula. Lifted nullable arithmetic gives null-in-null-out for free; the result is then kept only if `double.IsFinite`, so division by zero and NaN become `null`. No column, no row work, no share. A formula that throws propagates: it is application code with a bug, not data.

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

- **Immutable after build:** `_items`, all columns, all indexes, total counts, `sortedRows`. No locks needed to read. A scoped dashboard (§C4.10) is built the same way from its parent and is as immutable. `Dashboard<T>` carries `[ImmutableObject(true)]` (System.ComponentModel) so that caches which copy mutable values, `HybridCache` in particular, store and hand out the instance itself instead of serialising it; a dashboard cannot be serialised and must never reach a distributed cache. The attribute is the whole hosting contract the core makes: how a host loads, caches and replaces a dashboard is the host's code (the five-minute walkthrough shows a `HybridCache` service).
- **Selection row-set cache:** `(facetKey, Selection) → RowSet`, bounded LRU, default 256 entries, inside the dashboard. Selections are records with value equality, so they are their own cache keys. Toggling a value in one facet re-uses every other facet's cached set. A scoped dashboard shares this cache with its parent, since the rows a selection matches do not depend on the scope; only the AND with the scope is per dashboard.
- **State cache:** `Selections → DashboardState<T>`, bounded LRU, default 8 entries. Covers back/forward and "undo last click" for free. Per dashboard: a scope has its own, so switching between kept scopes returns cached states.
- Both caches are safe for concurrent readers and writers. A miss computed twice is harmless because results are immutable and equal.
- **States are immutable** and hold their own arrays. Rendering one state while the next is calculated is safe. A state keeps a reference to the dashboard for `GetPage` and `Search`; it never mutates it.
- **Scratch memory** during `Calculate` comes from `ArrayPool<T>` and is returned before the state is published. Arrays that the state keeps (`counts` for searchable facets, `M`) are allocated for it.

---

## 6. Extensibility: custom facets

Custom facets (§C5) are not part of the first version. The four built-in kinds are implemented against two **internal** abstract classes so that the calculation pipeline (§4) treats every facet the same way:

```csharp
internal abstract class FacetDefinition<T>          // mutable while the builder runs, frozen at Create
{
    string Key; string Name; FacetKind Kind;
    abstract FacetIndex Build(T[] items, TimeProvider timeProvider);   // once, at Create
}

internal abstract class FacetIndex                  // immutable, non-generic
{
    string Key; string Name; FacetKind Kind; int RowCount;
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
| any | `ContextCount`, `HasSelection`, `Name` | `Clear(key)` |

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
| `Where`, scoped dashboard over about 80 % of the rows (added 2026-09-15) | 28 ms | | 526 KB allocated |
| `Calculate`, no selection | 3.6 ms | 2.3 ms | < 50 ms ✓ |
| `Calculate`, 1 facet, warm | 9.3 ms | 5.4 ms | ✓ |
| `Calculate`, 3 facets, warm | 9.4 ms | 4.9 ms | ✓ |
| `Calculate`, 5 facets, warm | 5.2 ms | 3.4 ms | ✓ |
| `Calculate`, range + date interval, warm | 9.2 ms | 5.2 ms | ✓ |
| `Calculate`, 3 facets, cold caches | 19.9 ms | 15.4 ms | ✓ |
| `Calculate`, range + date, cold caches | 22.2 ms | 16.5 ms | ✓ |
| A click: 3 facets cold, then one value toggled | 33.4 ms | 24.0 ms | ✓ (the click alone is the difference, ~13 ms) |
| `Search` over 100 000 customer labels | 4.3 ms | | |
| `Calculate`, new text in a text facet, cold (two `Contains` per row) | 80.9 ms | 19.8 ms | see below |
| `GetPage`, first / middle / last of 2 200 pages | 0.001 / 2.1 / 4.0 ms | | |

| Memory above the 7.8 MB row array | |
|---|---|
| Value or boolean facet, any cardinality up to 2 000 | 3.8 MB |
| Customer facet, 100 000 values, searchable | 9.1 MB |
| Range facet | 11.6 MB |
| Date facet | 11.6 MB |
| Metric column | 7.6 MB |
| Distinct column (codes only) | 3.8 MB |
| Sort order | 3.8 MB |
| Full dashboard, 8 facets, 3 metrics, sort | 78 MB |

Every target is met with margin, so per-value bitmaps stay out (§3.4). What the numbers say about where time goes:

- **A scope costs about 3 % of a build and under 1 % of its memory** (added 2026-09-15): 28 ms and 526 KB against 1 040 ms and 78 MB. One predicate call per row, one counting pass per facet, one aggregation per metric; the row set is 125 KB and the rest is count arrays. A host can afford a scope per tenant, per user or per tab.
- **The sort order is half of the build.** 480 ms of the 1 040 ms is `Array.Sort` over a million row ids through a delegate comparison. A key-specialised sort (materialise the key into a primitive array and sort indices by it) would likely halve that. Not needed for the target; first candidate if build time matters.
- **The date facet is the next build cost** at about 190 ms, from one time zone conversion per row. Caching the offset per calendar day would remove most of it.
- **Selection scans cost about 3 ms each**, three times the estimate, because the value scan sets bits one at a time through a range-checked builder. A word-at-a-time scan would bring it to the estimate. Only cold calculations pay this.
- **Date presets are rescanned on every calculation**, about 1 ms per preset, because their interval depends on "now". Caching the preset row set keyed by its resolved interval would make them free until midnight.
- **Parallel counting gives 1.5 to 2× on four cores** for warm calculations. The default stays off as decided; the option is worth turning on for a desktop or single-user host.
- **The no-selection state is unusually cheap** because a full context copies the precomputed totals instead of counting. It is not representative of a click.
- **A text facet's scan is the one cost the library does not own** (added 2026-09-14). Two case-insensitive `Contains` over a million rows take about 80 ms serially and 20 ms with parallel counting on four cores, so a new text is four to eight times a cold click and the Blazor input debounces. Only a new text pays; the row set is cached by the text afterwards. An application with a heavy predicate or a large dataset should turn parallel counting on, or precompute what the predicate reads.

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

1. **`DashboardView<T>`** (named so because `Dashboard<T>` is the core type) takes the `Dashboard<T>` and a bindable `Selections`. Owns the current selections and state, raises `SelectionsChanged` so a host can bookmark through the serializer, and cascades a `DashboardContext<T>` holding dashboard, selections, state and formatter. Every child derives from `DashboardComponentBase<T>`, which receives the context and re-renders on its `StateChanged` event; the context's `ToggleAsync`, `SelectAsync`, `ClearAsync` and `ClearAllAsync` are the only ways a click changes anything. **`StateChanged` on the view (added 2026-09-14):** an `EventCallback<DashboardState<T>>` raised after every calculation, the initial one included, so a host can render its own chart or summary from the result without a `@ref` to the context. On a click it follows `SelectionsChanged` (cause, then result); for selections set through the parameter it is the only callback, since `SelectionsChanged` never echoes the host's own value back. The calculation is synchronous, so by the time either callback runs the context already holds the new state. **One context per view (fixed 2026-09-16):** the view creates its `DashboardContext<T>` once and cascades it as fixed; when the host passes another `Dashboard`, typically a scope of the first (§C4.10), or another `Formatter`, the same context adopts it, recalculates with the current selections and raises `StateChanged`. Until then the view built a new context, which the components inside never saw: they subscribe once, in `OnInitialized`, and a fixed cascading value is not re-delivered, so a scope switch in the sample left every metric tile at the parent's numbers. The docs demo uses it for an orders-per-month chart above the results, drawn by the page from the date facet's buckets and clickable back into the selections through the view's `Context`. **`Key` and `SyncUrl` (added 2026-09-16):** `Key` is the view's identity on the page, rendered as `data-key` on the root (not as `id`, which stays a pass-through attribute; `Name` was rejected because it means display text on every other component). `SyncUrl`, off by default, keeps the selections in the page URL in the query-string form of §2.5, prefixed `key.` when a key is given, so two views on one page stay apart. Rules: on first render the URL wins when it holds selections for the view, and `SelectionsChanged` reports them so a host binding `Selections` learns; otherwise the parameter applies and is written. Every change, a click, a host-set parameter or a new dashboard, replaces the URL in place (`replace: true`, so the back button leaves the page rather than undoing clicks; push history can be an option later), keeping the page's other parameters verbatim through `ToQueryString`'s existing-query argument. The view listens to `LocationChanged` and applies a navigation within the same path whose query says something else for its prefix, so back, forward and links on the page reach the view; a navigation to another path is ignored so a page being left is not recalculated. Writing happens only when `RendererInfo.IsInteractive`, so a prerendering Server view reads the URL and leaves it alone. Nothing is written when the URL already reads as the current selections, which is what stops the view's own navigation from coming back as a change. Until then the docs demo did this itself with JSON in one parameter, about thirty lines of page code; it now sets `SyncUrl` and keeps only its row count parameter. **Done:** the default child is `StateSummary<T>`, a raw but complete rendering of counts, metrics and every facet with clickable values, buckets, presets and the null value, which exercises the whole loop. The sample app runs it over 200 000 rows.
2. **`ValueFacet`**: `Values` with counts and the selected flag, the null value through the formatter, `Other`, a search box when `IsSearchable`. Click → `Toggle`. Clear link → `Clear`. **Done.** Takes the facet `Key`; boolean facets use the same component. Options: `HideZeroCounts` (off, per §C4.3 the core keeps them and the UI decides; a selected zero-count value always stays), `Sort` with `SortDescending` (`FacetSort.Rank` by default, `Label` sorts the formatter's text in the current culture ignoring case, `Value` uses `IComparable` and throws naming the key otherwise; null stays last, ties keep rank order, "Other" is rendered after the list so it is unaffected; per §C6 ranking picks the values and the UI orders them), `ShowTotals` ("filtered (total)", on by default; the button's tooltip always carries both plus the filtered count's share of the total through `FormatShare`, "34 (100) 34.0 %" in the invariant culture, as do the bucket and preset tooltips of the range and date facets, added 2026-09-16), `SearchLimit`, and the texts for clear, other, no matches and the search placeholder. Search text is component-local UI state, which is allowed: it is not a selection (§C4.5). Zero-count, null and selected values carry classes so the stylesheet can treat them. Ships with a scoped stylesheet reading `--l2d-*` custom properties, which `DashboardView` declares with defaults; the library bundle is imported by the host's scoped CSS bundle automatically.
3. **`ActiveSelections`**: the chip row of everything selected, each removable. Cheap and heavily used. **Done.** One chip per selected value (removal toggles that value off) and one per range or date selection (removal clears the facet), in facet order, plus clear-all. A range or date chip uses the bucket's or preset's own label when the selection is exactly that bucket or preset, and otherwise the formatter's interval text through two formatter methods added for this, `FormatRangeSelection` and `FormatDateSelection`. Options: `HideWhenEmpty`, `ShowFacetName`, and the texts. **Grouped values (added later):** by default a value facet's selected values share one chip, since they are one OR group (§C4.1); each value is a removable token inside it and the chip's own remove clears the facet. `ValueSeparator` sets the text between tokens (", " default, " or " states the semantics); `GroupValues="false"` restores one chip per value.
4. **`Results<T>`**: paged list over `GetPage` with a required `RowTemplate`, since the core cannot know what a row looks like. **Done.** Two layouts: a plain list of row fragments, or a table with `HeaderTemplate` in `thead` and rows in `tbody`. A summary line ("Showing 1–50 of 111 064"), a first/previous/next/last pager shown only when there is more than one page, an empty text or template, and a bindable `PageIndex`. The page index is component-local UI state that returns to zero whenever the selections change, through a hook on `DashboardComponentBase` that runs before re-rendering; it is also clamped when the page count shrinks.
   **Virtualisation (added after the first version):** `Virtualize="true"` replaces paging with Blazor's `Virtualize` in a fixed-height scroll container (`--l2d-results-height`), in either layout; the table variant uses `tr` spacers and a sticky header. The items provider calls the new `DashboardState<T>.GetItems(skip, take)`, which `GetPage` now shares. The `Virtualize` element is keyed on the selections, so a new selection recreates it and it queries afresh from the top, the virtualised equivalent of paging returning to page one; an in-place `RefreshDataAsync` did not reliably re-render under bUnit and the key is simpler anyway. Rows need a roughly constant height, given by `ItemSize`.
5. **`Metrics`**: one tile per `MetricState`; no value renders as a dash. **Done.** A grid of tiles in definition order, or the subset and order given by `Keys`. A tile without a value carries a class and shows whatever the formatter returns for it, a dash by default. `IncludeMatchingCount` prepends a tile with the matching row count, which is not a metric in the core but is what most dashboards put first. **Per-metric components (added later):** `Metric` renders one tile by key like the facet components do, with an optional `Name` override and `MetricTemplate`, so tiles can sit anywhere in a layout; `MatchingCount` is the row-count tile on its own. Both render through a shared `MetricTile`, so there is one tile implementation and one stylesheet. `MetricTile`, like `FacetHeader`, `BucketBars` and `RangeSlider`, is a rendering detail, not API: Razor cannot emit an internal component, so since 2026-09-14 the four shared building blocks are marked `EditorBrowsable(Never)` and documented as unsupported instead; the browsable API is the intent-carrying components only. Defining a metric stays a builder call, since its column is built at `Create`. **`Metrics` removed on 2026-09-14:** with per-metric components, a grid of every metric was a prototype convenience that took page-level decisions (which metrics, order, container) away from the page; a three-line CSS grid, shown in the getting-started page, covers the layout. **Share (added 2026-09-14):** `Metric` renders the state's share under the value through a new formatter method, `FormatShare`, one decimal by default; a metric without a share (average, min, max, no value, zero total) renders the tile unchanged. `MatchingCount` shows matching rows over total rows, computed in the component since the count is not a metric in the core. Both go through `MetricTile`'s `Share` parameter, styled by `l2d-metric-share`. Until 2026-09-16 both took a `ShowShare` parameter, off by default, and the template context's `Share` was gated by it too, so a template reading `Share` got null unless the page also set a flag meant for the default tile. The parameter is gone: the default tile is a placeholder to get pixels on the screen and shows what the state has, and the template context always carries the formatted share when there is one, so a template needs no flag to get a piece of data. **Template context (2026-09-14):** `MetricTemplate` on `Metric` and `MatchingCount` receives a `MetricTileContent` record, the formatted title, value and share, the empty flag and the raw `MetricState` (null for the matching count), so a template composes the pieces with its own markup instead of formatting the state again. **No wrapper with a template (2026-09-16):** until then the template replaced only the inside of `div.l2d-metric`, whose stylesheet gave it flex layout, padding, border, radius and background, so a host wanting its own tile had to fight the library's frame; the docs demo itself needed a `::deep` override of the tile's display and negative margins to bleed its icon panel through the padding. Now `Metric` and `MatchingCount` render the template fragment directly and `MetricTile` is the default tile only. The template owns the root, so `data-key`, `l2d-metric-empty` and the other hooks are the template's to place from the context, and `Class` and extra attributes apply to the default tile only, which the parameter's doc comment says. This is the one template in the package that drops the wrapper: `HeaderTemplate` and `ValueTemplate` keep theirs because the facet root and the value button carry behaviour, while the tile's wrapper carried nothing but style. The demo's tile draws its own frame and colours in its own stylesheet. An `Icon` render fragment slot, added and removed the same day, was the only decoration parameter in the package and its one consumer, the docs demo, overrode every default it set; a tile has no behaviour, so its look is the page's to decide and the template is the path. The demo renders its icon tiles through the template with a small component of its own.
6. **`RangeFacet`**: histogram of `Buckets` with the null value beside it; bucket click → `ToSelection()`. Min and max shown; a slider is a later iteration. **Done.** Each bar draws the total count faintly behind the filtered count, both scaled to the largest total, so the histogram keeps its shape (§C5) while the filled part follows the context. Two layouts via `BucketLayout`: vertical bars with labels beneath, or rows with an inline bar for many buckets or long labels. A click selects the bucket's interval; a second click on the selected bucket clears the facet; a wider interval marks every bucket it covers. The null value is a bucket of its own that toggles `RangeSelection.OnlyNull`. Bar sizes are passed to CSS as custom properties on each bucket, so the markup carries no pixel values.
   **Slider (done):** `ShowSlider` adds a `RangeSlider` beneath the bars, two overlapping native range inputs whose thumbs take the pointer, plus number inputs for precise entry. Handles preview while dragging and apply on release as a closed interval, so the dashboard recalculates once per gesture; both handles at the ends clears the facet; handles cannot cross and follow the current selection, so a bucket click moves them. The default step is a power of ten near a hundredth of the range. No JavaScript is involved.
7. **`DateFacet`**: period buckets and the preset list with counts; clicks → `ToSelection()`. **Done.** Presets render as a row of pills above the periods, each with its count as of this calculation; a preset click selects the relative preset, a second click clears. Periods render through the shared `BucketBars` component, extracted from `RangeFacet` so both facets draw bars the same way, with the null value as its own bar. A selected absolute interval marks every period and preset it covers, so clicking the March bar also lights "This month" when they coincide.
8. **Templates and styling last**, once the defaults work (§C7): optional `ValueTemplate` and `HeaderTemplate` per facet component, then the stylesheet. **Done.**
   - Templates: `HeaderTemplate` on `ValueFacet`, `RangeFacet` and `DateFacet` receives the facet state and replaces the default header; `ValueTemplate` on `ValueFacet` receives the `FacetValue` and replaces the label and count inside the button, so the click stays; `MetricTemplate` on `Metric` and `MatchingCount` receives a `MetricTileContent` with the formatted pieces and replaces the whole tile, wrapper included. `Results` already had `RowTemplate`, `HeaderTemplate` and `EmptyTemplate`.
   - The default header is one shared `FacetHeader` component (title, optional extra content such as range bounds, clear button), so the three facets look and behave the same and their stylesheets shrank to what is theirs. The clear button and the clear-all button in `ActiveSelections` show an × (changed 2026-09-15); the `ClearText` and `ClearAllText` parameters remain as the accessible label and tooltip.
   - Styling: every `--l2d-*` custom property is declared once on `.l2d-dashboard` in `DashboardView.razor.css`, grouped as layout, type and colour, with a `prefers-color-scheme: dark` block adjusting the neutrals. Components read the properties with fallbacks. A host restyles by setting properties on the dashboard element or an ancestor; nothing in the markup needs overriding.

   | Property group | Properties |
   |---|---|
   | Layout | `--l2d-gap`, `--l2d-section-gap`, `--l2d-radius`, `--l2d-chip-radius`, `--l2d-bar-height`, `--l2d-bucket-label-width`, `--l2d-metric-min-width`, `--l2d-metric-padding` |
   | Type | `--l2d-font-size`, `--l2d-title-size`, `--l2d-small-size`, `--l2d-tiny-size`, `--l2d-metric-size` |
   | Colour | `--l2d-title-color`, `--l2d-muted`, `--l2d-border`, `--l2d-accent`, `--l2d-hover`, `--l2d-selected-background`, `--l2d-selected-color`, `--l2d-bar`, `--l2d-bar-total`, `--l2d-bar-track`, `--l2d-input-background`, `--l2d-metric-background` |

   State classes the stylesheet and hosts can hook: `l2d-selected`, `l2d-zero` (filtered count zero), `l2d-null` (the null value), `l2d-metric-empty` (no value), `l2d-collapsed` (facet body hidden).

   - **Class and attribute passthrough (added 2026-09-14).** Every public component (`DashboardView`, the three facets, `Results`, `ActiveSelections`, `Metric`, `MatchingCount`, `StateSummary`) takes a `Class` parameter, rendered after the library's own classes on the root element, and captures every other unmatched attribute (`id`, `style`, `title`, `data-*`, `aria-*`) onto that element. This is the third tier of styling after custom properties and the stable classes: it lets a host target one component from its own stylesheet without a wrapper element or `::deep`. Parameter names match case-insensitively, so `class="..."` on a component is the `Class` parameter. The splat comes before the library's attributes, so `class` and `data-key` cannot be overridden. `Metric` and `MatchingCount` pass both through to the shared `MetricTile`, whose earlier `CssClass` became `Class`. The base class holds the two parameters and a `RootClass` helper, so a component adds them by using the helper on its root. **`InputClass` (added 2026-09-16).** `TextFacet`, `ValueFacet` (the search box) and `RangeFacet` (the slider's number inputs, passed on to `RangeSlider`) take an `InputClass` for the inner `<input>`, the one element a host realistically hands to a CSS framework. Each input renders a stable hook class (`l2d-text-input`, `l2d-facet-search`, `l2d-slider-input-from`/`-to`) that carries layout only, plus either the host's classes or, when none are given, the library's skin class `l2d-input` (font, padding, border, radius, background). Replacing the skin rather than appending to it is deliberate: scoped CSS appends an attribute selector to every rule, so a library rule on the hook class would outrank a single framework class such as `form-control` and the host would have to fight it. The range inputs of the slider are not affected; they are styled through the track. `CssClasses` holds the joining logic for both root and input classes.

9. **Collapsing (added after the first version).** Every facet component takes `Collapsible` (on by default since 2026-09-14; `false` gives a fixed header), which renders the title as a toggle with a chevron in the shared header, and a bindable `Collapsed` value with `CollapsedChanged`, so a host can set or remember which facets are open. Collapsed hides the body only; the header, including the clear link and a range facet's bounds, stays. The value is component-local UI state synced from the parameter the way the results page index is, so it survives state changes and works without a binding. A header template replaces the toggle, leaving `Collapsed` as the only control. **Name override (added 2026-09-15).** Every facet component takes an optional `Name`, as `Metric` already did, rendered by the shared header (and as the text input's accessible label) in place of the builder's title; the state, `ActiveSelections` and `StateSummary` keep the core's title (§C7 Decided). The page can localise a header without building one dashboard per culture. The whole chain was renamed from `Title` to `Name` the same day, builder method, `FacetState`, `MetricState`, `FacetInfo`, `MetricInfo`, `MetricTileContent`, the component parameters and `ShowFacetName`: `title` is one of the attributes the components pass through to their root, and a parameter called `Title` swallowed it. The CSS classes `l2d-facet-title` and `l2d-metric-title` keep their names since they describe the heading element.

10. **`TextFacet` (added 2026-09-14).** One text input in the shared header layout, bound to the facet's `TextSelection` through `Selections.With(key, new TextSelection(text))`; a cleared input or the clear link calls `Clear`. The component debounces: it sends the text after a pause (`DebounceMilliseconds`, 300 by default) or on Enter, since each new text costs a scan (§4.1), and the input shows the typed text meanwhile while the chip in `ActiveSelections` shows the applied one. The state's `ContextCount` is not rendered: an "Among N rows" line under the input was removed on 2026-09-15 as noise next to the matching-count tile; a page that wants it reads the state. Options: `Placeholder`, `DebounceMilliseconds`, `Collapsible`, `HeaderTemplate`, `InputClass`, `Class` and attribute passthrough like the other facets. `ActiveSelections` renders a `TextSelection` as one chip with the text, removal clears the facet.

### Code layout

Every component is a `.razor` file holding markup and directives only, with a `.razor.cs` partial class beside it holding parameters, state and methods. No `@code` blocks anywhere in the repository.

### State flow

`Calculate` runs inline in the click handler, then `StateHasChanged`. The component keeps no derived data of its own, so nothing can drift from the state. A back click is a state-cache hit and costs nothing.

---

## 10. Open questions

1. **Facets and metrics declared in Blazor markup.** Should `<Metric>` and the facet components be able to define what they show (aggregation and selector, or facet kind and selector), not only place something defined in the builder? Two routes were weighed on 2026-09-12: computing in the component over the matching rows (pure, but the UI calculates and pays per render; for facets that would be a second counting engine, which §C7 rules out), or registering the column lazily on the dashboard on first render (keeps the speed, but mutates a dashboard the design promises is immutable after `Create`, misses states already cached, and makes a page's markup part of the dashboard's definition). A third route is a markup-driven builder: a `Definition` fragment inside `DashboardView` whose children (`ValueFacetDefinition`, `RangeFacetDefinition`, `DateFacetDefinition`, `MetricDefinition`, `CountMetricDefinition`, `FixedFilter`, `SortDefinition`) register builder calls with a collector during the first render, after which the view runs `Dashboard.Create` once and then renders the placement components; the view cascades its type parameter so children need no `T`. This keeps the engine and its guarantees untouched and was prototyped on 2026-09-12 (facet and metric definition components were written, then removed unfinished). Discussed and parked the same day: the builder stays the primary API, the markup route is the one to add if any, and the one decision it needs is whether each view builds its own dashboard (right for WebAssembly) or a host can build once and share (needed on Blazor Server with large shared data; a `DashboardCreated` callback plus the existing `Dashboard` parameter would give both). Also noted: the core's internal `*Definition` classes would want renaming to avoid sharing names with the markup components.

### Decided

- **Parallel counting is off by default.** Counting facets one after another costs roughly 10 to 20 ms at the target; counting them on separate cores could cut that to a few milliseconds but occupies several cores per click, which can hurt a busy Blazor Server. Supported as a builder option, off unless the application turns it on. See §4.3.

- **String facets are case-insensitive by default.** `OrdinalIgnoreCase`; first-seen spelling is presented. Case-sensitive is a builder option. See §3.3.
- **Project renamed to `Linq2Dashboard` under `src/`.** First code change of the implementation. See §7.
- **Selections serialise to JSON only** in the first version; applications encoded the JSON for URLs themselves. Superseded after the first version by the query-string form below (2026-09-16), which sits beside the JSON and changes nothing in it. See §2.5.
- **Custom facets come later.** The facet interfaces are internal in the first version and become public when custom facets are added. See §6.
- **Text facets are a `FacetIndex` like the others, with a predicate scan.** `TextFacetIndex<T>` is the one generic index, since it needs the row array; `Present` returns a state with only the text. The scan runs the application's function over row chunks on several cores when parallel counting is enabled and serially otherwise, so the existing option stays the single switch for multi-core work. The always-explicit key and the `{ "text": ... }` JSON shape are fixed so a later precomputed text column can share them. Decided 2026-09-14. See §2.1, §2.2, §4.1, §9.
- **Blazor: Server primary, WebAssembly supported with smaller datasets.** See §7.
- **Blazor: plain CSS with custom properties, one cascaded formatter service, paging only.** See §9. Paging remains the default; virtualisation was added afterwards as an opt-in on `Results`, and a slider as an opt-in on `RangeFacet`.

- **Columnar only in the first version.** No per-value bitmaps. Same strategy for every cardinality; bitmaps are a later, benchmark-justified addition. See §1, §3.4, §4.1.
- **Range bounds are `double`.** The precision trade for `decimal` properties is accepted; range values are used only for filtering and bucketing, never for metrics. See §2.2, §3.3.
- **The dashboard is stateless.** `Calculate(selections)` is the only entry point; the UI owns the current `Selections`. See §2.3, §7.
- **Share of the total is a field on `MetricState`, not a metric kind.** Every state carries `Share`; the tile shows it. Decided 2026-09-14. See §2.2, §4.6, §9.5.
- **The default metric tile has no display switches; `ShowShare` is removed.** The default tile shows the name, the value and the share whenever the state has one, and the template context always carries every formatted piece, so a template never needs a default-tile parameter to receive data. Decided 2026-09-16. See §9.5.
- **Distinct count has its own code column and does not share a facet's.** Exact bit-set counting over dictionary codes, dictionary discarded after build. Decided 2026-09-14. See §3.3, §4.6.
- **Calculated metrics take a delegate over keyed values, not an expression or a fixed ratio shape.** Keys are checked by a dry run at definition; definition order is the dependency order. Decided 2026-09-14. See §2.1, §4.6.
- **Value labels are a row selector on the builder, evaluated once per distinct value at `Create`.** Not a value selector (the row form covers it, and reaches denormalised columns directly), not async (the dashboard reads its data once, synchronously; lookups happen before `Create`), and not a formatter concern (the label is data and must drive search). Decided 2026-09-14. See §2.1, §2.4, §3.3.
- **Metric tiles have no decoration parameters; a different look goes through `MetricTemplate`, which receives the formatted pieces.** A tile has no behaviour, so its default is a placeholder and the template is the real path. Decided 2026-09-14. See §9.5.
- **A scope is a `Dashboard<T>` made from another, not a `Calculate` argument.** `Where` on a built dashboard returns a new dashboard sharing rows, columns, indexes, sort order, serializer and selection row-set cache, with a `RowSet` as its "all rows" and per-facet and per-metric totals counted once over it. The pipeline is unchanged: the scope simply replaces the full set at the start of the prefix and suffix products. Chosen over a scope parameter on `Calculate` because every caller, the state cache and the Blazor context would otherwise carry a second axis of state; the Blazor package needed no change. Decided 2026-09-15. See §2.1, §3.1, §4.2, §4.3, §4.6, §5.
- **A scope from selections goes through the facet indexes and the row-set cache, not through a predicate.** `Where(Selections)` ANDs the selections' row sets with the current scope and reuses the scoped constructor, about twenty lines; presets freeze because the row set is taken now. Decided 2026-09-15. See §2.1, §4.1.
- **A template keeps the library's wrapper only when the wrapper carries behaviour; `MetricTemplate` renders no wrapper.** The tile's `div` carried nothing but style, so with a template the host's markup is the root and places its own hooks. Facet header and value templates keep their wrappers, since the facet root and the value button carry the click and the structure. Decided 2026-09-16. See §9.5.
- **Framework classes on inputs go through `InputClass`, which replaces the library's input skin instead of adding to it.** One parameter, on the three facets with an input, for the one inner element a host hands to a CSS framework; a headless mode or per-element class parameters everywhere were rejected. Decided 2026-09-16. See §9 (class and attribute passthrough).
- **Selections have a query-string form beside the JSON: one readable parameter per facet, hand-writable, with a caller-chosen prefix.** Chosen over a compacted JSON token because the point is links other pages and people can write, not shorter URLs. `ToQueryString` merges into an existing query so a page's own parameters survive. In Blazor the view does the sync itself, opt-in through `SyncUrl`, prefixed by its `Key`, since with the format and a key the earlier reasons to leave it to the host (parameter naming, collisions) fell away; the URL wins on first render and on navigation within the page, writes replace history, and only an interactive renderer writes. Decided 2026-09-16. See §2.5, §9.1.
- **`Dashboard<T>` is marked `[ImmutableObject(true)]`; hosting stays with the host.** The one thing the core does for lifecycle is tell general-purpose caches that the instance may be shared as is, which is what `HybridCache` needs to hold a dashboard without serialising it. Loading, expiry and rebuild are the host's code, shown in the walkthrough rather than shipped as a holder type or DI extension, until every consumer turns out to write the same class. Decided 2026-09-17. See §5.
