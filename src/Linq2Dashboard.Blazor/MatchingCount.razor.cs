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

    /// <summary>Shows the matching rows as a percentage of all rows after fixed filters, under the count.</summary>
    [Parameter]
    public bool ShowShare { get; set; }

    private string? ShareText =>
        ShowShare && State.TotalCount > 0 ? Formatter.FormatShare((double)State.MatchingCount / State.TotalCount) : null;
}
