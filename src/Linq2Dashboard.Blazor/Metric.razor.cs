using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// One metric tile, addressed by key like the facet components (design §9). The metric itself is
/// defined in the builder; this component only places and formats it. The default tile shows the
/// name, the value and, for count, sum and distinct, the share of the total as a percentage (concept §4.4).
/// </summary>
public partial class Metric<T>
{
    /// <summary>The metric key, as defined in the builder or, with an aggregation parameter, as this component defines it.</summary>
    [Parameter, EditorRequired]
    public string Key { get; set; } = default!;

    /// <summary>Overrides the name given in the builder. When this component defines the metric under a view with <c>Items</c>, it is the metric's name (design §9).</summary>
    [Parameter]
    public string? Name { get; set; }

    /// <summary>Items mode only: defines the metric as the number of matching rows, as <c>CountMetric</c> in the builder (concept §4.4, design §9). One aggregation per metric.</summary>
    [Parameter]
    public bool Count { get; set; }

    /// <summary>Items mode only: defines the metric as the sum of a numeric member over the matching rows, as <c>SumMetric</c> in the builder (concept §4.4, design §9). One aggregation per metric.</summary>
    [Parameter]
    public Expression<Func<T, object?>>? Sum { get; set; }

    /// <summary>Items mode only: defines the metric as the average of a numeric member, as <c>AverageMetric</c> in the builder (concept §4.4, design §9). One aggregation per metric.</summary>
    [Parameter]
    public Expression<Func<T, object?>>? Average { get; set; }

    /// <summary>Items mode only: defines the metric as the smallest value of a numeric member, as <c>MinMetric</c> in the builder (concept §4.4, design §9). One aggregation per metric.</summary>
    [Parameter]
    public Expression<Func<T, object?>>? Min { get; set; }

    /// <summary>Items mode only: defines the metric as the largest value of a numeric member, as <c>MaxMetric</c> in the builder (concept §4.4, design §9). One aggregation per metric.</summary>
    [Parameter]
    public Expression<Func<T, object?>>? Max { get; set; }

    /// <summary>Items mode only: defines the metric as the number of different non-null values of a member, as <c>DistinctMetric</c> in the builder (concept §4.4, design §9). One aggregation per metric.</summary>
    [Parameter]
    public Expression<Func<T, object?>>? Distinct { get; set; }

    /// <summary>
    /// Items mode only: defines the metric as a formula over other metrics, as <c>CalculatedMetric</c> in the builder
    /// (concept §4.4, design §9). The view defines every plain metric before any formula, so a formula can read a plain metric
    /// wherever it is declared; a formula reading another formula must come after it. One aggregation per metric.
    /// </summary>
    [Parameter]
    public Func<MetricValues, double?>? Formula { get; set; }

    /// <summary>Items mode only: any other option of the metric's builder. Code, so read when the metric is defined (design §9).</summary>
    [Parameter]
    public Action<MetricBuilder<T>>? Define { get; set; }

    /// <summary>True while the view has yet to define this metric; the component renders nothing meanwhile (design §9).</summary>
    private bool Waiting => AwaitingDefinition(isMetric: true, Key);

    /// <summary>Registers the metric this component defines, when it has an aggregation parameter.</summary>
    protected override void OnParametersSet()
    {
        (string Kind, object? Value)[] kinds =
        [
            (nameof(Count), Count ? true : null), (nameof(Sum), Sum), (nameof(Average), Average), (nameof(Min), Min),
            (nameof(Max), Max), (nameof(Distinct), Distinct), (nameof(Formula), Formula),
        ];
        string[] set = [.. kinds.Where(k => k.Value is not null).Select(k => k.Kind)];
        if (set.Length > 1)
        {
            throw new InvalidOperationException($"Metric '{Key}' has {string.Join(" and ", set)}; a metric has one aggregation.");
        }

        if (set.Length == 0)
        {
            if (Define is not null)
            {
                throw new InvalidOperationException($"Metric '{Key}' has Define but no aggregation: give it Count, Sum, Average, Min, Max, Distinct or Formula.");
            }

            return;
        }

        var parameters = new MetricParameters<T>(
            Name, Count, Unboxed(Sum), Unboxed(Average), Unboxed(Min), Unboxed(Max), Unboxed(Distinct), Formula, Define);
        LambdaExpression? selector = parameters.Sum ?? parameters.Average ?? parameters.Min ?? parameters.Max ?? parameters.Distinct;
        string key = Key;
        Register(new MarkupDefinition<T>(
            this, ComponentName, IsMetric: true, key, Explicit: true,
            [set[0], selector?.ReturnType, Name],
            b => MarkupMetrics.Define(b, key, parameters),
            IsCalculated: Formula is not null));
    }

    private static LambdaExpression? Unboxed(Expression<Func<T, object?>>? selector) =>
        selector is null ? null : MarkupFacets.Unbox(selector);


    /// <summary>
    /// Replaces the whole tile with the template's markup: the library renders no element of its own, so the
    /// template owns the root and puts its own classes and hooks on it. <c>Class</c> and extra attributes apply
    /// to the default tile only. The context carries the formatted pieces, the share included whenever the metric
    /// has one, and the raw state (design §9.5).
    /// </summary>
    [Parameter]
    public RenderFragment<MetricTileContent>? MetricTemplate { get; set; }

    private MetricState Current => State.Metric(Key);

    private MetricTileContent Content
    {
        get
        {
            MetricState current = Current;
            string? share = current.Share is double value ? Formatter.FormatShare(value) : null;
            return new MetricTileContent(Name ?? current.Name, Formatter.FormatMetric(current), share, !current.HasValue, current);
        }
    }
}
