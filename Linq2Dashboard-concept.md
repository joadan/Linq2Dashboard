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
        +----> Result rows        (the rows themselves, in order, for a grid)
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
| **Scoped dashboard** | A dashboard narrowed to the rows passing a predicate, sharing the parent's definitions and indexes. Behaves like the parent with one more fixed filter. |
| **Matching rows** | The rows in the dataset that satisfy every current selection. The state serves them as a counted, indexable list in the application-defined order. |
| **Metric** | A named summary number over the matching rows. |
| **State** | An immutable snapshot: counts, facet values, selections, metrics, and the matching rows, all calculated from the same selections at the same moment. |
| **Row selection** | Rows the user has marked in a grid for an action. An application concern, outside the library; unrelated to filtering. |

---

## 4. Behavioural rules

These rules define the experience. They are decisions, not options.

### 4.1 Selections combine as OR within a facet, AND across facets

```text
Country: Sweden, Norway        →  Country = Sweden OR Country = Norway
Amount:  < 100, ≥ 1 000        →  Amount < 100 OR Amount ≥ 1 000
Status:  Open                  →  Status = Open

Matching rows = (Sweden OR Norway) AND (< 100 OR ≥ 1 000) AND (Open)
```

An empty selection in a facet means "no constraint from this facet". OR within a facet holds for every kind: the values of a value facet, the intervals of a range or date facet (§5), each with or without the null value (§4.8).

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

### 4.4 Metrics and the result rows reflect all selections

Unlike facet counts, metrics and results have no "own selection" to exclude. They always reflect the full set of matching rows.

In the first version a metric is a single number over all matching rows. Metrics broken down per facet value (revenue per country) belong to grouping, which is a later concern (§8).

Null is skipped by metrics that read a property. Sum, average, min and max are calculated over the matching rows that have a value; average divides by that number, not by the number of matching rows. Count counts matching rows and is unaffected by nulls. A metric over a set with no values reports "no value", never zero.

**Distinct count** is the number of different non-null values of a property among the matching rows: how many customers, products or countries the selection touches. Two values are the same by the rules a value facet uses (§5): strings compare ignoring case unless the application gives a comparer. Null is not a value, so it is never one of the distinct ones, and a distinct count over rows that have only nulls is "no value". A distinct count per facet value (customers per country) is grouping and belongs to §8.

Every metric also carries its **share of the total**: the same aggregation over all rows (after fixed filters, like total counts in §4.3), with the matching value as a fraction of it. Revenue 12 400 out of 32 600 has the share 0.38; the UI decides whether and how to show it as a percentage. Count, sum and distinct count have a share, because the matching value is a part of the whole: rows of all rows, revenue of all revenue, customers of all customers. An average, a minimum or a maximum over a subset is not a part of the whole, so their share is "no value".

A **calculated metric** is a formula over the metrics defined before it, not over rows: average order value as revenue over orders, revenue per customer as revenue over distinct customers. It reads their values and shares by key and follows all selections because its inputs do. If any input has "no value" the result has "no value", and so has a result that is not a finite number: revenue over zero orders is "no value", never infinity or zero. A calculated metric has no share, since a ratio is not a part of anything. It can read only metrics defined earlier, which is what keeps formulas acyclic; a formula that names an unknown metric is a mistake and fails when the dashboard is defined. Anything computed per row, such as quantity times price, is a base metric whose selector does the multiplication. A share is also "no value" when the metric has no value or when the total is zero, since there is nothing to be a part of. A share above one or below zero is possible when a sum has negative contributions; the core reports it as it is.

### 4.5 Searching within a facet is not a selection

Typing "ACME" into the Customer facet narrows *which values are listed*. It does not narrow the matching rows. Only clicking a value does that.

A **text facet** (§5) is the other thing typing can do: its text is a selection and does narrow the matching rows, under the same rules as any other facet. The two are kept apart by name so that "search" always means the first.

### 4.6 Row selection is not filtering

Ticking rows in a result grid marks them for an action (export, bulk edit, navigation). It has no effect on facets, metrics or the matching rows. The library offers no row selection of its own: the result rows are the application's objects, so marking them, and acting on the marks, is the application's concern. This section states the boundary so that no future feature blurs it.

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
- Facet definitions, metric definitions and fixed filters are also part of initialisation. Only selections change during the dashboard's life; paging and display sorting belong to the grid and never reach the dashboard.
- New data means a new dashboard. Selections are serialisable (§7), so an application that wants "same view, fresh data" creates a new dashboard and applies the saved selections to it.

This keeps the engine a pure function of (dataset, selections) and means total counts are computed once and never invalidated.

### 4.10 A dashboard can be scoped to a subset of its dataset

The same definitions are often wanted over several subsets of the data: one tab per region, one page per customer, one dashboard per tenant. A dashboard can therefore be narrowed to a **scoped dashboard**: a new dashboard over the rows of its dataset that pass a predicate, sharing the parent's definitions and indexes.

- A scoped dashboard behaves exactly like a dashboard built with the predicate as one more fixed filter (§3). Its dataset is the subset, so total counts, the matching total, "Other" (§6) and every metric's share of the total (§4.4) are measured against the subset. A value that no row in the subset has is not part of the dataset and does not appear, just as it would not after a fixed filter; the zero-count rule of §4.3 concerns filtered counts and is unchanged.
- It is created from an existing dashboard, not from the source, and costs a fraction of a build: one pass over the rows for the predicate and one count per facet. Definitions, columns, indexes and the result order are shared, never copied.
- Range and date buckets come from the parent, and so do a range facet's smallest and largest value. A dashboard built from the subset would derive them from the subset alone; a scoped dashboard keeps the parent's so that the same UI has the same axes in every scope. This is the one observable difference from a rebuild.
- A scoped dashboard is as immutable as its parent (§4.9), holds no selection state and accepts the same selections. The facet keys are the same, so a saved selection applies to every scope.
- Scopes compose: a scoped dashboard can be scoped again.
- The scope is not a selection. It never appears among the active selections or in serialised selections, and the user cannot remove it.
- A scope can also be given as selections, in the facets' own terms: the rows the selections match become the scope, with the facets' matching rules (case-insensitive strings, null as a value, half-open intervals). This is how "make this view my dashboard" works: the current selections become the scope of a new dashboard, which starts with nothing selected. Three consequences follow from the scope not being a selection. The scoped facets show only the values in scope, without the own-facet exclusion of §4.2, since the scope is part of "everything", not a choice. A selection inside the scope on the same facet narrows further: Sweden inside a Nordic scope gives Sweden, Germany gives nothing. A relative date preset is resolved when the scope is made and stays fixed, as a fixed filter would.

---

## 5. Facet kinds

All facet kinds follow the rules in §4. They differ in what a "value" is and what a "selection" looks like.

### Value facet

The property has a discrete set of values: country, status, category, brand, customer.

- Values: each distinct value, with counts.
- Selection: a set of values (multi-select).
- Each row has exactly one value (or null) in a value facet. A collection-valued property such as tags is a *multi-valued facet*, below.
- Label: optionally, a text per value read from the row, for facets whose value is an identity rather than a name. A Customer facet counts and selects by customer id, so a renamed customer keeps its saved selections, but shows "Acme (Malmö)". The label is read once per distinct value, from the first row that has it, and is what search (§4.5) matches. A value without a label is shown by the UI as the value itself. The null value never has a label; how it is shown stays with the UI (§4.8).
- Concerns: high cardinality. A Customer facet with 100 000 values must not show 100 000 rows. See *Top N* and *Search* below.

### Boolean facet

A value facet with two values, or three when the property is nullable (true, false, null). Called out because the UI for it is usually a toggle or a set of chips rather than a list.

### Multi-valued facet

A value facet over a collection property: the tags on an order, the categories of a product, the people on a ticket. Added 2026-09-20.

- Values: each distinct item across all rows, with counts. A row is counted once under each distinct item in its collection, however often the collection repeats it; a null item inside the collection is skipped.
- Null: a row whose collection is null, empty or holds only nulls has the null value (§4.8).
- Selection: a set of values, matching the rows that have **any** of them (OR, as in §4.1). "Has all of these" is a later selection mode (§8).
- Counting follows §4.2 unchanged: the facet's own selection is excluded. Because a row appears under several values, the filtered counts sum to **at least** the context count, not exactly, and a bar is not a share of the whole. The UI must not imply that it is.
- Top N presents the highest-ranked values and pins the selected ones as in §6, but there is no "Other": the rows overlap, so a remainder cannot be computed by subtraction.
- Everything else is the value facet's: a label per value (read from the value, since a row has several), search, comparer, serialisation, scoping. Its state is the value facet's state with a flag saying it is multi-valued.

### Range facet

The property is numeric: amount, price, quantity, weight.

- Values: buckets over the property's range, with counts. Bucket boundaries are either fixed by the application in the facet definition or derived once from the dataset at initialisation. They never follow the current selections, so the histogram keeps its shape and only the bar heights change on a click.
- Derived boundaries are round numbers: about the requested number of equal buckets (ten by default) laid on a step from the 1, 2, 5 series over the body of the distribution, with an open bucket at each end for the values outside it. A histogram of amounts reads "0 to 200", "200 to 400" and "1 800 and above", not 12.37 to 187.42 with one tall bar and nine empty ones. An end bucket exists only when a value lies beyond the round edge, and the body is what the outliers do not stretch: the 2nd to 98th percentile.
- Selection: a set of intervals, combined as OR (§4.1), with or without the null value. A bucket click toggles that bucket's interval in and out of the set, so the bars behave like the values of a value facet: "below 100 or 1 000 and above" is one selection. A slider or an input contributes a continuous `[min, max]` interval of its own. Buckets are a way of *presenting* the distribution and a shortcut for *choosing* intervals; the selection itself is always made of intervals, never of bucket identities.
- The set is kept in canonical form: intervals sorted, disjoint, with overlapping and adjacent ones joined. Two neighbouring bars therefore make one interval, "100 to 300" rather than "100 to 200 or 200 to 300", which is what the active selections, a bookmark and a slider then show. A click on a bar the selection fully covers carves that bar's interval out, splitting the interval it sits inside, so clicking the middle of three selected bars leaves the two around it; a click on a bar not fully covered adds it and joins it to its neighbours. A bar is selected exactly when the selection covers it.

### Date facet

The property is a point in time: order date, created, last login.

- Values: buckets by calendar period (year, quarter, month, week, day), with counts. Optionally a set of relative presets (today, last 7 days, this year).
- Selection: a set of parts combined as OR (§4.1), like the range facet, with or without the null value. Each part is either a `[from, to)` interval or a named relative preset that resolves to an interval at calculation time, so "January, March or last 7 days" is one selection. The intervals follow the range facet's canonical form, so January and February clicked together are one interval "January to February", and clicking February again splits it. A preset is a part of its own and never merges with an interval, because it resolves only when calculated.
- A preset no row falls in can be left out of the state, the way a value no row has is not a facet value (§4.10). This is a facet definition choice and is off by default; the zero-count rule of §4.3 is about filtered counts and is unchanged. Because a preset's interval moves with the clock, it is decided at each calculation, so a preset comes back once its interval reaches a row. A selected preset is left out on the same terms: the selection still applies, still matches, still shows among the active selections and still clears.

**Starting position on dates** (to be revisited once the first version is in use):

- Supported property types: `DateTime`, `DateTimeOffset` and `DateOnly`, each also in nullable form.
- Every date facet has an explicit **time zone**, given in the facet definition. Default is UTC. Calendar buckets and relative presets are computed in that zone.
- Conversion into the facet's zone: `DateTimeOffset` is converted. `DateTime` with kind UTC is converted; with kind Local or Unspecified it is taken as already being in the facet's zone. `DateOnly` has no zone and buckets as the calendar day it is.
- **"Now"** comes from a `TimeProvider` given to the dashboard at initialisation. Default is the system clock. Tests and reproducible snapshots supply their own.
- A relative preset is stored in the selection as the preset, not as the interval it resolved to. A bookmarked "last 7 days" therefore stays relative when restored later. Because the data is fixed (§4.9), the same preset can still resolve differently on a different day; that is intended.
- The bucket granularity (year, quarter, month, week, day) is a facet definition choice or, when the definition names none, derived once from the data at initialisation: the finest period that lays at most about thirty periods over the body of the data, the 2nd to 98th percentile as for range buckets, so a fortnight of data shows days, a season weeks, two years months, five years quarters, and a stray date from long ago does not decide. A named period always wins. The state reports the period chosen, never "derived". A dataset with no dates gets months. Quarters are the calendar quarters, January to March first. Weeks follow ISO 8601 unless the definition says otherwise.

### Text facet

Free text the user types, matched against each row by a function the application supplies: `(row, text) => row.Name.Contains(text) || row.Notes.Contains(text)`.

- Values: none. A text facet has nothing to list or count; its state carries only the current text. `ContextCount` still applies and says how many rows the text is searched among.
- Selection: the text, trimmed. Whitespace-only text is no selection at all, the way an empty value set is.
- Matching: the function decides. Which properties take part, case sensitivity and whether every word must match are the application's choice, made once in the function. The core passes the text as typed after trimming and never interprets it.
- It is a facet so that everything key-addressed works unchanged: it joins the AND across facets (§4.1), its own text is excluded from nothing since it counts nothing, it appears in the state and among the active selections, and it serialises with the others (§7). Several text facets in one dashboard are allowed, each with its own key and function.
- The function must be pure and safe to call from several threads at once; the core may evaluate it in parallel. Cost is one call per row in the dataset for each distinct text, so the UI should wait for the user to pause before sending a text.
- Concerns: a text facet is the only facet whose matching is not a column lookup, so it is the one potentially expensive operation in the library. A large dataset pays for the function on every new text, at a cost set by the function and the row count rather than by the library; an application with a heavy function or many rows should enable parallel counting or precompute what the function reads. A precomputed text column is a possible later addition behind the same selection; the function stays the primitive.

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
- A multi-valued facet (§5) has no "Other". Its rows appear under several values, so "context count minus the presented counts" is not a remainder. Top N still limits the list and pins the selected values.
- Values the user has already selected are always presented, even if they fall outside the top N. Otherwise a click could make the clicked value disappear.
- Ranking is a per-facet choice between two modes:

  ```text
  By filtered count   the list follows the current context; reorders on every click
  By total count      the list is stable; some presented values may show a filtered count of 0
  ```

  Filtered count is the default. Total count suits facets where users expect a familiar, fixed order, such as a short list of well-known customers or regions.
- Ranking mode is part of the facet definition (§4.9), not of the selection. The UI does not change it at runtime.
- Ranking decides *which* values are presented. The order they appear in on screen is the UI's choice: the rank order by default, or alphabetically by label, or by the value itself, in either direction. Reordering never changes the set of presented values or the "Other" remainder, and null stays last.

### Search within a facet

A facet may expose a search over its values. The result is a filtered list of values, each with its counts under the current context (§4.2). Per §4.5 this does not change the matching rows.

---

## 7. Boundaries

### The core library

Understands: data, facets, selections, fixed filters, counts, metrics, ordering, state.

Does not understand: rendering, layout, formatting, colours, click handling, Blazor, HTML.

Is usable on its own from a console app, a test, or a background job.

### The Blazor library

Understands: how to render a state, how to turn a click into a new selection, templates, layout, formatting. It renders facets, metrics and selections; the matching rows it hands to the application's own grid.

Does not understand: how counts are calculated. It never counts anything itself.

### The contract between them

The UI consumes a **UI-friendly state** and produces **selections**. It never sees selectors, expressions, or indexes.

```text
FacetState
    Key            stable string identity, e.g. "country"
    Name           display name
    Kind           Value | Boolean | Range | Date | Text | Custom
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
- The derivation is public, `FacetKey.Of<Order>(x => x.Country)`, so code and a UI can name a derived facet by its selector and get the same key the builder did. The string stays the identity; the selector is one way to spell it.

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
- Text facets: a user-typed text matched by an application function. Added 2026-09-14; see §5.
- Multi-valued facets over collection properties, OR within the facet. Added 2026-09-20; see §5.
- Scoped dashboards: the same definitions over a subset of the dataset, without a rebuild. Added 2026-09-15; see §4.10.
- Metrics: count, sum, average, min, max.
- The matching rows as a list in application-defined order, for any grid to page, virtualise and sort.
- Immutable state snapshot with a UI-friendly facet model.
- Selections as serialisable, key-addressed objects.

**Blazor**

- One dashboard component consuming a state.
- One component per facet kind.
- Metric display, basic layout, basic templates. The rows go to the application's grid.

### Later

- Grouping and hierarchical facets, including metrics per facet value (revenue per country).
- ~~Multi-valued facets over collection properties (tags, categories).~~ Added 2026-09-20 with OR within the facet; see §5. AND within the facet ("has all of these tags") remains later, as a selection mode: it makes a value's count depend on the facet's own selection, against §4.2.
- ~~Distinct count, percentage-of-total, calculated metrics.~~ All three were added on 2026-09-14; see §4.4.
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
- **A dashboard can be scoped to a subset without a rebuild.** A scoped dashboard shares the parent's definitions and indexes and behaves exactly like one built over the subset with a fixed filter, except that buckets and range bounds come from the parent. The scope is not a selection. Decided 2026-09-15. See §4.10.
- **A scope can be given as selections as well as a predicate.** The facets resolve the rows, so semantics are theirs and nothing scans the row objects; the scoped dashboard starts with nothing selected, shows only the values in scope without own-facet exclusion, and freezes relative date presets at the moment of scoping. Decided 2026-09-15. See §4.10.
- **Multi-valued properties are a facet kind of their own, with OR within the facet.** Until 2026-09-20 every row had exactly one value or null per facet. A multi-valued facet counts a row under each distinct value it has, selects the rows having any selected value, sums its filtered counts to at least the context count and has no "Other", because a remainder by subtraction is wrong when rows overlap. AND within the facet is a later selection mode, since with it a value's count would have to reflect the facet's own selection, against §4.2. Decided 2026-09-20. See §5, §6, §8.
- **OR within a facet, AND across facets is the only combination mode.** Exclusion is a later addition. See §4.1 and §8.
- **Top N ranking supports both modes.** Per facet, by filtered count (default) or by total count. See §6.
- **Ranking picks the values; the UI orders them.** Rank mode stays in the definition because it needs every value's count. The display order of the presented values is a UI choice: rank, label or value, either direction, null last. Decided 2026-09-15. See §6.
- **Facet keys: derived by default, explicit when given.** Unique, case-sensitive, fixed at initialisation. See §7.
- **The name in the definition is a default display name; the UI may override it.** A display name is presentation, but the state is consumed outside Blazor too, so the core carries one string per facet and metric and every consumer has a name for every key. A component replaces it for its own rendering only, for example with a localised string, so one dashboard serves every language without a rebuild. Nothing else (state, chips, JSON) sees the override. It is called `Name`, not `Title`, since `title` is an HTML attribute the components pass through and a parameter of that name would swallow it. Decided 2026-09-15. See §7.
- **Metrics are over all matching rows only.** Per-facet-value metrics belong to grouping, later. See §4.4 and §8.
- **Zero-count values stay in the state.** The core always includes them; hiding or greying them out is a UI choice. See §4.3.
- **A date preset no row falls in can be left out of the state.** Off by default, per facet. A preset is declared rather than discovered, so unlike a value it can name an interval the dataset has nothing in; a facet that asks is not offered one, selected or not. Decided 2026-09-19. See §5.
- **Metrics skip null.** Sum, average, min and max use rows with a value; average divides by that number. Count is unaffected. See §4.4.
- **Every metric carries its share of the total.** A fraction of the same aggregation over all rows after fixed filters, on the state itself rather than as a separate metric kind. Defined for count, sum and distinct count; "no value" for the other aggregations, for a metric without a value and for a zero total. Added 2026-09-14. See §4.4.
- **Distinct count is a metric aggregation.** Different non-null values among the matching rows, with value facet equality (case-insensitive strings by default). Exact, never approximate; per-facet-value breakdowns stay with grouping. Added 2026-09-14. See §4.4.
- **Calculated metrics are formulas over earlier metrics.** Null in, "no value" out; a non-finite result is "no value"; no share. Only metrics defined before the formula are visible, so cycles cannot be expressed. Added 2026-09-14. See §4.4.
- **A value facet can carry a label per value, read from the row.** The value stays the identity for counting, selections and JSON; the label is what is shown and searched. First row wins, null means "show the value". Async lookups happen in the application before the dashboard is created. Added 2026-09-14. See §5.
- **Free text is a facet kind, matched by an application function.** A text facet has no values and carries only its text; it joins the AND across facets and rides every key-addressed path (selections, state, active selections, JSON). The function `(row, text) => bool` is the primitive and owns the matching semantics; it must be pure and thread-safe. Whitespace-only text clears. Search within a facet (§4.5) stays a UI operation and keeps the word "search". Decided 2026-09-14. See §5.
- **Range buckets are fixed at initialisation.** Either application-defined or derived once from the dataset, never from the current selections. See §5.
- **Derived range buckets have round boundaries and open tails.** Until 2026-09-20 they were equal widths between the dataset's minimum and maximum, which gave edges no one would write and, on skewed data, one tall bar. Now about the requested number of buckets on a 1, 2, 5 step covers the 2nd to 98th percentile, and the values beyond it fall in an open bucket at each end, as with application-defined cuts. Quantile buckets and a log scale were considered and left out until a dataset asks for them. Decided 2026-09-20. See §5.
- **Quarter is a date granularity, with ThisQuarter and LastQuarter presets.** Business data is read by quarter, and without it a date facet had nothing between 12 bars a year and 1, which an automatic granularity needs. Calendar quarters only; a fiscal year offset and periods finer than a day are left until a dataset asks. Added 2026-09-20. See §5.
- **A date facet without a named granularity derives one from the data.** Until 2026-09-20 the default was months, which showed one bar for a fortnight of logins and sixty for five years of orders. Now the finest of day, week, month, quarter and year that keeps to about thirty periods over the 2nd to 98th percentile span is chosen at initialisation, on the same terms as derived range buckets: fixed, never following the selections, and the parent's in a scope. Letting the granularity follow the selection (drill-down) was considered and rejected because buckets never follow selections. Decided 2026-09-20. See §5.
- **A range or date selection is a set of intervals, combined as OR.** Until 2026-09-19 it was one interval, so a second bar click replaced the first and "January or March" could not be said. A bar now toggles its interval like a value, the null value toggles beside them, and a slider contributes one interval; a date part may be a preset. Bucket identities never enter the selection. Decided 2026-09-19. See §4.1 and §5.
- **Adjacent and overlapping intervals are one interval.** Until 2026-09-21 two neighbouring bars stayed two intervals, so the active selections read "100 to 200, 200 to 300", a bookmark carried two parts and the slider fell back to its ends. A range or date selection is now kept in canonical form, sorted and disjoint with touching intervals joined, and a bar click toggles coverage: added and joined when the bar is not fully covered, carved out, splitting if needed, when it is. Presets never merge. Decided 2026-09-21. See §5.
- **"Other" is measured against the facet's own counting context.** Not against the matching rows. See §6.
- **The matching rows are a list, not pages.** The state exposes them counted and indexable in the application-defined order. Paging, virtualisation and display sorting belong to the grid that shows them; the core never tries to be that grid and offers no page API, and the Blazor package renders no rows either: it hands them to the application's grid. Decided 2026-09-19. See §3, §4.4, §7.
