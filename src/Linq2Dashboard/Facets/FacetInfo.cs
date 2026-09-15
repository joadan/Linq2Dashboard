namespace Linq2Dashboard;

/// <summary>Static description of a facet as configured at build: its identity, display name and kind (concept §3).</summary>
public sealed record FacetInfo(string Key, string Name, FacetKind Kind);
