# Linq2Dashboard

.NET library for interactive exploration of large in-memory collections: facets with counts, metrics, paged results. Core engine only so far; a Blazor package is planned but not started.

## Documents are the source of truth

- `Linq2Dashboard-concept.md` says what the library is and how it behaves. Its rules are decisions. If code and concept disagree, the code is wrong.
- `Linq2Dashboard-design.md` says how it is built and records measured numbers. Section 9 is the Blazor plan. If design and concept disagree, the concept wins.
- Both end with a **Decided** list. Do not reopen those decisions; when a new one is made, add it there and point to the section that implements it.
- When code changes the public API or a behavioural rule, update the relevant document in the same commit.

## Layout

```text
src/Linq2Dashboard/            core, net10.0, no dependencies, package id Linq2Dashboard
  Indexing/                    RowSet, columns, caches, sort order (internal)
  Facets/                      definitions, builders, indexes, state
  Metrics/  Selections/  State/  Serialization/  Calculation/
tests/Linq2Dashboard.Tests/    xUnit, InternalsVisibleTo
benchmarks/Linq2Dashboard.Benchmarks/   BenchmarkDotNet, plus a --memory report
```

Public types live in the `Linq2Dashboard` namespace regardless of folder. Internal types use the folder as a sub-namespace (`Linq2Dashboard.Indexing`, `Linq2Dashboard.Facets`, ...).

## Commands

```powershell
dotnet build Linq2Dashboard.slnx          # must finish with 0 warnings
dotnet test Linq2Dashboard.slnx
dotnet run -c Release --project benchmarks/Linq2Dashboard.Benchmarks -- --memory
dotnet run -c Release --project benchmarks/Linq2Dashboard.Benchmarks -- --job short --filter *
```

## Conventions

- Private fields are plain camelCase, never `_prefixed`. Use `this.field = field` in constructors when a parameter shares the name.
- Zero build warnings. Nullable is enabled everywhere; do not suppress warnings except the documented `CS8714` in `ValueColumn`.
- Every behavioural rule in the concept has a named test. A new rule or an edge case gets a test in the same commit.
- Validate eagerly in the builder so mistakes surface inside `Dashboard.Create`, not at first use.
- Reading external input (JSON selections) is lenient: drop what cannot be read. Code paths are strict: unknown keys throw.
- Doc comments cite the section they implement, e.g. `(concept §4.2)` or `(design §3.3)`.
- Commit messages: an imperative summary line, then a short paragraph on what and why. No attribution trailers.

## Things that are settled, so do not propose them

- Columnar only; no per-value bitmaps. The benchmarks meet every target without them.
- The dashboard is immutable after `Create` and holds no selection state. The UI owns `Selections`.
- Null is a facet value. Zero-count values stay in the state. Filtered counts always sum to the facet's context count.
- Parallel counting exists but is off by default.
- The Blazor package follows the plan in design §9; do not start it until asked.

## Performance baseline

One million rows, 8 facets, 4 cores: build about 1 s, warm recalculation 5 to 10 ms, cold 20 ms, 78 MB. If a change is likely to move these, run the benchmarks and update design §8.
