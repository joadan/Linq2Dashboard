using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// A tile with the number of matching rows (concept §3). Not a metric in the core, but the number
/// most dashboards put first, so it gets a component of its own beside <see cref="Metric{T}"/>.
/// </summary>
public partial class MatchingCount<T>
{
    [Parameter]
    public string Title { get; set; } = "Matching";
}
