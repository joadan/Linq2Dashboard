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

    b.MultiValueFacet(x => x.Tags)                             // collection property: a row counts under every tag (§C5)
     .Top(20);                                                 // no "Other" (§C6)

    b.RangeFacet(x => x.Amount)
     .Buckets(0, 100, 500, 1000);                              // explicit boundaries, or .AutoBuckets(10)

    b.DateFacet(x => x.OrderDate)
     .TimeZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm"))
     .Granularity(DateGranularity.Month)                       // or leave it out: derived from the data (§3.3)
     .Presets(DatePreset.Today, DatePreset.Last7Days, DatePreset.ThisYear)
     .SkipEmptyPresets();                                     // leave out a preset no row falls in (§C5)

    b.TextFacet("search", (x, text) =>                          // free text, matched by the application (§C5)
        x.CustomerName.Contains(text, StringComparison.OrdinalIgnoreCase)
     || x.Product.Contains(text, StringComparison.OrdinalIgnoreCase))
     .Name("Search");

    b.CountMetric("orders");
    b.SumMetric("revenue", x => x.Amount);
    b.AverageMetric("average", x => x.Amount).Name("Average order");
    b.DistinctMetric("customers", x => x.Customer);                  // different non-null values; optional comparer
    b.CalculatedMetric("perCustomer", m => m["revenue"] / m["customers"]);   // formula over earlier metrics; null in, null out

    b.OrderByDescending(x => x.OrderDate)                      // application-defined sort (§C8)
     .ThenBy(x => x.Id);

    b.UseTimeProvider(TimeProvider.System);                    // default; tests pass a fake
});
```

- `Dashboard.Create` enumerates the source exactly once, applies fixed filters, and builds every column and index. After it returns, the dashboard is immutable and thread-safe.
- Every builder method validates eagerly. Duplicate keys, a non-member selector without an explicit key, or a range facet over a non-numeric property throw at `Create`, not at first use. The builder and every facet builder refuse further configuration once the dashboard is built.
- Metrics are builder methods (`CountMetric`, `SumMetric`, `AverageMetric`, `MinMetric`, `MaxMetric`, `DistinctMetric`, `CalculatedMetric`) rather than a separate `Metric` factory, because C# cannot infer `T` for `Metric.Sum(x => x.Amount)` outside the builder. They carry the `Metric` suffix for the same reason the facet methods carry `Facet`: on the builder completion list a bare `Sum` or `Count` reads as a LINQ operator over the builder rather than as a declaration, and metrics were the one group of builder methods that did not say what they defined (renamed 2026-09-18). `DistinctMetric` takes any equatable property and an optional `IEqualityComparer<TProp>`, with the value facet default (case-insensitive strings) when none is given. `CalculatedMetric` takes a `Func<MetricValues, double?>`; `MetricValues` exposes the values and shares of the metrics defined before it by key and throws on any other key. The builder runs the formula once at definition with every input at null, so an unconditionally read wrong key fails in the builder call; a key first read inside a branch fails at the first `Calculate` that reaches it, with the key in the message.
- Range and metric selectors accept any numeric property, nullable or not; the conversion to `double` is compiled into the selector. Date selectors accept `DateTime`, `DateTimeOffset`, `DateOnly` and their nullable forms; anything else is rejected at `Create`.
- `Buckets(100, 500, 1000)` names cut points, not edges: it yields "below 100", "100 to 500", "500 to 1000" and "1000 and above", so every value lands in a bucket. `AutoBuckets(count)`, the default with ten, derives round cut points from the data at build (§3.3, §C5); `count` is approximate, since a round step rarely divides the data's span evenly.
- `TextFacet` (§C5, added 2026-09-14) always takes an explicit key, since there is no selector to derive one from, and a `Func<T, string, bool>` that must be pure and thread-safe. The builder offers `Name` only; matching semantics live in the function.
- `MultiValueFacet` (§C5, added 2026-09-20) takes an `Expression<Func<T, IEnumerable<TItem>?>>`, so the item type is inferred from the collection. It is a method of its own rather than an overload of `ValueFacet` because `string` is `IEnumerable<char>`: an overload would make every string facet ambiguous, or silently a facet over characters. Its builder has the value facet's options, with `Label` reading the value (`Func<TItem, string?>`) since a row has several.
- All facet builders return a typed builder so kind-specific options are discoverable; the shape above is the whole configuration surface for the first version.

Scoping a dashboard (§C4.10, added 2026-09-15):

```csharp
Dashboard<Order> nordic = dashboard.ScopeTo(x => x.Region == "Nordic");   // a scoped dashboard
Dashboard<Order> open = nordic.ScopeTo(x => x.Status == "Open");           // scopes compose
Dashboard<Order> view = dashboard.ScopeTo(selections);                      // the same, in the facets' own terms
```

- `ScopeTo` on a built dashboard returns a new `Dashboard<T>` over the rows that pass the predicate. It behaves exactly like a dashboard built with the predicate as one more fixed filter: `TotalCount`, every total count, "Other" and every metric share are measured against the subset, and a value no row in the subset has is not listed. The one difference is that range and date buckets, and a range facet's `Min` and `Max`, are the parent's.
- It costs one predicate call per row plus one count per facet and one aggregation per metric (§4.3, §4.6), not a build: rows, columns, indexes, the sort order, the serializer and the selection row-set cache are shared with the parent (§5). Measured in §8.
- The parent is unchanged. Both are immutable and thread-safe, take the same `Selections`, and share one `Serializer`, so a bookmark applies to any scope. A host that switches between scopes keeps the scoped dashboards it has made; each has its own state cache.
- `ScopeTo(Selections)` (added 2026-09-15) resolves each selection through its facet index and the shared row-set cache (§4.1, §5), ANDs the sets with the current scope and builds the same scoped dashboard. No predicate runs and no row object is read, so a scope the user has just clicked costs only the counts. The facets' matching rules apply, a relative date preset is resolved once, now, and an unknown key throws as in `Calculate`. The scoped dashboard starts from `Selections.Empty`; because the scope is part of "all rows" rather than a selection, a scoped facet lists only the values in scope and gets no own-facet exclusion.

### 2.2 Selections

`Selections` is an immutable, non-generic map from facet key to a selection. It is the only thing the UI sends back.

```csharp
var selections = Selections.Empty
    .With("Country", ValueSelection.Of("SE", "NO"))
    .With("Amount",  RangeSelection.Between(100, 500))
    .With("OrderDate", DateSelection.Relative(DatePreset.Last30Days));

// convenience for the click case
selections = selections.Toggle("Country", "DK");                        // add if absent, remove if present
selections = selections.ToggleInterval("Amount", bucket.ToInterval());  // the same for a bar
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

sealed record RangeInterval(
    double? From, double? To,
    bool FromInclusive = true, bool ToInclusive = true);
//  null bound = unbounded on that side; RangeInterval.Between / AtLeast / AtMost

sealed record RangeSelection : Selection            // new RangeSelection([low, high]); RangeSelection.Between(100, 500) for one
{
    IReadOnlyList<RangeInterval> Intervals;         // a set, OR-ed (§C4.1): order does not matter for equality
    bool IncludeNull;                               // the null rows as one more part (§C4.8)
    //  Toggle(interval) / ToggleNull() for the click case; Empty, OnlyNull; IsEmpty clears the facet
}

sealed record DateInterval                          // one part: absolute (From/To) or relative (Preset); built via factories
{
    DateTimeOffset? From;    // inclusive        DateInterval.Between(from, to)
    DateTimeOffset? To;      // exclusive
    DatePreset?     Preset;  // resolved with TimeProvider at Calculate (§C5)   DateInterval.Relative(preset)
}

sealed record DateSelection : Selection             // new DateSelection([march, recent]); DateSelection.Between / Relative for one
{
    IReadOnlyList<DateInterval> Intervals;          // a set, OR-ed, intervals and presets mixed
    bool IncludeNull;
    //  Toggle(interval) / ToggleNull(); Empty, OnlyNull; IsEmpty clears the facet
}

sealed record TextSelection(string Text) : Selection;   // trimmed; whitespace-only is empty and clears the facet (§C5)
```

`Selections` is keyed by facet key, compares by value, and treats an empty selection of any kind as "clear": no value, no text, or no interval without the null rows. `Toggle` uses default equality on the boxed value; the facet's comparer applies when values are mapped to codes, so `"se"` and `"SE"` may both sit in a selection and still select the same rows. `ToggleInterval` is the same click for a range or date facet: it adds or removes one `RangeInterval` or `DateInterval` in that facet's set, starting from `Empty` when the facet has no selection, and clears the facet when the last part goes unless the null rows stay selected. It is a separate name because `Toggle(key, null)` must keep meaning the null value of a value facet. A slider does not toggle; it replaces the set with its one interval through `With`.

**Interval sets (2026-09-19).** Until then a range or date selection was one interval, and a second bar click replaced the first. The records now hold a set of parts (§C5), toggled like values, and the null rows are a flag beside the set rather than a third form; `OnlyNull` is the empty set with the flag on. The matching set is the union of the parts' row sets (§4.1), a bucket is selected when any part covers it (§4.5), and the JSON and query forms gained a list form while the one-interval forms stayed as they were (§2.5). Bucket identities never enter a selection, so a saved selection survives a change of bucket boundaries.

**Canonical interval sets (2026-09-21).** The constructor of `RangeSelection` and `DateSelection` normalises: absolute intervals are sorted by start and every interval that overlaps or abuts the next is joined into one, so `Intervals` is ascending and disjoint and equality is a sequence comparison. Two half-open buckets `[100, 200)` and `[200, 300)` join at their shared edge; `[100, 200)` and `(200, 300)` do not, since neither holds 200. `Contains(interval)` is coverage, the same test that lights a bucket (§4.5): true when one canonical interval holds every point of the argument. `Add` appends and renormalises unless already covered; `Remove` is set difference, carving the argument's points out of whatever it overlaps and splitting an interval it sits inside, so the two pieces keep the right ends (`[150, 350]` minus `[200, 300)` is `[150, 200)` and `[300, 350]`); `Toggle` is `Contains ? Remove : Add`, which makes the click on a covered bar remove exactly that bar and the click on any other bar add it. A date preset is a part of its own: `Contains`, `Add` and `Remove` work by value on presets, they sort after the intervals in enum order, and an interval never absorbs one, since a preset's instants are known only at calculation. A consequence for presets (§2.4): "This month" lights while the March bar is a part of its own and goes dark once April joins it into "March to April", which is what the selection then says.

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

    IReadOnlyList<T> GetItems(long skip, int take);   // a slice of the matching rows
    IReadOnlyList<T> Items { get; }         // all matching rows in sort order; counted and indexable, for a grid or export
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
```

`Value` is `object?` on purpose. The UI formats it; the core does not know about cultures, which is also why buckets carry bounds rather than label strings. The one string the core carries is the application's own label for a value facet's value (§C5), supplied by the builder's `Label` selector: it is data read from the rows, not formatting, and it is on `FacetValue.Label` for presented values and behind `LabelOf` for any value, so the chip for a selected value can be named from the selection alone. The default formatter shows it when present and formats the value otherwise. Boolean facets reuse `ValueFacetState`. `FacetKind` has `Value`, `Boolean`, `Range` and `Date` in the first version, and `Text` since 2026-09-14.

A bucket is `Selected` when one of the selected intervals fully covers it, so a wide interval lights up several buckets, a partial one lights up none, and two bar clicks light two bars whether or not they joined into one interval (§2.2). A preset is `Selected` when one part of the selection is that preset or an absolute interval exactly equal to the preset's interval (clicking the March bar alone lights "This month"); coverage would light every preset inside a wide selection, which reads wrong, so a joined "March to April" lights neither. `ToInterval()` on a bucket or preset gives exactly the part a click should toggle, and `ToSelection()` that part alone, so the UI never constructs interval bounds itself.

### 2.5 Serialising selections

```csharp
string json = dashboard.Serializer.ToJson(selections);
Selections restored = dashboard.Serializer.FromJson(json);
```

```json
{
  "Country":   { "values": ["SE", "NO"] },
  "Amount":    { "from": 100, "to": 500, "toInclusive": false },
  "Weight":    { "intervals": [ { "to": 1, "toInclusive": false }, { "from": 50 } ], "includeNull": true },
  "OrderDate": { "preset": "last30Days" },
  "Created":   { "intervals": [ { "from": "2026-01-01T00:00:00.0000000+01:00", "to": "2026-02-01T00:00:00.0000000+01:00" }, { "preset": "last7Days" } ] },
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
  - A range facet is a comma-separated list of intervals, like a value facet's list of values, each `from..to`; an empty bound is unbounded (`100..`, `..500`). Both ends are inclusive without brackets; when an end is exclusive the interval is written in bracket notation, `[100..500)`, which is what a bucket click produces. A single number is the closed interval at that value. `null` as an item adds the null rows (`[..100),1000..,null`); `null` alone is only the null rows. `..` with no bound is not an interval and drops the selection.
  - A date facet is the same list of parts, each the preset name in camelCase (`last30Days`, read case-insensitively) or `from..to` in the shortest ISO 8601 text that round-trips: seconds and fraction only when present, `Z` for a zero offset. `null` as for ranges. Interval text never contains a comma, so no escaping is needed.
  - A text facet is the text itself.
  - Encoding keeps the URL readable: only `& = + # %`, space, quotes and non-ASCII are percent-encoded, so `,` `..` `:` `[` `]` and `/` stay as they are. Reading accepts a query string with or without `?`, or a whole URL, decodes `+` as a space, and forgives a hand-written `+` in an offset or exponent that arrived as a space.
  - Reading is as lenient as JSON: a parameter that is not a facet key is ignored, a value that does not parse drops that facet, an empty value clears it, and the last of several parameters with the same name wins.
  - **Prefix.** Both methods take a prefix, prepended to every facet key, so two dashboards on one page (`o.Country`, `r.Country`) and a page's own parameters (`rows`, `tab`) never collide. The prefix is a plain string, not parsed, so a facet key may contain anything.
  - **Composition.** `ToQuery` returns every facet as a dictionary entry, `null` for an unselected one, the shape `NavigationManager.GetUriWithQueryParameters` takes. `ToQueryString` takes an optional existing query or URL and keeps, verbatim and in place, every parameter in it that is not one of the dashboard's facets under the prefix, so a page's parameters survive and the facets' are replaced or removed. `FromQuery` reads already decoded pairs.
  - The Blazor view's `SyncUrl` (§9.1) is a thin client of these methods; a host that owns its URL, or a non-Blazor host, calls them directly.
- Each facet writes and reads its own shape. Value facets write values as JSON primitives: strings, booleans and numbers as themselves, enums by name, `Guid` and the date and time types as ISO 8601 strings. Reading brings a primitive back to the facet's value type, so `"5"` or `5.0` reads into an `int` facet and `"store"` into an enum facet; booleans and numbers do not convert into each other. The builder's `Serialize(format, parse)` replaces the default with an application-supplied string form for a facet.
- Null in a value selection is JSON `null`. Omitted interval bounds mean unbounded. Omitted flags take the defaults from §2.2, so the common case reads cleanly. Presets are written in camelCase and read case-insensitively. Instants keep their offset.
- A range or date selection with one part writes it flat, as above; with several it writes an `intervals` array of the same objects, `includeNull` beside it. Reading accepts either, drops an element that cannot be read and keeps the rest, and reads the flat form with no bound and no preset as nothing: `{ "includeNull": true }` alone is the null rows only, the same as `{ "onlyNull": true }`, which is still what nulls-only writes. An explicitly unbounded interval, "every row with a value", is `{ "intervals": [ {} ] }`.
- Unknown facet keys, values that cannot be read, and shapes that do not fit the facet's kind are dropped, not thrown. A stale bookmark degrades to "fewer selections", never to an error page. Only text that is not JSON at all throws.
- The serializer is built by the dashboard because parsing needs each facet's value type. It is otherwise stateless and exposed as `dashboard.Serializer`. `ToJsonObject` and `FromJsonObject` work on `System.Text.Json.Nodes` for callers that embed selections in a larger document.

---

## 3. Internal data model

### 3.1 Rows

```text
source  --Where(fixed filters)-->  T[] _items      row id = array index, 0..N-1
```

Rows that fail a fixed filter are dropped at build time, so ids are dense over the dataset and every count in the system is relative to the dataset (§C4.3). A scoped dashboard (§C4.10) keeps the same array and ids and carries a `RowSet` naming the rows in scope; its counts are relative to that set. The dashboard keeps `_items` for `Items` only, the rows a grid shows or an export walks. Nothing else touches the objects after build.

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

**Multi-value column** (multi-valued facets, added 2026-09-20):

```text
int[]     offsets      rowCount + 1; the codes of row r are codes[offsets[r]..offsets[r + 1]]
int[]     codes        one per (row, distinct value) pair, flat, never 0
TValue[]  dictionary   as the value column
int[]     totalCounts  per code; index 0 counts the rows with an empty slice
```

- A row's collection is read once; null items and the row's repeats of a value (under the facet's comparer) are dropped, and the remaining occurrences are coded by a `ValueColumn` built over them, so equality and first-seen spelling have one implementation. A row with an empty slice is the null value.
- Cost is 4 bytes per row plus 4 per occurrence. Counting (§4.3) walks each context row's slice, so it is proportional to the occurrences in the context rather than to the rows; `RowsWithCodes` stops at the first selected code in a row.
- `ValueFacetIndex` sees both columns through `IValueColumn<TValue>` (dictionary, totals, `RowsWithCodes`, `CountInto`). Nothing else in the pipeline knows the kind.

**Range column**:

```text
double[]  values       converted once with generic math; NaN where null
RowSet    nulls
int[]     bucketCodes  0 = null, 1..B = bucket index + 1, fixed at build (§C5)
double    min, max     dataset bounds
```

Derived buckets (`AutoBuckets`, §C5, re-decided 2026-09-20) are resolved once in `RangeBucketing.Auto` from the finished value array. The body of the distribution is the 2nd and 98th percentile of the non-null values, read from a stride sample of at most 100 000 values so a million-row column costs one sort of 100 000 doubles, a few milliseconds and deterministic for the same data. The step is `body / count` snapped to the 1, 2, 5 series times a power of ten on a log scale (the thresholds are √2, √10 and √50, as charting libraries pick axis ticks), so the bucket count comes out between about 0.6 and 1.6 times the request. The inner edges are the multiples of the step from the one at or below the low percentile to the one at or above the high, each rounded to the step's decimals so 0.6 is not 0.6000000000000001; an infinite edge is added below only when the minimum lies under the first inner edge, and above only when the maximum lies over the last, so a dataset that starts at 0 has no empty "below 0" bar. When the two percentiles coincide (heavy ties) the step is taken over the minimum and maximum instead; a column with no values gets no buckets, and one with a single distinct value gets one closed bucket. Equal widths between minimum and maximum, the rule until 2026-09-20, gave edges like 12.37 and one tall bar on skewed data; quantile edges (equal counts, snapped to round numbers) and a log scale (1, 2, 5, 10, 20, 50) were considered and left for a dataset that needs them, the log scale as a later explicit option.

Bounds arrive from `RangeSelection` as `double`. Comparisons happen in `double`. For `decimal` properties this is a deliberate precision trade: a UI slider does not carry more than double precision, and the converted values are used only for filtering and bucketing, never for metrics, which read their own column. That column is `double` too (§2.1), so a metric over a `decimal` property is computed in double as well. The sum is compensated (§4.6), so the error over a million rows stays at about one unit in the last place of the result, and a two-decimal source total rounded to two decimals by the formatter is exact until it passes 2^53. Exact money arithmetic is left to the host's own reports.

**Date column**:

```text
long[]    ticks        UTC ticks after conversion into the facet's zone rules (§C5); long.MinValue where null
RowSet    nulls
int[]     bucketCodes  period index at the configured granularity, 0 = null
long[]    bucketStarts start tick of each period present in the dataset
```

Period boundaries are computed in the facet's time zone once, at build. When the definition names no granularity, `DateGranularities.Auto` (added 2026-09-20) picks one from the finished tick array before the periods are computed: the 2nd and 98th percentile of the non-null instants, from the same stride sample of at most 100 000 values as the range facet, are converted into the zone and their distance in calendar days is divided by the average length of a day, week, month (365.2425 / 12), quarter and year; the finest period whose estimated count stays within the definition's ceiling, 30 by default, is taken, and a span beyond even years still gets years. The estimate uses the span rather than the periods present, so a sparse column is measured by the distance its bars cover. The build reads the instants in one pass and computes period starts in a second, since the period is not known until the first is done; the cost is the same conversion work as before plus one sort of the sample. The column carries the resolved granularity, so the state, the formatter and the presets never see that it was derived. A preset such as `Last7Days` is resolved at `Calculate` by asking the `TimeProvider` for now, converting into the facet zone, and snapping to day boundaries in that zone.

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
- Range: one scan of `values` per interval in the selection, setting a bit where the value is inside it, OR-ed together (§C4.1). OR in `nulls` if `IncludeNull`; no interval and the flag on is `nulls` alone.
- Date: for each part, resolve a preset to `[from, to)` ticks if needed and scan `ticks`; OR the parts together. OR in `nulls` if `IncludeNull`. A selection of several bars costs one scan per bar, about 1 ms each at a million rows, and the union is a word-wise OR.
- Text (§C5): scan `_items` and set a bit where the application's function returns true for `(items[row], text)`. The only scan that touches row objects and runs application code, so it is the one scan whose cost the library does not control. When parallel counting is enabled it is split across cores over word-aligned chunks of 64 rows, which the contract (pure, thread-safe) allows; otherwise it runs serially like the other scans, so the option keeps its meaning of "this dashboard may use several cores per click". The result is cached like any other row set, so retyping a text or removing and re-adding it costs nothing.

Each `R_f` is cached by `(facetKey, selection)` (§5). The scans are `O(N)` with sequential access, roughly 1 ms per million rows. `ScopeTo(Selections)` (§C4.10) produces its scope from the same `R_f` sets through the same cache, so scoping by selections adds no scan of its own.

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

`counts[0]` is the null value's filtered count (§C4.8). A multi-value column increments every code in a context row's slice and `counts[0]` for an empty slice, so its counts sum to at least |C_f| (§C5). Total counts come from the column and are never recomputed. A scoped index (§C4.10) carries its own totals array, counted once over the scope with this same loop when `ScopeTo` is called, and presents against it; the column is shared with the parent. A value facet also counts how many codes have a non-zero total, which is the scope's value count, and skips codes with a zero total when presenting and searching, so a value absent from the scope is never listed.

Facets are independent at this stage and may be counted in parallel. Parallelism is a builder option, off by default until benchmarks say otherwise.

### 4.4 Presentation of a value facet

From `counts`, build the presented list (§C6):

1. Start with every selected value, regardless of rank.
2. Fill up to `N` with the highest-ranked remaining values, ranking by filtered count or total count per the facet's `RankMode`. Zero-count values are eligible and are included if they rank (§C4.3).
3. Ties break by total count, then by dictionary order, so the order is deterministic.
4. `Other.FilteredCount = |C_f| − Σ presented filtered`, `Other.TotalCount = N_dataset − Σ presented total`. Omitted when nothing was truncated, and always for a multi-valued facet (§C6).

Selecting the top `N` from `V = 100 000` counts is a partial sort, `O(V)` expected. The full `counts` array is retained inside the state to serve `Search` without recounting.

### 4.5 Range and date facets

Buckets are counted through `bucketCodes` exactly like value facets. Each `Bucket` gets `Selected = true` when one of the selected intervals fully covers it, presets resolved first, so two bar clicks light two bars. `Null` reports `counts[0]`. For date facets, each configured preset is resolved and counted against `C_f` as well, so the UI can show "Last 7 days (312)" without a round trip. With `SkipEmptyPresets`, a preset whose total over the dataset (or the scope) is zero is not added to the state at all, selected or not, which is the presets' counterpart of the `totals[code] > 0` gate the value facet already applies; it is re-decided here on every calculation, since the interval follows the clock, and it saves no work because the scan is what discovers the count.

### 4.6 Metrics

One pass over `M.Rows()` per metric column, accumulating sum, count-of-values, min and max in a single loop when several metrics share a column. The sum uses Neumaier compensated summation, so its error does not grow with the number of rows added. Count is `M.Count`. A metric with zero contributing values reports `null` (§C4.4).

**Share of the total (§C4.4, added 2026-09-14).** `MetricColumn` already keeps the aggregate over every row from the build, so the share costs one division per metric: `M.Count / N` for count, `Sum(M) / Total.Sum` for sum. Average, min and max get `null`, as does a sum without contributing rows and any share whose total is zero. `MetricIndex.Present(matching)` produces the whole `MetricState`, so the value and its share cannot disagree. A scoped metric index (§C4.10) holds the aggregate, row count and distinct count over the scope, computed once by `ScopeTo` with the same passes a calculation makes, so shares in a scope are against the scope.

**Distinct (§C4.4, added 2026-09-14).** One pass over `M.Rows()` reading the distinct column's code per row and marking it in a bit set of V bits; the count of newly marked bits is the answer, and the full dataset answers with V without a scan. The share is that count over V. With no non-null value in the matching rows both are `null`. At a million rows this is the same sequential scan as facet counting, so it sits inside the per-click budget; the bit set is 12.5 KB for a 100 000-value customer column and is allocated per calculation.

**Calculated (§C4.4, added 2026-09-14).** Metrics are presented in definition order into one `MetricState[]`; a calculated metric receives a `MetricValues` over the states filled so far (a struct holding the array and the count, no allocation) and applies its formula. Lifted nullable arithmetic gives null-in-null-out for free; the result is then kept only if `double.IsFinite`, so division by zero and NaN become `null`. No column, no row work, no share. A formula that throws propagates: it is application code with a bug, not data.

### 4.7 Matching rows

```csharp
IReadOnlyList<T> Items { get; }
IReadOnlyList<T> GetItems(long skip, int take)
```

The rows are shaped for a data grid, since that is what shows them (§C3, §C8): the grid pages, virtualises and sorts, and the state must not make each of those a pass over `N`. `Items` is a read-only list (`MatchingItems<T>`, internal) whose `Count` is `M.Count` and whose indexer reads the item at the `i`-th ordered matching row. The ordered row indexes, an `int[]` of `M.Count`, are built on first indexed access or enumeration and kept for the life of the state: one walk over `sortedRows` testing membership in `M`, or over `M.Rows()` when `sortedRows` is identity: 4 bytes per matching row and, at 1 M rows with 437 000 matching, 4 to 8 ms (§8). Two threads racing on the first access build equal arrays and the last one wins, which is harmless. The count needs no materialisation, so a state that is never shown as rows never pays.

The list also implements `IList<T>`, read-only, because that is what LINQ's fast paths check for: `Count()` reads the count, `Skip` and `Take` index into the list instead of walking, and `OrderBy(...).Skip(...).Take(...)` buffers through `CopyTo` and selects rather than fully sorting. A grid given `Items.AsQueryable()` runs those very calls through `EnumerableQuery`, so a page, a viewport or a sorted slice costs the slice plus, for a sort, one pass over the matching rows. `GetItems(skip, take)` is the same slice as a named method, for a host that pages by hand or over HTTP. Rows are answered from the state without touching the dashboard's state, of which there is none (§5).

Sorting for display is the grid's, over the matching rows it was given; the core's order is the application's `OrderBy` from the builder and nothing else (§C4.9).

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
- **States are immutable** and hold their own arrays. Rendering one state while the next is calculated is safe. A state keeps a reference to the dashboard for `Items` and `Search`; it never mutates it.
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
        DashboardView.razor
        Facets/  Metrics/               facets, chips, tiles; the rows go to the host's grid (§9)
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
Render(state);                                  // Facets, Metrics, MatchingCount, Items
bookmark = dashboard.Serializer.ToJson(selections);
```

What the UI reads from the state, per facet kind:

| Kind | Renders | Click produces |
|---|---|---|
| `ValueFacetState` | `Values` (value, total, filtered, selected), `Other`, `Search(text)` | `Toggle(key, value.Value)` |
| `RangeFacetState` | `Buckets` as a histogram, `Null` beside it, `Min`/`Max` for a slider | `ToggleInterval(key, bucket.ToInterval())`, or `new RangeSelection(from, to)` from a slider |
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
6. `Items`: the first slice of a fresh state (the order is materialised), a slice at the end, and a sorted slice through `AsQueryable`, which is what a grid does per fetch.
7. Everything above with parallel counting on and off.

**Targets**: `Create` under 2 s. Warm `Calculate` under 50 ms in every scenario. Peak managed memory reported per facet kind. If a scenario misses its target, the per-facet timings say whether per-value bitmaps (§3.4) would help before any are added.

### Measured, first version

Recorded 2026-09-17 for the 1.0 candidate; the first recording, 2026-09-14 to 15, is in the git history of this file and every
number moved by less than ten per cent. Intel Xeon W-2223 (4 physical cores, 8 logical), Windows 11 24H2, .NET SDK 10.0.400,
runtime 10.0.11, BenchmarkDotNet 0.15.8 short job (3 warmups, 3 iterations, 1 launch), 1 000 000 rows. Run with
`dotnet run -c Release --project benchmarks/Linq2Dashboard.Benchmarks -- --job short --filter *` and `-- --memory`.
The short job's error bars are wide, so treat differences under about ten per cent as noise.

| Scenario | Sequential | Parallel counting | Target |
|---|---|---|---|
| `Create`, full dashboard | 1 087 ms | | < 2 s ✓ |
| `Create` without sort order | 590 ms | | |
| `Create`, date facet only | 193 ms | | |
| `Create`, customer facet only (100 000 values) | 159 ms | | |
| `ScopeTo`, scoped dashboard over about 80 % of the rows (added 2026-09-15) | 24 ms | | 526 KB allocated |
| `Calculate`, no selection | 3.7 ms | 2.0 ms | < 50 ms ✓ |
| `Calculate`, 1 facet, warm | 10.9 ms | 5.4 ms | ✓ |
| `Calculate`, 3 facets, warm | 10.1 ms | 5.2 ms | ✓ |
| `Calculate`, 5 facets, warm | 5.3 ms | 3.8 ms | ✓ |
| `Calculate`, range + date interval, warm | 9.5 ms | 5.9 ms | ✓ |
| `Calculate`, 3 facets, cold caches | 21.9 ms | 16.2 ms | ✓ |
| `Calculate`, range + date, cold caches | 22.4 ms | 17.5 ms | ✓ |
| A click: 3 facets cold, then one value toggled | 35.0 ms | 26.0 ms | ✓ (the click alone is the difference, ~13 ms) |
| `Search` over 100 000 customer labels | 5.6 ms | | |
| `Calculate`, new text in a text facet, cold (two `Contains` per row) | 76.6 ms | 19.7 ms | see below |
| `Items`, first slice of a fresh state, `Calculate` included (replaces `GetPage`, 2026-09-19) | 18.3 ms | 8.8 ms | 4 to 8 ms above the warm `Calculate` to materialise the order; 1.7 MB for 437 000 matching rows |
| `Items`, a slice at the start / at the end, order materialised | 0.16 / 0.24 µs | | |
| `Items.AsQueryable().OrderBy(…).Skip(…).Take(50)`, a grid's fetch with a sort column | 12.8 ms | | one pass over the matching rows, 4 MB; without a sort column a fetch is the slice alone |

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

- **A scope costs about 3 % of a build and under 1 % of its memory** (added 2026-09-15): 24 ms and 526 KB against 1 087 ms and 78 MB. One predicate call per row, one counting pass per facet, one aggregation per metric; the row set is 125 KB and the rest is count arrays. A host can afford a scope per tenant, per user or per tab.
- **The sort order is half of the build.** About 500 ms of the 1 087 ms is `Array.Sort` over a million row ids through a delegate comparison. A key-specialised sort (materialise the key into a primitive array and sort indices by it) would likely halve that. Not needed for the target; first candidate if build time matters.
- **The date facet is the next build cost** at about 190 ms, from one time zone conversion per row. Caching the offset per calendar day would remove most of it.
- **Selection scans cost about 3 ms each**, three times the estimate, because the value scan sets bits one at a time through a range-checked builder. A word-at-a-time scan would bring it to the estimate. Only cold calculations pay this.
- **Date presets are rescanned on every calculation**, about 1 ms per preset, because their interval depends on "now". Caching the preset row set keyed by its resolved interval would make them free until midnight.
- **Parallel counting gives 1.5 to 2× on four cores** for warm calculations. The default stays off as decided; the option is worth turning on for a desktop or single-user host.
- **The no-selection state is unusually cheap** because a full context copies the precomputed totals instead of counting. It is not representative of a click.
- **A text facet's scan is the one cost the library does not own** (added 2026-09-14). Two case-insensitive `Contains` over a million rows take about 80 ms serially and 20 ms with parallel counting on four cores, so a new text is four to eight times a cold click and the Blazor input debounces. Only a new text pays; the row set is cached by the text afterwards. An application with a heavy predicate or a large dataset should turn parallel counting on, or precompute what the predicate reads.

### Measured, small datasets built per visit

Recorded 2026-09-26, same machine and toolchain, BenchmarkDotNet medium job. The question is whether a dashboard can be built on every page visit, with no cache, when the dataset is small: the same §8 definition (eight facets, a text facet, three metrics, a sort order) over the first rows of the generated dataset. Run with `-- --job medium --filter *SmallCreate*`.

| Rows | `Create` | `Create` + first `Calculate` | Allocated by `Create` | `Calculate`, 3 facets, cold caches |
|---|---|---|---|---|
| 0 | 1.7 ms | 1.8 ms | 93 KB | 3 µs |
| 1 000 | 2.6 ms | 2.6 ms | 432 KB | 78 µs |
| 10 000 | 9.6 ms | 10.5 ms | 2.7 MB | 0.4 ms |
| 100 000 | 90 ms | 98 ms | 16 MB | 3.0 ms |

- **The fixed cost is 1.7 ms**: what a definition costs with no rows, mainly compiling the selectors. It dominates only below about 1 000 rows, where the whole build is under 3 ms anyway. The worry that compilation would make small builds expensive does not hold.
- **Above that the build is linear, at 0.8 to 0.9 µs per row**, a little under the rate at a million rows (1 087 ms, where the sort's n log n weighs more). Up to about 10 000 rows a build per visit is around 10 ms, well inside a page request; at 100 000 it is about 100 ms, noticeable but not by itself a reason to cache.
- **A click stays proportionally cheap**: a cold three-facet calculation is 3 to 4 % of a build at every size.

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
- **No results component.** The matching rows go to the host's grid: the view's child content is a template over the `DashboardContext<T>`, whose `Items` is one `IQueryable<T>` per state, and the state's list does the rest (§4.7). `Results<T>` shipped in the first version and was removed 2026-09-19; see component 4.

### Components, in build order

1. **`DashboardView<T>`** (named so because `Dashboard<T>` is the core type) takes the `Dashboard<T>` and a bindable `Selections`. Owns the current selections and state, raises `SelectionsChanged` so a host can bookmark through the serializer, and cascades a `DashboardContext<T>` holding dashboard, selections, state and formatter. Every child derives from `DashboardComponentBase<T>`, which receives the context and re-renders on its `StateChanged` event; the context's `ToggleAsync`, `SelectAsync`, `ClearAsync` and `ClearAllAsync` are the only ways a click changes anything. The three selector facets share `FacetComponentBase<T>`, which resolves the facet key from `Key` or `For` (added 2026-09-22): `For` is an `Expression<Func<T, object?>>`, so the component needs no second type parameter and `For="x => x.Amount"` compiles, boxing the member, and `FacetKey.Of` looks through the conversion and derives the key by the builder's rule (§C7). The base also builds the wrong-kind error, which names the facet's kind and the component that renders it, and the core's unknown-key error lists the known keys with a case-only match pointed out. **`StateChanged` on the view (added 2026-09-14):** an `EventCallback<DashboardState<T>>` raised after every calculation, the initial one included, so a host can render its own chart or summary from the result without a `@ref` to the context. On a click it follows `SelectionsChanged` (cause, then result); for selections set through the parameter it is the only callback, since `SelectionsChanged` never echoes the host's own value back. The calculation is synchronous, so by the time either callback runs the context already holds the new state. **One context per view (fixed 2026-09-16):** the view creates its `DashboardContext<T>` once and cascades it as fixed; when the host passes another `Dashboard`, typically a scope of the first (§C4.10), or another `Formatter`, the same context adopts it, recalculates with the current selections and raises `StateChanged`. Until then the view built a new context, which the components inside never saw: they subscribe once, in `OnInitialized`, and a fixed cascading value is not re-delivered, so a scope switch in the sample left every metric tile at the parent's numbers. The docs demo uses it for an orders-per-month chart above the results, drawn by the page from the date facet's buckets and clickable back into the selections through the view's `Context`. **`Key` and `SyncUrl` (added 2026-09-16):** `Key` is the view's identity on the page, rendered as `data-key` on the root (not as `id`, which stays a pass-through attribute; `Name` was rejected because it means display text on every other component). `SyncUrl`, off by default, keeps the selections in the page URL in the query-string form of §2.5, prefixed `key.` when a key is given, so two views on one page stay apart. Rules: on first render the URL wins when it holds selections for the view, and `SelectionsChanged` reports them so a host binding `Selections` learns; otherwise the parameter applies and is written. Every change, a click, a host-set parameter or a new dashboard, replaces the URL in place (`replace: true`, so the back button leaves the page rather than undoing clicks; push history can be an option later), keeping the page's other parameters verbatim through `ToQueryString`'s existing-query argument. The view listens to `LocationChanged` and applies a navigation within the same path whose query says something else for its prefix, so back, forward and links on the page reach the view; a navigation to another path is ignored so a page being left is not recalculated. Writing happens only when `RendererInfo.IsInteractive`, so a prerendering Server view reads the URL and leaves it alone. Nothing is written when the URL already reads as the current selections, which is what stops the view's own navigation from coming back as a change. Until then the docs demo did this itself with JSON in one parameter, about thirty lines of page code; it now sets `SyncUrl` and keeps only its row count parameter. **Done:** the default child is `StateSummary<T>`, a raw but complete rendering of counts, metrics and every facet with clickable values, buckets, presets and the null value, which exercises the whole loop. The sample app runs it over 200 000 rows. **An unchanged `Selections` parameter does nothing (fixed 2026-09-22):** the view compares the parameter with the value it last received, not with the current selections. A host that does not bind `Selections` re-renders with the same null after every click, and under `SyncUrl` the view's own navigation causes exactly that re-render through the router, so until then every click was undone about forty milliseconds later and the page looked as if it reloaded. The same comparison decides whether a dashboard switch keeps the current selections or takes the parameter. **`Items`, `Build` and `RebuildKey` (added 2026-09-26):** the view builds its own dashboard when the host gives it rows instead of a dashboard (§C7). `Items` is an `IReadOnlyList<T>`, so a deferred query does not compile and the reference is something the view can compare; `Build` is an `Action<DashboardBuilder<T>>`, the same builder as `Dashboard.Create`, so fixed filters, the sort order and the time provider need no parameters of their own; `RebuildKey` is any value, compared with `Equals`, for a `Build` that reads page state. The view builds on its first parameters and again only when the `Items` reference or `RebuildKey` changes, never because the `Build` delegate did, since a lambda in markup is a new delegate on every render. A rebuilt dashboard goes through the same path as a host switching dashboards, so the context and every component's subscription stay. The selections move to it the way a bookmark moves to new data (§C4.9): written by the old serializer, read by the new, so the same definition over new rows keeps them all, and a definition that lost a facet, or reads its values differently, drops what it cannot read instead of failing in `Calculate`; the view then raises `SelectionsChanged` with what was kept. A host-built `Dashboard` switch keeps the strict path, where an unknown key throws. `Dashboard` lost `[EditorRequired]`, since Blazor cannot say "exactly one of two"; both, neither, or `Build` or `RebuildKey` beside a `Dashboard` throw on first render with a message naming the parameters. The measured cost that makes a build per visit reasonable is in §8.
2. **`ValueFacet`**: `Values` with counts and the selected flag, the null value through the formatter, `Other`, a search box when `IsSearchable`. Click → `Toggle`. Clear link → `Clear`. **Done.** Takes the facet `Key`, or since 2026-09-22 the selector it was declared from as `For`, like `RangeFacet` and `DateFacet` (component 1); boolean facets use the same component. Multi-valued facets (added 2026-09-20) use it too: their state is a `ValueFacetState`, the root carries `l2d-multi-value-facet` so a host can style the overlap, and there is never an "Other" row (§C6). Options: `HideZeroCounts` (off, per §C4.3 the core keeps them and the UI decides; a selected zero-count value always stays), `Sort` with `SortDescending` (`FacetSort.Rank` by default, `Label` sorts the formatter's text with the formatter's `LabelComparer`, case-insensitive in its culture since 2026-09-17 (it used the machine's current culture before, so a Swedish server and an invariant CI disagreed on where ö goes), `Value` uses `IComparable` and throws naming the key otherwise; null stays last, ties keep rank order, "Other" is rendered after the list so it is unaffected; per §C6 ranking picks the values and the UI orders them), `ShowTotals` ("filtered (total)", off by default since 2026-09-20, see Decided: the two numbers are equal until another facet narrows the set, and the total sits behind the filtered count through `--l2d-total-opacity`; the button's tooltip always carries the value's label, both counts and the filtered count's share of the total through `FormatShare`, "Sweden: 34 (100) 34.0 %" in the invariant culture, as do the bucket and preset tooltips of the range and date facets, added 2026-09-16; the label was added to the value and preset tooltips on 2026-09-20 so all three read alike and a truncated label can still be read in full), `SearchLimit`, and the texts for clear, other, no matches and the search placeholder. Search text is component-local UI state, which is allowed: it is not a selection (§C4.5). Zero-count, null and selected values carry classes so the stylesheet can treat them. Ships with a scoped stylesheet reading `--l2d-*` custom properties, which `DashboardView` declares with defaults; the library bundle is imported by the host's scoped CSS bundle automatically.
3. **`ActiveSelections`**: the chip row of everything selected, each removable. Cheap and heavily used. **Done.** One chip per selected value (removal toggles that value off) and one per range or date selection (removal clears the facet), in facet order, plus clear-all. A range or date chip uses the bucket's or preset's own label when the selection is exactly that bucket or preset, and otherwise the formatter's interval text through two formatter methods added for this, `FormatRangeSelection` and `FormatDateSelection`. Options: `HideWhenEmpty`, `ShowFacetName`, and the texts. **Grouped values (added later):** by default a value facet's selected values share one chip, since they are one OR group (§C4.1); each value is a removable token inside it and the chip's own remove clears the facet. `ValueSeparator` sets the text between tokens (", " default, " or " states the semantics); `GroupValues="false"` restores one chip per value. **Parts (2026-09-19):** with range and date selections now sets of intervals (§2.2), every facet's selection is rendered the same way, as a list of removable parts: each value, each interval or preset, the null rows as a part of their own, or the text. A part's remove toggles it off (`ToggleAsync`, `ToggleIntervalAsync`, `ToggleNull` through `SelectAsync`, or `ClearAsync` for a text); with `GroupValues` a facet with several parts is one group chip whose own remove clears the facet, and a facet with a single part is a plain chip whichever way, since a group of one had two remove buttons doing the same thing. An interval part uses the bucket's or preset's own label when it is exactly that, otherwise the formatter's text through `FormatRangeInterval` and `FormatDateInterval`, which replaced the selection-level `FormatRangeSelection` and `FormatDateSelection`; "or (none)" is gone from the formatter because the null rows are a part with the null label.
4. **Rows: the host's grid, not a component.** `Results<T>`, a paged list over `GetPage` with a `RowTemplate`, two layouts, a pager and later an opt-in `Virtualize`, shipped in the first version and was removed on 2026-09-19. A real page renders its rows in the grid it already has, QuickGrid or a vendor's, which brings sorting, column templates, row selection and export; the moment a page needed sorting it left `Results` behind, and the component was in use only in the sample and the demo. What such a grid needs from this package is three things, and they are what remains. The rows as a list: `DashboardState<T>.Items` (§4.7). A stable per-state queryable: `DashboardContext<T>.Items` is `State.Items.AsQueryable()` cached until the next calculation, because QuickGrid re-queries when the reference of its `Items` parameter changes and would otherwise do so on every render of the page. And the context in the page's own markup without a `@ref`: `ChildContent` is a `RenderFragment<DashboardContext<T>>`, so a page writes `<DashboardView Context="dash"> … <QuickGrid Items="dash.Items" Virtualize="true">`. The view already re-renders its content after every click, so the grid refreshes with no callback. Markup that never touches the context is unchanged, except that a template nested inside now needs the view's context named when it relies on the implicit `context`. There is no items-provider adapter: the queryable path covers paging, virtualisation, sorting and counting with no package dependency, a sorted fetch is one pass over the matching rows (§8), and an adapter caching a sorted order per state is a later, separate package if a host asks for it. The sample and the docs demo run QuickGrid, virtualised and sortable, over 200 000 rows. `--l2d-results-height` and `--l2d-sticky-background` went with the component.
5. **`Metrics`**: one tile per `MetricState`; no value renders as a dash. **Done.** A grid of tiles in definition order, or the subset and order given by `Keys`. A tile without a value carries a class and shows whatever the formatter returns for it, a dash by default. `IncludeMatchingCount` prepends a tile with the matching row count, which is not a metric in the core but is what most dashboards put first. **Per-metric components (added later):** `Metric` renders one tile by key like the facet components do, with an optional `Name` override and `MetricTemplate`, so tiles can sit anywhere in a layout. It and the since-removed `MatchingCount` rendered through a shared `MetricTile`, which is now the default tile of `Metric` alone. `MetricTile`, like `FacetHeader`, `BucketBars` and `RangeSlider`, is a rendering detail, not API: Razor cannot emit an internal component, so since 2026-09-14 the four shared building blocks are marked `EditorBrowsable(Never)` and documented as unsupported instead; the browsable API is the intent-carrying components only. Defining a metric stays a builder call, since its column is built at `Create`. **`Metrics` removed on 2026-09-14:** with per-metric components, a grid of every metric was a prototype convenience that took page-level decisions (which metrics, order, container) away from the page; a three-line CSS grid, shown in the getting-started page, covers the layout. **Share (added 2026-09-14):** `Metric` renders the state's share under the value through a new formatter method, `FormatShare`, one decimal by default; a metric without a share (average, min, max, no value, zero total) renders the tile unchanged. The share goes through `MetricTile`'s `Share` parameter, styled by `l2d-metric-share`. Until 2026-09-16 it and `MatchingCount` took a `ShowShare` parameter, off by default, and the template context's `Share` was gated by it too, so a template reading `Share` got null unless the page also set a flag meant for the default tile. The parameter is gone: the default tile is a placeholder to get pixels on the screen and shows what the state has, and the template context always carries the formatted share when there is one, so a template needs no flag to get a piece of data. **Template context (2026-09-14):** `MetricTemplate` on `Metric` receives a `MetricTileContent` record, the formatted title, value and share, the empty flag and the raw `MetricState`, so a template composes the pieces with its own markup instead of formatting the state again. **No wrapper with a template (2026-09-16):** until then the template replaced only the inside of `div.l2d-metric`, whose stylesheet gave it flex layout, padding, border, radius and background, so a host wanting its own tile had to fight the library's frame; the docs demo itself needed a `::deep` override of the tile's display and negative margins to bleed its icon panel through the padding. Now `Metric` renders the template fragment directly and `MetricTile` is the default tile only. The template owns the root, so `data-key`, `l2d-metric-empty` and the other hooks are the template's to place from the context, and `Class` and extra attributes apply to the default tile only, which the parameter's doc comment says. This is the one template in the package that drops the wrapper: `HeaderTemplate` and `ValueTemplate` keep theirs because the facet root and the value button carry behaviour, while the tile's wrapper carried nothing but style. The demo's tile draws its own frame and colours in its own stylesheet. An `Icon` render fragment slot, added and removed the same day, was the only decoration parameter in the package and its one consumer, the docs demo, overrode every default it set; a tile has no behaviour, so its look is the page's to decide and the template is the path. The demo renders its icon tiles through the template with a small component of its own. **`MatchingCount` removed on 2026-09-18:** it rendered the matching row count and its share of all rows straight off the state, which `Aggregation.Count` already computes: a count metric has no column, so the engine returns the matching row count with the same share, formats to the same string through the same formatter, and stays right under a scope, where both the metric's row count and the dashboard's total count are the scope. The sample proved the point by rendering `MatchingCount` beside `Metric Key="orders"`, two tiles with one number. The component bought one builder line, `b.CountMetric(key)`, and cost a second public component plus two special cases in the shared template context, whose `Metric` was null and whose `IsEmpty` was always false for it. `MetricTileContent.Metric` is now non-nullable, so a template never handles a null that a real metric never has. A named core test pins the equivalence the removal rests on, across selections and a scope.
6. **`RangeFacet`**: histogram of `Buckets` with the null value beside it; bucket click → `ToSelection()`. Min and max shown; a slider is a later iteration. **Done.** Each bar draws the total count faintly behind the filtered count, both scaled to the largest total, so the histogram keeps its shape (§C5) while the filled part follows the context. Two layouts via `BucketLayout`: vertical bars with labels beneath, or rows with an inline bar for many buckets or long labels. A click toggles the bucket's interval in the facet's set (`ToggleIntervalAsync`, 2026-09-19; until then it replaced the selection and a second click cleared), so bars select like values: two clicks light two bars, clicking a lit bar takes it out, and the last one out clears the facet; a wider interval from a slider or a bookmark marks every bucket it covers. The null value is a bucket of its own that toggles the null rows beside the intervals (`ToggleNull`), alone or with bars. Bar sizes are passed to CSS as custom properties on each bucket, so the markup carries no pixel values.
   **The null bar stands apart (2026-09-19).** The null value sits beside the buckets, not inside them (§C4.8), and a same-coloured column at the end of a histogram reads as one more interval, the highest one. So the null bar's filled part is striped in the bar colour (`--l2d-bar-null`, by default a `repeating-linear-gradient` of 1px hairlines in `--l2d-bar` over a 35 % `color-mix` tint of the same colour, so it reads as a soft, ghosted bar rather than hard bands, and dark mode and a host's bar colour carry through) and its column or row is set apart by `--l2d-gap`. Stripes rather than a second hue: a grey bar reads as inactive, and the null rows are real rows that count toward the total. The italic label alone could not carry this, since a narrow column hides it. The total behind stays faint, the selected state stays on the button, and the value facet is untouched because there null is an ordinary list item without a bar. Both layouts get the same treatment in `BucketBars.razor.css`; nothing in the markup changed.
   **A histogram never widens its host (fixed 2026-09-18).** Each column of the histogram is `contain`ed in the inline axis (`container-type: inline-size`), and a column too narrow for its text drops the label and the count through a container query at `4rem`, leaving the bar and its tooltip. Both come from the same declaration. Without the containment a column reports its non-wrapping label as its smallest width, and a row of `flex: 1 1 0` columns reports that width times the bucket count as its own intrinsic width, which becomes a floor under whatever the host puts the facet in: a date facet of 26 monthly buckets in the docs demo made the page 1662 px wide in a 390 px viewport, so the counts sat off-screen and the page scrolled sideways. `min-width: 0` on the column does not prevent it; it bounds how far a column shrinks, not what the row reports. The hiding is the other half of the same problem: the widest label in that demo measures 60 px and the widest count 70 px, so below about `4rem` the text was clipped to fragments, and a clipped label reads as a shorter label while clipped digits read as a smaller number. The query repeats the selector of the rule it overrides and follows it in the file, since a container query adds no specificity of its own. The list layout is unaffected; its columns are fixed-width and its rows wrap nothing.
   **Slider (done):** `ShowSlider` adds a `RangeSlider` beneath the bars, two overlapping native range inputs whose thumbs take the pointer, plus number inputs for precise entry. Handles preview while dragging and apply on release as an inclusive interval, so the dashboard recalculates once per gesture; the handles follow the current selection when it is a single interval, so a bucket click moves them; with several intervals toggled (2026-09-19) there is no one interval to show, so the handles rest at the ends, and applying the slider replaces the whole set with its interval, because the slider is a single-interval editor and a bar click adds to whatever it produced. **A handle at its end means no bound on that side (fixed 2026-09-19).** Until then the slider applied the handle positions as a closed interval, so the left handle at the far left gave "from 1.47", the data minimum. The first and last buckets are open-ended ("below 50", "2,000 and above") and a bucket lights up only when the selection fully covers it, which a bounded interval never does for them, so the end buckets stayed unlit however far the handles went. The slider now reports null for a side whose handle rests at its end and the facet builds the selection from what remains: "≤ 500" rather than "1.47 – 500", which is what the gesture meant and reads better in chips and URLs; both ends null clears the facet, as before. The upper end counts as reached within one step of the maximum, because a native range input snaps to a grid that starts at the minimum: in the docs demo the step is 100 over 1.47 to 19,302.36, so the right handle can never pass 19,301.47, and the earlier "both handles at the ends" test never fired in a real browser (the tests' 0 to 2,500 range happened to be on the grid). The core's cover rule was left alone: treating the data bounds as the open buckets' edges would make "fully covers" depend on the data, and the selection would still carry a bound nobody meant. The default step is a power of ten near a hundredth of the range. No JavaScript is involved. **The handles may cross (fixed 2026-09-20).** Until then the component clamped a handle at the other one, but Blazor writes a value to the DOM only when it differs from the previous render, so the second event past the crossing point changed nothing and the native thumb walked on while the state stood still: the picture showed "from" above "to" even though the applied selection was right. Without JavaScript the DOM cannot be pushed back, so the component now keeps each handle exactly where the browser has it and reads the interval as the lower one to the upper one; the fill, the value label, the number inputs and the applied selection all use that order, and the "From" label follows whichever handle is lower. Dragging one thumb past the other simply makes it the other end; on release the ordered interval is applied, the host feeds it back and the handles adopt it in order, so the thumbs stay where they are and only swap roles. The number inputs address the lower and the upper handle rather than a fixed input, so typing a lower value above the upper one crosses the handles the same way and the inputs re-render in order.
7. **`DateFacet`**: period buckets and the preset list with counts; clicks → `ToSelection()`. **Done.** Presets render as a row of pills above the periods, each with its count as of this calculation; a preset click toggles the relative preset as a part of its own (since 2026-09-19; until then it replaced the selection and a second click cleared), so a preset and several periods can be one selection. Periods render through the shared `BucketBars` component, extracted from `RangeFacet` so both facets draw bars the same way, with the null value as its own bar. A selected absolute interval marks every period and preset it covers, so clicking the March bar also lights "This month" when they coincide.
8. **Templates and styling last**, once the defaults work (§C7): optional `ValueTemplate` and `HeaderTemplate` per facet component, then the stylesheet. **Done.**
   - Templates: `HeaderTemplate` on `ValueFacet`, `RangeFacet` and `DateFacet` receives the facet state and replaces the default header; `ValueTemplate` on `ValueFacet` receives the `FacetValue` and replaces the label and count inside the button, so the click stays; `MetricTemplate` on `Metric` receives a `MetricTileContent` with the formatted pieces and replaces the whole tile, wrapper included. The docs demo renders the Status facet through `ValueTemplate` with a small badge component of its own (added 2026-09-20), beside the default facets, so the page shows what a value template receives and what it keeps.
   - The default header is one shared `FacetHeader` component (title, optional extra content such as range bounds, clear button), so the three facets look and behave the same and their stylesheets shrank to what is theirs. The clear button and the clear-all button in `ActiveSelections` show an × (changed 2026-09-15); the `ClearText` and `ClearAllText` parameters remain as the accessible label and tooltip.
   - Styling: every `--l2d-*` custom property is declared once on `.l2d-dashboard` in `DashboardView.razor.css`, grouped as layout, type and colour, with a `prefers-color-scheme: dark` block adjusting the neutrals. Components read the properties with fallbacks. A host restyles by setting properties on the dashboard element or an ancestor; nothing in the markup needs overriding.

   | Property group | Properties |
   |---|---|
   | Layout | `--l2d-gap`, `--l2d-section-gap`, `--l2d-radius`, `--l2d-chip-radius`, `--l2d-bar-height`, `--l2d-bucket-label-width`, `--l2d-facet-max-height`, `--l2d-metric-min-width`, `--l2d-metric-padding` |
   | Type | `--l2d-font-size`, `--l2d-title-size`, `--l2d-small-size`, `--l2d-tiny-size`, `--l2d-metric-size` |
   | Colour | `--l2d-title-color`, `--l2d-muted`, `--l2d-border`, `--l2d-accent`, `--l2d-hover`, `--l2d-selected-background`, `--l2d-selected-color`, `--l2d-bar`, `--l2d-bar-total`, `--l2d-bar-null`, `--l2d-bar-track`, `--l2d-input-background`, `--l2d-metric-background`, `--l2d-total-opacity` |

   State classes the stylesheet and hosts can hook: `l2d-selected`, `l2d-zero` (filtered count zero), `l2d-null` (the null value), `l2d-metric-empty` (no value), `l2d-collapsed` (facet body hidden).

   **A facet's list is capped through `--l2d-facet-max-height` (added 2026-09-20).** The value facet's list and the list layout of the range and date facets take `max-height` from the property, `none` by default, and scroll inside themselves past it; the header and the search box are siblings of the list, so they stay in place while it scrolls. The histogram is untouched, its height being `--l2d-bar-height`. A property rather than a `MaxHeight` parameter because it is styling, and the property already does what a parameter would: set on `.l2d-dashboard` it is the default for every facet, and set on one facet's `style` attribute, which lands on the root element, it overrides that default for that facet alone through the cascade. The docs demo and the sample cap the Country facet.

   - **Class and attribute passthrough (added 2026-09-14).** Every public component (`DashboardView`, the three facets, `ActiveSelections`, `Metric`, `StateSummary`) takes a `Class` parameter, rendered after the library's own classes on the root element, and captures every other unmatched attribute (`id`, `style`, `title`, `data-*`, `aria-*`) onto that element. This is the third tier of styling after custom properties and the stable classes: it lets a host target one component from its own stylesheet without a wrapper element or `::deep`. Parameter names match case-insensitively, so `class="..."` on a component is the `Class` parameter. The splat comes before the library's attributes, so `class` and `data-key` cannot be overridden. `Metric` passes both through to the shared `MetricTile`, whose earlier `CssClass` became `Class`. The base class holds the two parameters and a `RootClass` helper, so a component adds them by using the helper on its root. **`InputClass` (added 2026-09-16).** `TextFacet`, `ValueFacet` (the search box) and `RangeFacet` (the slider's number inputs, passed on to `RangeSlider`) take an `InputClass` for the inner `<input>`, the one element a host realistically hands to a CSS framework. Each input renders a stable hook class (`l2d-text-input`, `l2d-facet-search`, `l2d-slider-input-from`/`-to`) that carries layout only, plus either the host's classes or, when none are given, the library's skin class `l2d-input` (font, padding, border, radius, background). Replacing the skin rather than appending to it is deliberate: scoped CSS appends an attribute selector to every rule, so a library rule on the hook class would outrank a single framework class such as `form-control` and the host would have to fight it. The range inputs of the slider are not affected; they are styled through the track. `CssClasses` holds the joining logic for both root and input classes.
   - **Render modes: clicks are links when the view is not interactive (added 2026-09-20).** Every click in the library goes through one element, `Clickable`, which is a `<button @onclick>` in an interactive view and an `<a href>` when the view is not interactive and has `SyncUrl`: static server-side rendering, or the prerender of an interactive page before its circuit connects, where a button did nothing for that second. The href is the URL of this page with the changed selections written over the view's query parameters, built by `DashboardContext<T>.Href(selections)`, which is available whenever `SyncUrl` is on so a host can render its own links, and `Links` says which form the view renders. Components ask the base class's `LinkTo(change)`, which returns null in an interactive view so the change is not computed there. Both forms carry the same class, and the rules that style the element itself, per component and including those keyed on an ancestor's `.l2d-selected`, `.l2d-zero` or layout class, live in `Clickable.razor.css`: the element carries that component's CSS scope and not its parent's, so a rule left in `ValueFacet.razor.css` compiles to a selector the element never matches, while a scoped rule needs the scope only on its last compound, so ancestor-keyed rules work from the shared file. Rules for the content inside stay with the component that renders it. A link has no `aria-pressed`; the selected state stays on the list item. Covered: values, bars, the null bar, presets, header clear buttons, chips and clear-all, and the raw `StateSummary`. Not covered, and needing an interactive mode: the text facet's input, the value facet's search box, the range slider and collapsing, which still render and do nothing when static; a static page sets `Collapsible="false"`. `ResetOnChange` on the view names page parameters a selection change drops, comma-separated, for a grid's `page`: a new filter has a new first page. It applies to the links and to the URL an interactive view writes; the sort parameters are kept, since a sort survives a filter. Static rendering fits this library because the dashboard is an immutable singleton and a calculation is milliseconds, so a request needs nothing but the URL and the server holds nothing per visitor; the rows are then a page of the state's list, which the host renders itself (QuickGrid pages and sorts through the URL by itself from .NET 11; before that its paginator needs interactivity). The sample's `/static` page shows it with a hand-paged table. The docs demo cannot, being WebAssembly.
   - **Accessibility (added 2026-09-17).** Every icon button carries an `aria-label` and a `title` from a text parameter: the clear and remove buttons since the start, the pager's four glyph buttons through `FirstPageText`, `PreviousPageText`, `NextPageText` and `LastPageText`. The value facet's search box is labelled with the facet name like the text facet's input. What changes on a click is announced: the results summary, the empty-results text and "no matches" carry `role="status"`, the pager status `aria-live="polite"`. Every list the library renders (values, presets, buckets, chips) is keyed, values by their boxed value with a marker for null, so items keep their element across states; the result rows are the host's fragments and cannot be keyed by the library.

9. **Collapsing (added after the first version).** Every facet component takes `Collapsible` (on by default since 2026-09-14; `false` gives a fixed header), which renders the title as a toggle with a chevron in the shared header, and a bindable `Collapsed` value with `CollapsedChanged`, so a host can set or remember which facets are open. Collapsed hides the body only; the header, including the clear link and a range facet's bounds, stays. The value is component-local UI state synced from the parameter the way the results page index is, so it survives state changes and works without a binding. A header template replaces the toggle, leaving `Collapsed` as the only control. **Name override (added 2026-09-15).** Every facet component takes an optional `Name`, as `Metric` already did, rendered by the shared header (and as the text input's accessible label) in place of the builder's title; the state, `ActiveSelections` and `StateSummary` keep the core's title (§C7 Decided). The page can localise a header without building one dashboard per culture. The whole chain was renamed from `Title` to `Name` the same day, builder method, `FacetState`, `MetricState`, `FacetInfo`, `MetricInfo`, `MetricTileContent`, the component parameters and `ShowFacetName`: `title` is one of the attributes the components pass through to their root, and a parameter called `Title` swallowed it. The CSS classes `l2d-facet-title` and `l2d-metric-title` keep their names since they describe the heading element.

10. **`TextFacet` (added 2026-09-14).** One text input in the shared header layout, bound to the facet's `TextSelection` through `Selections.With(key, new TextSelection(text))`; a cleared input or the clear link calls `Clear`. The component debounces: it sends the text after a pause (`DebounceMilliseconds`, 300 by default) or on Enter, since each new text costs a scan (§4.1), and the input shows the typed text meanwhile while the chip in `ActiveSelections` shows the applied one. The state's `ContextCount` is not rendered: an "Among N rows" line under the input was removed on 2026-09-15 as noise next to the matching-count tile; a page that wants it reads the state. Options: `Placeholder`, `DebounceMilliseconds`, `Collapsible`, `HeaderTemplate`, `InputClass`, `Class` and attribute passthrough like the other facets. `ActiveSelections` renders a `TextSelection` as one chip with the text, removal clears the facet.

11. **Markup definitions (added 2026-09-26).** Under a view with `Items`, the facet and metric components define what they show (§C7), on the same components as display rather than separate definition components. Done for every facet component; `Metric` follows the same pattern. `RangeFacet` defines from `For` with `Buckets` or `AutoBuckets` (both throw), `DateFacet` with `TimeZone` (watched by id), `Granularity` or `AutoGranularity` (both throw), `Presets` and `SkipEmptyPresets`, each with a `Define` over its builder; their value type is read from the selector as for `ValueFacet`, and a selector the kind cannot take fails with the builder's own message. `TextFacet` has no selector, so its `Match` function is what defines it (always explicit); `Define` without `Match` throws, and the component reads the facet's text only once the facet exists.
   - **Registration.** A component registers a `MarkupDefinition<T>` with the view in `OnParametersSet`: the owner, the key, whether it is *explicit* (carries a definition parameter) or only names a selector, the *watched* values, and an `Apply` delegate that adds it to the builder. The view implements the internal `IMarkupRegistry<T>`, reached through the context. Rules: the same owner registering again replaces its definition when a watched value changed (arrays compare by content), otherwise nothing happens; an explicit definition takes over from an implicit one for the same key, so `<ValueFacet For=... />` and `<ValueFacet For=... Top="5" />` may appear in either order; two explicit ones throw; a key that `Build` defines displays for an implicit component and throws for an explicit one; under a host-built `Dashboard` an explicit definition throws and an implicit one is ignored, so plain `For` keeps naming a builder facet as before.
   - **Values are watched, code is not.** For `ValueFacet` the watched values are the selector's type, `Name`, `Top`, `RankBy`, `Searchable` and `Multiple`; `Label` and `Define` are delegates, read when the facet is defined, since a lambda in markup is a new instance on every render and comparing it would rebuild every time. `RebuildKey` on the view covers code that reads page state.
   - **One build per render pass.** Every registration that changes something bumps a version and queues the view. At the top of its render the view compares the version with the one it saw last: if it moved, it records it and queues itself again, which puts it behind the components queued so far and therefore behind their children; if not, it builds once from `Build` followed by the markup's facets, plain metrics and calculated metrics, each in render order, and rebinds the context, which re-renders every component in the same batch. The spike below built once per nesting level; this loop builds once per pass whatever the depth, at the cost of rendering the view's content once more.
   - **Waiting.** A component whose key the dashboard lacks renders nothing while the view has unsettled registrations. Once settled, it asks the view for the key (`AskFor`), which counts as one more change, so a component later in the same pass can still define it; only when the view has settled again and the key is still unknown does the component render and the core's unknown-key error surface. Under a host-built `Dashboard` nothing waits and an unknown key throws at once, as before.
   - **Held selections.** The first context gets only the requested selections the first dashboard knows. After a build from registrations, under `SyncUrl` the URL is read again with the new definition; otherwise the `Selections` parameter applies again while the user has changed nothing; failing both, the selections carry over as on any rebuild. `SelectionsChanged` reports a change. A `Selections` parameter that changes later is filtered to the known keys the same way.
   - **Host callbacks during a render.** A build from registrations happens inside the view's render, so the tasks of `StateChanged` and `SelectionsChanged` are observed and a failure goes to the renderer through `DispatchExceptionAsync`, instead of being lost. Calling them in the render rather than after it keeps static rendering, which has no after-render step, informed.
   - **Typed builder calls from a boxed selector.** `For` is an `Expression<Func<T, object?>>`. `MarkupFacets.Unbox` removes the conversion to `object` so the lambda's return type is the member's, and a generic method per facet kind, reached with `MakeGenericMethod`, casts it back to `Expression<Func<T, TProp>>` and calls the typed builder method: `BooleanFacet` for `bool` and `bool?`, `MultiValueFacet` with `Multiple` over the collection's item type, `ValueFacet` otherwise. A collection selector without `Multiple` throws with the hint, since a value facet over arrays is never what was meant. Key and `For` together are allowed under `Items` only, as the explicit key of a selector with no member name to derive one from.
   - **`Define` is a `Delegate` on `ValueFacet`.** The plan was a facade with generic `Comparer<TValue>` and `Serialize<TValue>` methods checked at registration. A `Delegate` parameter gives the whole typed builder instead: the lambda names its parameter type, `(ValueFacetBuilder<Order, string> f) => f.Comparer(...)`, which C# needs for a lambda's natural type anyway, and the view checks it against the selector's type when it defines the facet, with a message that spells the expected type. No new public type, and every current and future builder option works. The other facet kinds are generic in the row type only, so their `Define` is a plain `Action` over their builder.

### Markup definitions: spike (2026-09-26)

Before facet and metric components define what they show under an `Items` view (§10, question 1), a throwaway prototype checked the one mechanism the design rests on: components register their definition during the first render, and the view builds from what registered and renders again, under every render mode. The prototype (a `SpikeFacet` that registered a builder call in `OnParametersSet` and rendered the real `ValueFacet` once its key existed) ran in the sample at three nesting depths, with a fourth facet behind an `@if`, over 2 000 rows. It was not merged.

How it worked: the first registration in a pass queues the view's re-render with `StateHasChanged`; the view, at the top of that render, builds from `Build` plus every registration so far and rebinds the context synchronously, which re-renders the subscribed components in the same render batch.

| Render mode | Result |
|---|---|
| Static SSR | Works. The server processes the queued re-renders before it writes the response, so the HTML holds every facet, the nested ones included, with the URL's selections applied and clicks as links. |
| Interactive Server, not prerendered | Works. Both passes land in one render batch: a `MutationObserver` watching from the page head never saw the placeholder a pending component renders. |
| Interactive Server, prerendered | Works. The prerendered HTML is complete, and the interactive render that replaces it is one batch again, so nothing flashes on the handover. |
| A facet appearing later (`@if`) | One build; the selections and the URL are kept. |

What step 4 must do differently from the prototype:

- **Hold the requested selections aside until the definitions are in.** The first dashboard lacks every markup-defined facet, so selections read from the URL on first render were dropped as unknown, and a `Selections` parameter naming such a facet would throw in `Calculate`. Verified fix: the first context gets only the keys the dashboard knows; each rebuild then reads the URL again with the new definition under `SyncUrl`, where the URL is the state, or applies the `Selections` parameter again while the user has not changed anything. Because `ToQueryString` keeps parameters that are not yet facets, a facet behind an `@if` picks up its selection from the URL when it appears.
- **Build once per first render, not once per nesting level.** The prototype built 4 times for facets at depths 1 to 3: the initial build with `Build` alone, then one per level, because the view's queued re-render runs after the components already queued and before their children. About 3 ms each at 2 000 rows, so harmless here, but wasted. The view can queue itself again until a pass adds no registration and only then build; the initial build without the markup's facets may be avoidable too.
- **Raise the host's `StateChanged` properly.** The prototype rebound inside the render and dropped the task of the host callback; the real view must raise it after the render, through `InvokeAsync`.

Verdict: go. The mechanism holds under static SSR, prerendering and interactive rendering, with the three changes above.

### Code layout

Every component is a `.razor` file holding markup and directives only, with a `.razor.cs` partial class beside it holding parameters, state and methods. No `@code` blocks anywhere in the repository.

### State flow

`Calculate` runs inline in the click handler, then `StateHasChanged`. The component keeps no derived data of its own, so nothing can drift from the state. A back click is a state-cache hit and costs nothing.

---

## 10. Open questions

1. **Facets and metrics declared in Blazor markup.** Should `<Metric>` and the facet components be able to define what they show (aggregation and selector, or facet kind and selector), not only place something defined in the builder? Two routes were weighed on 2026-09-12: computing in the component over the matching rows (pure, but the UI calculates and pays per render; for facets that would be a second counting engine, which §C7 rules out), or registering the column lazily on the dashboard on first render (keeps the speed, but mutates a dashboard the design promises is immutable after `Create`, misses states already cached, and makes a page's markup part of the dashboard's definition). A third route is a markup-driven builder: a `Definition` fragment inside `DashboardView` whose children (`ValueFacetDefinition`, `RangeFacetDefinition`, `DateFacetDefinition`, `MetricDefinition`, `CountMetricDefinition`, `FixedFilter`, `SortDefinition`) register builder calls with a collector during the first render, after which the view runs `Dashboard.Create` once and then renders the placement components; the view cascades its type parameter so children need no `T`. This keeps the engine and its guarantees untouched and was prototyped on 2026-09-12 (facet and metric definition components were written, then removed unfinished). Discussed and parked the same day: the builder stays the primary API, the markup route is the one to add if any, and the one decision it needs is whether each view builds its own dashboard (right for WebAssembly) or a host can build once and share (needed on Blazor Server with large shared data; a `DashboardCreated` callback plus the existing `Dashboard` parameter would give both). Also noted: the core's internal `*Definition` classes would want renaming to avoid sharing names with the markup components. **Update 2026-09-26:** the one decision is made: a view renders either a host-built `Dashboard` or builds its own from `Items` and `Build` (§C7, §9 component 1), and markup definitions are to apply to `Items` views only, on the same facet and metric components rather than separate definition components. A spike confirmed the registration pass under static SSR, prerendering and interactive rendering (§9, Markup definitions: spike).

### Decided

- **Parallel counting is off by default.** Counting facets one after another costs roughly 10 to 20 ms at the target; counting them on separate cores could cut that to a few milliseconds but occupies several cores per click, which can hurt a busy Blazor Server. Supported as a builder option, off unless the application turns it on. See §4.3.

- **String facets are case-insensitive by default.** `OrdinalIgnoreCase`; first-seen spelling is presented. Case-sensitive is a builder option. See §3.3.
- **Project renamed to `Linq2Dashboard` under `src/`.** First code change of the implementation. See §7.
- **Selections serialise to JSON only** in the first version; applications encoded the JSON for URLs themselves. Superseded after the first version by the query-string form below (2026-09-16), which sits beside the JSON and changes nothing in it. See §2.5.
- **Custom facets come later.** The facet interfaces are internal in the first version and become public when custom facets are added. See §6.
- **Text facets are a `FacetIndex` like the others, with a predicate scan.** `TextFacetIndex<T>` is the one generic index, since it needs the row array; `Present` returns a state with only the text. The scan runs the application's function over row chunks on several cores when parallel counting is enabled and serially otherwise, so the existing option stays the single switch for multi-core work. The always-explicit key and the `{ "text": ... }` JSON shape are fixed so a later precomputed text column can share them. Decided 2026-09-14. See §2.1, §2.2, §4.1, §9.
- **Blazor: Server primary, WebAssembly supported with smaller datasets.** See §7.
- **Blazor: plain CSS with custom properties, one cascaded formatter service, no results component.** See §9. A slider is an opt-in on `RangeFacet`; the rows go to the host's grid (decided 2026-09-19, below).

- **Columnar only in the first version.** No per-value bitmaps. Same strategy for every cardinality; bitmaps are a later, benchmark-justified addition. See §1, §3.4, §4.1.
- **Range bounds are `double`.** The precision trade for `decimal` properties is accepted; range values are used only for filtering and bucketing, never for metrics. See §2.2, §3.3.
- **Metric values are `double`, from the selector to `MetricState`.** One 8-byte column per metric, NaN for null, compensated summation; the state and the template context expose `double?`, so the UI decides decimals and units per key. A `decimal` column would double the memory, rule out vectorisation and still be shown rounded, so exact money arithmetic is left to the host's own reports. Decided 2026-09-22. See §2.1, §3.3, §4.6.
- **Derived range buckets: a 1, 2, 5 step over the 2nd to 98th percentile, open tails only where values lie beyond, percentiles from a stride sample of at most 100 000 values.** Chosen over equal widths between minimum and maximum (unreadable edges, one tall bar on skewed data), over quantile edges (unpredictable widths, collapse on ties) and over a log scale (positive values only; a candidate for an explicit option). The sample keeps the build cost at one small sort per range facet and the result deterministic. Decided 2026-09-20. See §2.1, §3.3, §C5.
- **Derived date granularity: the finest of day, week, month, quarter and year within about 30 periods over the 2nd to 98th percentile span, resolved once at build.** A hard ceiling was chosen over "closest to a target" because fewer bars read better than more in a facet and the rule is easy to state; the percentile body reuses the range facet's reasoning and sample size. Decided 2026-09-20. See §3.3, §C5.
- **The dashboard is stateless.** `Calculate(selections)` is the only entry point; the UI owns the current `Selections`. See §2.3, §7.
- **Share of the total is a field on `MetricState`, not a metric kind.** Every state carries `Share`; the tile shows it. Decided 2026-09-14. See §2.2, §4.6, §9.5.
- **The default metric tile has no display switches; `ShowShare` is removed.** The default tile shows the name, the value and the share whenever the state has one, and the template context always carries every formatted piece, so a template never needs a default-tile parameter to receive data. Decided 2026-09-16. See §9.5.
- **Distinct count has its own code column and does not share a facet's.** Exact bit-set counting over dictionary codes, dictionary discarded after build. Decided 2026-09-14. See §3.3, §4.6.
- **A multi-valued facet is the value facet index over a second column, behind an interface.** `IValueColumn<TValue>` carries the dictionary, the totals, `RowsWithCodes` and `CountInto`; `MultiValueColumn<TValue>` stores offsets plus a flat code array and codes its occurrences through a `ValueColumn`, so equality and first-seen spelling have one implementation. `ValueFacetIndex` is unchanged except that it computes no "Other" for `FacetKind.MultiValue`, and `ValueFacetState` gains `IsMultiValued`, so the Blazor `ValueFacet` component renders it as it is. A state type and an index of their own were rejected as duplicating the value facet for one flag. Decided 2026-09-20. See §2.1, §3.3, §4.3, §4.4, §C5.
- **Calculated metrics take a delegate over keyed values, not an expression or a fixed ratio shape.** Keys are checked by a dry run at definition; definition order is the dependency order. Decided 2026-09-14. See §2.1, §4.6.
- **Value labels are a row selector on the builder, evaluated once per distinct value at `Create`.** Not a value selector (the row form covers it, and reaches denormalised columns directly), not async (the dashboard reads its data once, synchronously; lookups happen before `Create`), and not a formatter concern (the label is data and must drive search). Decided 2026-09-14. See §2.1, §2.4, §3.3.
- **Metric tiles have no decoration parameters; a different look goes through `MetricTemplate`, which receives the formatted pieces.** A tile has no behaviour, so its default is a placeholder and the template is the real path. Decided 2026-09-14. See §9.5.
- **A scope is a `Dashboard<T>` made from another, not a `Calculate` argument.** `ScopeTo` on a built dashboard returns a new dashboard sharing rows, columns, indexes, sort order, serializer and selection row-set cache, with a `RowSet` as its "all rows" and per-facet and per-metric totals counted once over it. The pipeline is unchanged: the scope simply replaces the full set at the start of the prefix and suffix products. Chosen over a scope parameter on `Calculate` because every caller, the state cache and the Blazor context would otherwise carry a second axis of state; the Blazor package needed no change. Decided 2026-09-15. See §2.1, §3.1, §4.2, §4.3, §4.6, §5.
- **A scope from selections goes through the facet indexes and the row-set cache, not through a predicate.** `ScopeTo(Selections)` ANDs the selections' row sets with the current scope and reuses the scoped constructor, about twenty lines; presets freeze because the row set is taken now. Decided 2026-09-15. See §2.1, §4.1.
- **A template keeps the library's wrapper only when the wrapper carries behaviour; `MetricTemplate` renders no wrapper.** The tile's `div` carried nothing but style, so with a template the host's markup is the root and places its own hooks. Facet header and value templates keep their wrappers, since the facet root and the value button carry the click and the structure. Decided 2026-09-16. See §9.5.
- **Framework classes on inputs go through `InputClass`, which replaces the library's input skin instead of adding to it.** One parameter, on the three facets with an input, for the one inner element a host hands to a CSS framework; a headless mode or per-element class parameters everywhere were rejected. Decided 2026-09-16. See §9 (class and attribute passthrough).
- **Selections have a query-string form beside the JSON: one readable parameter per facet, hand-writable, with a caller-chosen prefix.** Chosen over a compacted JSON token because the point is links other pages and people can write, not shorter URLs. `ToQueryString` merges into an existing query so a page's own parameters survive. In Blazor the view does the sync itself, opt-in through `SyncUrl`, prefixed by its `Key`, since with the format and a key the earlier reasons to leave it to the host (parameter naming, collisions) fell away; the URL wins on first render and on navigation within the page, writes replace history, and only an interactive renderer writes. Decided 2026-09-16. See §2.5, §9.1.
- **`Dashboard<T>` is marked `[ImmutableObject(true)]`; hosting stays with the host.** The one thing the core does for lifecycle is tell general-purpose caches that the instance may be shared as is, which is what `HybridCache` needs to hold a dashboard without serialising it. Loading, expiry and rebuild are the host's code, shown in the walkthrough rather than shipped as a holder type or DI extension, until every consumer turns out to write the same class. Decided 2026-09-17. See §5.
- **`IDashboardFormatter` may grow; compatibility covers `DefaultDashboardFormatter` only.** The interface has no default implementations because every member needs the formatter's culture, so a new member is a break for a direct implementer. Consumers derive from the default and override, which the interface's doc comment and the usage guide say. `LabelComparer` was the first addition after that rule. Decided 2026-09-17. See §9.
- **`Create` and `ScopeTo` stay synchronous and take no `CancellationToken`.** A build is one enumeration and a few array passes; there is no I/O to cancel and no await point to observe a token at that would not cost a check per row. A host that must stay responsive builds on a background thread or in a hosted service, which the usage guide says. Decided 2026-09-17. See §2.1.
- **`Selections` has `==` and `!=`.** It is a value, compared by value everywhere (the state cache keys on it), and a class, so without the operators `a == b` silently compared references. Decided 2026-09-17. See §2.2.
- **Blazor parameter names: a visibility toggle is `Show…`.** `ShowSlider`, `ShowSliderInputs`, `ShowBounds`, `ShowCounts`, `ShowTotals`, `ShowPresets`, `ShowFacetName`; `SliderInputs` was renamed for 1.0. A parameter that carries a value keeps its noun (`SliderStep`, `PageSize`). Decided 2026-09-17. See §9.
- **A component never sets a floor under the host's layout, and a bar too narrow for its text shows no text.** The histogram's columns are contained in the inline axis so the bucket count cannot push the host's column, and the same declaration makes each column a query container that drops its label and count below `4rem`. Nothing is better than part of a number. Decided 2026-09-18. See §9.6.
- **There is no matching-count component; a `Count` metric is the matching row count.** The engine already returns `matching.Count` with its share for a count metric, so a second component only saved one builder line and forced a nullable `MetricState` into the shared template context. Decided 2026-09-18. See §9.5.
- **Every metric builder method carries a `Metric` suffix.** `b.Sum("revenue", …)` and `b.Count("orders")` read as LINQ operators over the builder, not as declarations; the facet methods had said what they defined since the start, and metrics were the one group that did not. `CountMetric`, `SumMetric`, `AverageMetric`, `MinMetric`, `MaxMetric`, `DistinctMetric` and `CalculatedMetric` restore the symmetry. A grouping property (`b.Metrics.Sum`) and a key-first entry point (`b.Metric("revenue").Sum(…)`) were both considered and dropped: the first has no facet counterpart, the second costs a second object per metric. Breaking, with no shim, as the `Title`→`Name` rename was. Decided 2026-09-18. See §2.1.
- **An empty date preset can be left out of the state, through `SkipEmptyPresets` on the facet.** A preset is declared rather than discovered, so it is the one facet entry that can carry a total of zero; value facets already drop those, and this gives date presets the same rule where the count is computed, in `Present`. Off by default, since a dashboard rebuilt over live data legitimately shows "Today (0)" before the first row of the day arrives. A selected empty preset is dropped as well, matching the value facet, and stays clearable through the facet header and the active selections. Decided 2026-09-19. See §4.5, §C5.
- **Scoping a built dashboard is `ScopeTo`, not `Where`.** Every document calls the result a scope, so the usage guide had to translate the method name into the domain word on each mention ("`dashboard.Where(...)` scopes without a rebuild"). `Where` also promises LINQ: a lazy sequence, free until enumerated, whereas this does a pass over the rows plus a count per facet and metric and returns an object the host keeps and caches. `DashboardState<T>.Items` is a list, so `dashboard.Where(x => …)` and `state.Items.Where(x => …)` could sit in one page meaning different things. The builder's fixed filter keeps `Where`: it configures rather than returning a thing, and there the LINQ echo is honest. Breaking, with no shim. Decided 2026-09-18. See §2.1, §3.1.
- **The null bar in a histogram or bucket list is striped and set apart by a gap.** The concept places null beside the buckets, and a solid column at the end read as the highest interval. Stripes in the bar colour through `--l2d-bar-null`, not grey (reads as inactive) and not a second hue (fights the host's theme); a host overrides the one property for a solid fill. Decided 2026-09-19. See §9 (`RangeFacet`).
- **A range or date selection is a set of parts, OR-ed, with the null rows as a flag beside it.** `RangeInterval` and `DateInterval` are the parts; the records hold a set and the same `Toggle`/`ToggleNull` shape as values; `Selections.ToggleInterval` is the click, named apart from `Toggle` so `Toggle(key, null)` keeps meaning the null value. The row set is the union of the parts; a bucket lights when any part covers it; JSON and the query string gained a list form and kept the one-part forms. Adjacent bars are not merged. Decided 2026-09-19. See §2.2, §2.5, §4.1, §4.5 and §C5.
- **A slider handle at the end of its travel means no bound on that side.** The selection is built from the other handle alone, so the open-ended end buckets light up and chips and URLs read "≤ 500" instead of "1.47 – 500"; the upper end counts within one step because the native input's grid starts at the minimum. The core's cover rule stays data-independent. Decided 2026-09-19. See §9 (`RangeFacet`, slider).
- **The state serves the matching rows as a read-only list; `GetPage`, `PageCount` and `ResultPage` are gone.** A competent grid pages, virtualises and sorts by itself and asks its source for a count, a slice and an order through LINQ, so the source has to be a list for those to be cheap; a lazy enumerable made every scroll tick a pass over the matching rows, and a page API duplicated what the grid does. `Items` is `IReadOnlyList<T>` and `IList<T>` over an ordered row-index array built once per state, `GetItems` stays as the named slice. Breaking, with no shim. Decided 2026-09-19. See §2.4, §4.7, §C3.
- **The Blazor package renders no rows: `Results<T>` is removed, the view's child content is a template over the context, and the context carries one `IQueryable<T>` per state.** A page shows its rows in the grid it already has, and a grid needs a list, a stable queryable and the context in the markup, not a component that pages by itself and stops at the first request for sorting. Chosen over growing `Results` toward a grid, and over an items-provider adapter, which would need a QuickGrid dependency and is a separate package if ever. Breaking, with no shim. Decided 2026-09-19. See §9 (component 4).
- **Facets show the filtered count alone; `ShowTotals` is opt-in, and every two-count text reads "filtered (total)".** The convention in faceted UIs is one number per value, the count a click would give, which is the filtered count. The dataset total repeats it exactly until another facet narrows the set, so on by default it doubled every number on the first screen and crowded the label for nothing; the tooltip keeps both counts and the share regardless. A host that wants the total sets `ShowTotals`, and the total is rendered behind the filtered count through `--l2d-total-opacity`. `StateSummary` used "filtered / total" and now uses the same parentheses as the facets, so the library has one notation. Decided 2026-09-20. See §9.
- **A facet list is capped through `--l2d-facet-max-height`, not a `MaxHeight` parameter.** It is styling, and the property already gives both levels a parameter would: on `.l2d-dashboard` it is the default for every facet, on one facet's `style` attribute it overrides that default for that facet alone. The value list and the bucket list scroll inside themselves; header and search box stay in place; the histogram keeps `--l2d-bar-height`. Decided 2026-09-20. See §9 (styling).
- **Clicks are links when the view is not interactive; inputs, the slider and collapsing stay interactive-only.** The URL is already the state under `SyncUrl`, so a link to the URL of the changed selections is the click, which makes static server-side rendering work for click-only dashboards and makes the prerender of an interactive page usable before its circuit connects. One shared element renders either form, so the components and stylesheets do not branch. Forms for the inputs and the slider are not built until someone asks. Decided 2026-09-20. See §9 (render modes).
- **Adjacent and overlapping intervals are joined into one, in the selection records themselves.** The chips, the URL, the slider and a saved selection all read the set, so a rule applied only in the components would leave the rest saying "100 to 200, 200 to 300"; normalising in the constructor gives one canonical form everywhere, including for JSON and query text read back. Toggling became coverage-based so a click on a joined bar removes exactly that bar again, and `Remove` is set difference so a slider interval can be trimmed. Presets stay atomic parts. Decided 2026-09-21. See §2.2 and §2.4.
- **`Selections` on the view applies only when the parameter changes; binding it is optional.** A component parameter that the host passes unchanged on a re-render is not a new instruction, and a view that read it as one could not be used without `@bind-Selections`: under `SyncUrl` the view's own navigation re-rendered the page and cleared every click. Binding stays the way for the host to follow the clicks. Decided 2026-09-22. See §9 (component 1).
- **A facet declared from a member is named in markup by the same selector, `For`; `Key` stays for everything else.** The string key is the identity in selections, state and URLs (§C7) and stays so; `For` is another spelling of it that the compiler checks, the editor completes and a rename follows, the Blazor idiom of `ValidationMessage For` and QuickGrid's `Property`. Metrics and explicitly keyed facets have no selector to name them, so they keep `Key`, and the guide recommends a constants class for those keys. Typed key objects returned by the builder, an analyzer over Razor and derived metric keys were considered and rejected: they cannot escape the `Create` lambda cleanly, cannot see the builder, or collide (a sum and an average over one column). Decided 2026-09-22. See §9 (components 1 and 2).
- **A view builds its own dashboard from `Items` and `Build`, rebuilding when the list reference or `RebuildKey` changes.** `Build` is the full `DashboardBuilder<T>`; the rows are compared by reference and the delegate never, since a markup lambda is new on every render. Selections carry over a rebuild through the serializers, lenient like a bookmark; a host-built `Dashboard` switch stays strict. `Dashboard` and `Items` are exclusive and checked on first render. Decided 2026-09-26. See §9, component 1.
- **Markup definitions register with the view and settle in one build per render pass.** Explicit definitions beat implicit ones, two explicit ones or one against `Build` throw, a host-built `Dashboard` rejects explicit ones; components with an unknown key wait until the view has settled after being asked; requested selections are held until their facets exist. `ValueFacet.Define` is a `Delegate` checked against the selector's type rather than a facade type. Decided 2026-09-26. See §9, component 11.
