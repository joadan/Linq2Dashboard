namespace Linq2Dashboard.Blazor;

/// <summary>
/// What a metric tile shows, formatted and ready to render: the context of <c>MetricTemplate</c> on
/// <see cref="Metric{T}"/> and <see cref="MatchingCount{T}"/> (design §9.5). A template composes these
/// pieces with its own markup instead of formatting the state again.
/// </summary>
/// <param name="Name">The tile's name: the builder's, or the component's override.</param>
/// <param name="Value">The formatted value; the formatter's empty text, a dash by default, when the metric has no value.</param>
/// <param name="Share">The formatted share of the total when <c>ShowShare</c> is on and the metric has one; otherwise null.</param>
/// <param name="IsEmpty">True when the metric has no value (concept §4.4). Always false for the matching count.</param>
/// <param name="Metric">The raw state for anything the formatted pieces do not cover. Null for the matching count, which is not a metric in the core.</param>
public sealed record MetricTileContent(string Name, string Value, string? Share, bool IsEmpty, MetricState? Metric);
