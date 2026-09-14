using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// A tile with the number of matching rows (concept §3). Not a metric in the core, but the number
/// most dashboards put first, so it gets a component of its own beside <see cref="Metric{T}"/>.
/// </summary>
public partial class MatchingCount<T>
{
    /// <summary>The tile's title. Default "Matching".</summary>
    [Parameter]
    public string Title { get; set; } = "Matching";

    /// <summary>Shows the matching rows as a percentage of all rows after fixed filters, under the count.</summary>
    [Parameter]
    public bool ShowShare { get; set; }

    /// <summary>
    /// Replaces the title and count inside the tile with the template's markup. The tile element and its
    /// classes stay. The context carries the formatted pieces; its <c>Metric</c> is null since the count is
    /// not a metric in the core (design §9.5).
    /// </summary>
    [Parameter]
    public RenderFragment<MetricTileContent>? MetricTemplate { get; set; }

    private MetricTileContent Content
    {
        get
        {
            string? share = ShowShare && State.TotalCount > 0 ? Formatter.FormatShare((double)State.MatchingCount / State.TotalCount) : null;
            return new MetricTileContent(Title, Formatter.FormatCount(State.MatchingCount), share, IsEmpty: false, Metric: null);
        }
    }

    private RenderFragment? Template => MetricTemplate is null ? null : MetricTemplate(Content);
}
