# Linq2Dashboard – Concept

> Status: draft, being built iteratively. This document describes *what* Linq2Dashboard is and how it behaves.
> It deliberately avoids implementation detail (indexes, bitmaps, data structures). Those belong in a later design document.

---

## 1. What it is

Linq2Dashboard is a .NET library for **interactive exploration of a large in-memory collection**.

You give it a collection of objects. You tell it which properties are interesting. It gives you back, at any moment:

- how many objects match the current selections,
- for every interesting property, which values exist and how many objects have each value,
- summary numbers (count, sum, average, ...) over the matching objects,
- the matching objects themselves, one page at a time.

Every time the user changes a selection, all of that is recalculated and presented as a fresh, consistent picture.

It is the faceted-search experience of an e-commerce site, applied to any .NET collection, with a LINQ-flavoured API. It sits next to `Linq2OData` and `Linq2GraphQL` as a member of the same family.

### What it is not

- Not a BI platform. No query language, no report designer, no data warehouse.
- Not a data access layer. The data is already in memory when the dashboard sees it.
- Not a UI framework. The core knows nothing about rendering. A separate Blazor package renders it.

---

## 2. Mental model

```text
  Source collection              (read once at initialisation, then copied)
        |
        v
  Fixed filters                  (set by the application, invisible to the user)
        |
        v
  Dataset                        ("everything" from the user's point of view)
        |
        v
  User selections                (what the user has clicked)
        |
        v
  Matching rows                  (the current answer)
        |
        +----> Facet counts       (per facet, per value)
        +----> Metrics            (count, sum, average, ...)
        +----> Result page        (the rows themselves, paged)
        |
        v
  Dashboard state                (one immutable snapshot of all of the above)
        |
        v
  UI                             (renders the snapshot, sends back new selections)
```

The engine is a function: **(dataset, selections) → state**. The UI is a loop: render state, receive a click, produce new selections, ask for a new state.

---

## 3. Core concepts

| Concept | One-line definition |
|---|---|
| **Dashboard** | The whole thing: a dataset plus a set of facet definitions, metric definitions and the current selections. |
| **Dataset** | The source collection after fixed filters. The universe the user explores. Fixed at initialisation and never changed afterwards. |
| **Facet** | One dimension the user can explore by. Defined by a selector (`x => x.Country`) and a kind (value, range, date, ...). Static. |
| **Facet value** | One entry shown under a facet: a value, a bucket, or a range. Carries a total count and a filtered count. |
| **Selection** | What the user has chosen within one facet. Dynamic. Small and serialisable. |
| **Fixed filter** | A predicate the application applies before the user sees anything. Defines the dataset. Never shown, never removable by the user. |
| **Matching rows** | The rows in the dataset that satisfy every current selection. |
| **Metric** | A named summary number over the matching rows. |
| **Result page** | A slice of the matching rows, in a chosen order, for display. |
| **State** | An immutable snapshot: counts, facet values, selections, metrics, and access to the result rows, all calculated from the same selections at the same moment. |
| **Row selection** | Rows the user has marked in a grid for an action. Unrelated to filtering. |

---

## 4. Behavioural rules

These rules define the experience. They are decisions, not options.

### 4.1 Selections combine as OR within a facet, AND across facets

```text
Country: Sweden, Norway        →  Country = Sweden OR Country = Norway
Status:  Open                  →  Status = Open

Matching rows = (Sweden OR Norway) AND (Open)
```

An empty selection in a facet means "no constraint from this facet".

This is the only combination mode in the first version. There is no "everything except", no AND within a facet, and no custom boolean logic across facets. The selection model should still leave room for an exclusion mode later without changing the shape of the state contract (§7).

### 4.2 A facet's own selection is excluded when counting that facet

When calculating the values and counts shown under a facet, every selection **except that facet's own** is applied.

```text
Selections:  Country = Sweden,  Status = Open

Counts shown under Country  use:  Status = Open
Counts shown under Status   use:  Country = Sweden
Counts shown under Brand    use:  Country = Sweden AND Status = Open
```

This is what lets a user see "if I also picked Norway, I would get 780 more" instead of every other option dropping to zero. It applies to all facet kinds, including range and date histograms.

### 4.3 Every facet value carries two counts

```text
Total count     how many rows in the dataset have this value.  Never changes.
Filtered count  how many rows have this value given the other facets' selections.  Changes on every click.
```

Total is calculated *after* fixed filters. The user never sees numbers from rows they are not allowed to see.

A value whose filtered count is zero is still a facet value. The core always includes it in the state with its total count and a filtered count of 0. Whether such values are shown, greyed out or hidden is a presentation choice made by the UI.

### 4.4 Metrics and the result page reflect all selections

Unlike facet counts, metrics and results have no "own selection" to exclude. They always reflect the full set of matching rows.

In the first version a metric is a single number over all matching rows. Metrics broken down per facet value (revenue per country) belong to grouping, which is a later concern (§8).

Null is skipped by metrics that read a property. Sum, average, min and max are calculated over the matching rows that have a value; average divides by that number, not by the number of matching rows. Count counts matching rows and is unaffected by nulls. A metric over a set with no values reports "no value", never zero.

### 4.5 Searching within a facet is not a selection

Typing "ACME" into the Customer facet narrows *which values are listed*. It does not narrow the matching rows. Only clicking a value does that.

### 4.6 Row selection is not filtering

Ticking rows in a result grid marks them for an action (export, bulk edit, navigation). It has no effect on facets, metrics, or paging.

### 4.7 The state is a consistent snapshot

Everything in one state object was calculated from the same selections. The UI never sees a Country count from one click and a Status count from the next.

### 4.8 Null is a value

A null facet property is a valid facet value like any other. It is always handled, never dropped.

```text
Country

Sweden        4 200
Norway        1 800
(none)          312
```

- Every facet kind exposes a null value when at least one row has a null for that property.
- The null value carries total and filtered counts and can be selected, alone or together with other values.
- Selecting it means "rows where this property is missing". It combines with other selections exactly as any other value does.
- The core represents it explicitly as null, not as a magic string. How it is labelled ("(none)", "Unknown", "Not set") is a presentation concern and belongs to the UI.
- For range and date facets, the null value sits beside the buckets, not inside them. Selecting an interval never includes nulls unless the null value is also selected.

The consequence is that facet counts always add up: for a value facet, the sum of all filtered counts including null equals the number of matching rows.

### 4.9 The data is fixed at initialisation

A dashboard is created from a collection once. After that, the data cannot be added to, removed from, or replaced.

- The dashboard reads the source exactly once, at creation, and keeps its own copy. Later changes to the original collection are not seen.
- Facet definitions, metric definitions and fixed filters are also part of initialisation. Only selections, paging and sorting change during the dashboard's life.
- New data means a new dashboard. Selections are serialisable (§7), so an application that wants "same view, fresh data" creates a new dashboard and applies the saved selections to it.

This keeps the engine a pure function of (dataset, selections) and means total counts are computed once and never invalidated.

---

## 5. Facet kinds

All facet kinds follow the rules in §4. They differ in what a "value" is and what a "selection" looks like.

### Value facet

The property has a discrete set of values: country, status, category, brand, customer.

- Values: each distinct value, with counts.
- Selection: a set of values (multi-select).
- Each row has exactly one value (or null) in a value facet. Collection-valued properties such as tags are not supported by value facets in the first version. See *Later* in §8.
- Concerns: high cardinality. A Customer facet with 100 000 values must not show 100 000 rows. See *Top N* and *Search* below.

### Boolean facet

A value facet with two values, or three when the property is nullable (true, false, null). Called out because the UI for it is usually a toggle or a set of chips rather than a list.

### Range facet

The property is numeric: amount, price, quantity, weight.

- Values: buckets over the property's range, with counts. Bucket boundaries are either fixed by the application in the facet definition or derived once from the dataset at initialisation. They never follow the current selections, so the histogram keeps its shape and only the bar heights change on a click.
- Selection: a continuous `[min, max]` interval. Buckets are a way of *presenting* the distribution and a shortcut for *choosing* an interval. The selection itself is always an interval.

### Date facet

The property is a point in time: order date, created, last login.

- Values: buckets by calendar period (year, month, week, day), with counts. Optionally a set of relative presets (today, last 7 days, this year).
- Selection: a continuous `[from, to]` interval, like the range facet, or a named relative preset that resolves to an interval at calculation time.

**Starting position on dates** (to be revisited once the first version is in use):

- Supported property types: `DateTime`, `DateTimeOffset` and `DateOnly`, each also in nullable form.
- Every date facet has an explicit **time zone**, given in the facet definition. Default is UTC. Calendar buckets and relative presets are computed in that zone.
- Conversion into the facet's zone: `DateTimeOffset` is converted. `DateTime` with kind UTC is converted; with kind Local or Unspecified it is taken as already being in the facet's zone. `DateOnly` has no zone and buckets as the calendar day it is.
- **"Now"** comes from a `TimeProvider` given to the dashboard at initialisation. Default is the system clock. Tests and reproducible snapshots supply their own.
- A relative preset is stored in the selection as the preset, not as the interval it resolved to. A bookmarked "last 7 days" therefore stays relative when restored later. Because the data is fixed (§4.9), the same preset can still resolve differently on a different day; that is intended.
- The bucket granularity (year, month, week, day) is a facet definition choice. Weeks follow ISO 8601 unless the definition says otherwise.

### Custom facet

An extension point. A custom facet defines its own notion of value, selection, and how a selection matches a row. Everything in §4 still applies. This exists so that hierarchical facets, geo facets, or domain-specific facets can be added without changing the core.

---

## 6. Presenting many values

### Top N and "Other"

A facet may declare that only its top N values are presented.

```text
Customer  (top 3 of 100 000)

ACME             2 421
Siemens          1 982
Volvo            1 754
Other           87 421
```

- "Other" is the remainder: rows in the facet's own counting context (§4.2) that are not covered by the presented values. Its filtered count is the context count minus the sum of the presented filtered counts. Its total count is the dataset count minus the sum of the presented total counts. It is informational, not selectable.
- Values the user has already selected are always presented, even if they fall outside the top N. Otherwise a click could make the clicked value disappear.
- Ranking is a per-facet choice between two modes:

  ```text
  By filtered count   the list follows the current context; reorders on every click
  By total count      the list is stable; some presented values may show a filtered count of 0
  ```

  Filtered count is the default. Total count suits facets where users expect a familiar, fixed order, such as a short list of well-known customers or regions.
- Ranking mode is part of the facet definition (§4.9), not of the selection. The UI does not change it at runtime.

### Search within a facet

A facet may expose a search over its values. The result is a filtered list of values, each with its counts under the current context (§4.2). Per §4.5 this does not change the matching rows.

---

## 7. Boundaries

### The core library

Understands: data, facets, selections, fixed filters, counts, metrics, paging, sorting, state.

Does not understand: rendering, layout, formatting, colours, click handling, Blazor, HTML.

Is usable on its own from a console app, a test, or a background job.

### The Blazor library

Understands: how to render a state, how to turn a click into a new selection, templates, layout, virtualisation, formatting.

Does not understand: how counts are calculated. It never counts anything itself.

### The contract between them

The UI consumes a **UI-friendly state** and produces **selections**. It never sees selectors, expressions, or indexes.

```text
FacetState
    Key            stable string identity, e.g. "country"
    Title          display name
    Kind           Value | Boolean | Range | Date | Custom
    Values         presented values with total/filtered counts and selected flag
    Selection      the current selection in a serialisable form
    Other          remainder count when top N applies, otherwise absent
    Configuration  kind-specific hints (min/max for range, bucket size, etc.)
```

**Facet keys** are assigned when the facet is defined. Either form is allowed:

```csharp
dashboard.AddFacet(x => x.Country);               // key derived from the selector: "Country"
dashboard.AddFacet("shipTo", x => x.Address.Country);  // explicit key
```

- Without an explicit key, the key is the member name at the end of the selector.
- An explicit key is required when the selector is not a simple member access, or when two facets would otherwise get the same key.
- Keys must be unique within a dashboard. Defining a duplicate is an error at initialisation, not a silent overwrite.
- Keys are case-sensitive and are the identity used in selections, state, bookmarks and templates.

Because facets are identified by string keys and selections are serialisable, a full set of selections can be stored, bookmarked, put in a URL, or restored later without the core changing.

---

## 8. Scope

### First version

**Core**

- Dashboard over an `IEnumerable<T>`, materialised once.
- Fixed filters.
- Value, boolean, range and date facets.
- Multi-select value selection; interval selection for range and date.
- Total and filtered counts with own-facet exclusion.
- Top N with "Other" and pinned selected values.
- Search within a value facet.
- Metrics: count, sum, average, min, max.
- Result paging with application-defined sorting.
- Immutable state snapshot with a UI-friendly facet model.
- Selections as serialisable, key-addressed objects.

**Blazor**

- One dashboard component consuming a state.
- One component per facet kind.
- Metric display, result list with paging, basic layout, basic templates.

### Later

- Grouping and hierarchical facets, including metrics per facet value (revenue per country).
- Multi-valued facets over collection properties (tags, categories). One row appears under several values, so counts no longer sum to the matching total. Needs its own rules for counting and for AND versus OR within the facet.
- Distinct count, percentage-of-total, calculated metrics.
- Exclusion selections ("everything except Sweden") as an additional mode on value facets.
- Saved views and bookmarks (the state contract already allows it).
- Export.
- Cross-filtering between multiple dashboards.
- Remote/server-side data adapters.
- Other UI adapters than Blazor.

---

## 9. Open questions

Decisions still to be made, roughly in order of how much they shape everything else.

1. **Sorting the result page.** Application-defined only, or user-controlled with the same key-addressed contract as selections? Parked until the rest of the first version is clearer.

### To revisit

- **Dates.** The starting position in §5 (types, time zone per facet, `TimeProvider` for "now", presets stored as presets) is a sensible default, not a final decision. Revisit after the first version has been used.

### Decided

- **Null is a value.** Always exposed, always selectable, never dropped. See §4.8.
- **Data is fixed at initialisation.** No add, remove, replace or refresh. New data means a new dashboard. See §4.9.
- **Multi-valued properties are a later concern.** In the first version every row has exactly one value or null per facet. See §5 and §8.
- **OR within a facet, AND across facets is the only combination mode.** Exclusion is a later addition. See §4.1 and §8.
- **Top N ranking supports both modes.** Per facet, by filtered count (default) or by total count. See §6.
- **Facet keys: derived by default, explicit when given.** Unique, case-sensitive, fixed at initialisation. See §7.
- **Metrics are over all matching rows only.** Per-facet-value metrics belong to grouping, later. See §4.4 and §8.
- **Zero-count values stay in the state.** The core always includes them; hiding or greying them out is a UI choice. See §4.3.
- **Metrics skip null.** Sum, average, min and max use rows with a value; average divides by that number. Count is unaffected. See §4.4.
- **Range buckets are fixed at initialisation.** Either application-defined or derived once from the dataset, never from the current selections. See §5.
- **"Other" is measured against the facet's own counting context.** Not against the matching rows. See §6.
