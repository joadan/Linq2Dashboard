namespace Linq2Dashboard;

/// <summary>
/// The values a calculated metric's formula reads (concept §4.4): the metrics defined before it in
/// the builder, by key. An unknown key throws; a metric defined later is unknown, which is what
/// keeps formulas acyclic. Values are null when the metric has no value, so a formula written with
/// nullable arithmetic (<c>m["revenue"] / m["orders"]</c>) is null whenever an input is.
/// </summary>
public readonly struct MetricValues
{
    private readonly MetricState[] states;
    private readonly int count;

    internal MetricValues(MetricState[] states, int count)
    {
        this.states = states;
        this.count = count;
    }

    /// <summary>The value of the metric <paramref name="key"/>, the same as <see cref="Value"/>.</summary>
    public double? this[string key] => Value(key);

    /// <summary>The value of the metric <paramref name="key"/> over the matching rows; null when it has none.</summary>
    public double? Value(string key) => Find(key).Value;

    /// <summary>The share of the total of the metric <paramref name="key"/>; null for aggregations without one.</summary>
    public double? Share(string key) => Find(key).Share;

    private MetricState Find(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        for (int i = 0; i < count; i++)
        {
            if (states[i].Key == key)
            {
                return states[i];
            }
        }

        throw new ArgumentException($"No metric with key '{key}' is defined before this one. A formula can only read metrics defined earlier in the builder.", nameof(key));
    }
}
