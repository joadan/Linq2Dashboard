using Linq2Dashboard.Facets;

namespace Linq2Dashboard.Queries;

/// <summary>Builds the <see cref="DashboardDescription"/> of design §10.7 from the dashboard's own indexes and the row type.</summary>
internal static class QueryDescriber
{
    public static DashboardDescription Describe<T>(Dashboard<T> dashboard, DescribeOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(options.ValueLimit, nameof(options));
        IReadOnlyDictionary<string, string> descriptions = options.Descriptions ?? new Dictionary<string, string>();
        foreach (string name in descriptions.Keys)
        {
            bool known = dashboard.TryGetFacetIndex(name, out _) || dashboard.TryGetMetricIndex(name, out _) || dashboard.Properties.TryGet(name, out _);
            if (!known)
            {
                throw new ArgumentException($"There is a description for '{name}', which is neither a facet, a metric nor a property of {typeof(T).Name}.", nameof(options));
            }
        }

        bool Shown(string name) => options.Allowed is null || options.Allowed.Contains(name, StringComparer.Ordinal);
        string? Text(string name) => descriptions.TryGetValue(name, out string? text) ? text : null;

        FacetDescription[] facets = dashboard.FacetIndexes
            .Where(f => Shown(f.Key))
            .Select(f => f.Describe(options.ValueLimit) with { Description = Text(f.Key) })
            .ToArray();
        MetricDescription[] metrics = dashboard.MetricIndexes
            .Where(m => Shown(m.Key))
            .Select(m => new MetricDescription(m.Key, m.Name, m.Aggregation) { Description = Text(m.Key) })
            .ToArray();
        PropertyDescription[] properties = dashboard.Properties.Names
            .Where(Shown)
            .Select(name =>
            {
                dashboard.Properties.TryGet(name, out PropertyAccessor<T> property);
                return property.Describe(dashboard.ItemArray, dashboard.All) with { Description = Text(name), BehindFacet = dashboard.TryGetFacetIndex(name, out _) };
            })
            .ToArray();

        return new DashboardDescription(facets, metrics, properties);
    }
}
