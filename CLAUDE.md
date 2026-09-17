# Linq2Dashboard

.NET library for interactive exploration of large in-memory collections: facets with counts, metrics, paged results. The core engine and the Blazor package from design §9 are complete for the first version.

## Documents are the source of truth

- `Linq2Dashboard-concept.md` says what the library is and how it behaves. Its rules are decisions. If code and concept disagree, the code is wrong.
- `Linq2Dashboard-design.md` says how it is built and records measured numbers. Section 9 is the Blazor plan. If design and concept disagree, the concept wins.
- Both end with a **Decided** list. Do not reopen those decisions; when a new one is made, add it there and point to the section that implements it.
- `Linq2Dashboard-usage.md` is the guide for consumers, written to be pasted into another project's instructions file. It is served by the docs site and packed into both NuGet packages. Keep it short and keep it current: a public API change updates it in the same commit.
- When code changes the public API or a behavioural rule, update the relevant document in the same commit.

## Layout

```text
src/Linq2Dashboard/            core, net10.0, no dependencies, package id Linq2Dashboard
  Indexing/                    RowSet, columns, caches, sort order (internal)
  Facets/                      definitions, builders, indexes, state
  Metrics/  Selections/  State/  Serialization/  Calculation/
src/Linq2Dashboard.Blazor/     Razor class library: DashboardView<T>, DashboardContext<T>, IDashboardFormatter, components
samples/Linq2Dashboard.Sample/ Blazor Server sample over 200 000 generated rows
samples/Linq2Dashboard.SampleData/  the generator both the sample and the docs site use
docs/Linq2Dashboard.Docs/      Blazor WebAssembly docs site: demo, getting started, concept and design rendered from the root markdown; deployed to GitHub Pages by the CI workflow on every push to master
tests/Linq2Dashboard.Tests/    xUnit, InternalsVisibleTo
tests/Linq2Dashboard.Blazor.Tests/   bUnit; tests pass an invariant-culture formatter so they do not depend on the machine
benchmarks/Linq2Dashboard.Benchmarks/   BenchmarkDotNet, plus a --memory report
```

Public types live in the `Linq2Dashboard` namespace regardless of folder. Internal types use the folder as a sub-namespace (`Linq2Dashboard.Indexing`, `Linq2Dashboard.Facets`, ...).

## Commands

```powershell
dotnet build Linq2Dashboard.slnx          # must finish with 0 warnings
dotnet test Linq2Dashboard.slnx
dotnet run -c Release --project benchmarks/Linq2Dashboard.Benchmarks -- --memory
dotnet run -c Release --project benchmarks/Linq2Dashboard.Benchmarks -- --job short --filter *
dotnet pack src/Linq2Dashboard/Linq2Dashboard.csproj -c Release -o artifacts   # version from version.json + git height (Nerdbank.GitVersioning)
```

## Conventions

- Private fields are plain camelCase, never `_prefixed`. Use `this.field = field` in constructors when a parameter shares the name.
- Blazor components are always split: markup in `X.razor`, everything else in a `X.razor.cs` partial class. No `@code` blocks, in the library, the docs site or the sample. `@typeparam`, `@inherits` and `@inject` stay in the `.razor` file; the partial class repeats the type parameter and omits the base class.
- Zero build warnings. Nullable is enabled everywhere; do not suppress warnings except the documented `CS8714` in `ValueColumn` and `DistinctColumn`. Every public type and member has a doc comment: `CS1591` is not suppressed, so a missing one fails the zero-warning rule. Overrides and interface implementations use `<inheritdoc />`.
- Every behavioural rule in the concept has a named test. A new rule or an edge case gets a test in the same commit.
- Validate eagerly in the builder so mistakes surface inside `Dashboard.Create`, not at first use.
- Reading external input (JSON selections) is lenient: drop what cannot be read. Code paths are strict: unknown keys throw.
- Doc comments cite the section they implement, e.g. `(concept §4.2)` or `(design §3.3)`.
- Commit messages: an imperative summary line, then a short paragraph on what and why. No attribution trailers.
- Releases: never push to NuGet from a machine. The Create Release workflow (manual) tests, packs, pushes through NuGet Trusted Publishing (OIDC via NuGet/login; the only secret is NUGET_USER, the nuget.org profile name) and tags. Bump major.minor or the prerelease tag in version.json; the patch is the git height. CI packs on every push to verify the packages build but pushes nothing.

## Things that are settled, so do not propose them

- Columnar only; no per-value bitmaps. The benchmarks meet every target without them.
- The dashboard is immutable after `Create` and holds no selection state. The UI owns `Selections`.
- Null is a facet value. Zero-count values stay in the state. Filtered counts always sum to the facet's context count.
- Parallel counting exists but is off by default.
- Blazor components hold no dashboard state; everything goes through DashboardContext<T>. Facet defaults use the shared FacetHeader and BucketBars; styling goes through the --l2d-* properties declared in DashboardView.razor.css, never hard-coded values in components.

## Performance baseline

One million rows, 8 facets, 4 cores: build about 1 s, warm recalculation 5 to 10 ms, cold 20 ms, 78 MB. If a change is likely to move these, run the benchmarks and update design §8.
