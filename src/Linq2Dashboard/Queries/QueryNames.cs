using Linq2Dashboard.Facets;
using Linq2Dashboard.Metrics;

namespace Linq2Dashboard.Queries;

/// <summary>Resolves the names in a query and words its errors so a caller or an agent can retry (design §10.1, §10.5).</summary>
internal static class QueryNames
{
    /// <summary>A name in a query: the facet with that key, else the property of that name (concept §4.11).</summary>
    public static (FacetIndex? Facet, PropertyAccessor<T>? Property) Name<T>(Dashboard<T> dashboard, string name, string use)
    {
        if (dashboard.TryGetFacetIndex(name, out FacetIndex facet))
        {
            return (facet, null);
        }

        if (dashboard.Properties.TryGet(name, out PropertyAccessor<T> property))
        {
            return (null, property);
        }

        if (dashboard.TryGetMetricIndex(name, out _))
        {
            throw Error($"'{name}' is a metric, and a query cannot {use} a metric. Ask for its value with QueryValue.Metric(\"{name}\").");
        }

        IEnumerable<string> facets = dashboard.Facets.Select(f => f.Key);
        IReadOnlyList<string> properties = dashboard.Properties.Names;
        throw Error($"'{name}' is neither a facet of this dashboard nor a property of {typeof(T).Name}.{Suggest(name, facets.Concat(properties))} Facets: {string.Join(", ", facets)}. Properties: {string.Join(", ", properties)}.");
    }

    public static PropertyAccessor<T> Property<T>(Dashboard<T> dashboard, string name)
    {
        if (dashboard.Properties.TryGet(name, out PropertyAccessor<T> property))
        {
            return property;
        }

        IReadOnlyList<string> properties = dashboard.Properties.Names;
        throw Error($"'{name}' is not a property of {typeof(T).Name}.{Suggest(name, properties)} Properties: {string.Join(", ", properties)}.");
    }

    public static MetricIndex Metric<T>(Dashboard<T> dashboard, string key)
    {
        if (dashboard.TryGetMetricIndex(key, out MetricIndex metric))
        {
            return metric;
        }

        IEnumerable<string> keys = dashboard.Metrics.Select(m => m.Key);
        string known = dashboard.Metrics.Count == 0 ? " The dashboard has no metrics." : $" Metrics: {string.Join(", ", keys)}.";
        throw Error($"'{key}' is not a metric of this dashboard.{Suggest(key, keys)}{known}");
    }

    /// <summary>The nearest known name, ignoring case and up to two edits away, so a caller or an agent can retry (design §10.5).</summary>
    public static string Suggest(string name, IEnumerable<string> candidates)
    {
        string? best = null;
        int bestDistance = 3;
        foreach (string candidate in candidates)
        {
            int distance = Distance(name.ToUpperInvariant(), candidate.ToUpperInvariant());
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best is null ? string.Empty : $" Did you mean '{best}'?";
    }

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j], current[j - 1]) + 1);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    public static DashboardQueryException Error(string message, string? path = null) => new(message, path);

    /// <summary>Resolves one part of a query, so an error it raises names that part (design §10.6).</summary>
    public static TResult At<TResult>(string path, Func<TResult> resolve)
    {
        try
        {
            return resolve();
        }
        catch (DashboardQueryException e)
        {
            throw e.At(path);
        }
    }

    /// <summary>Every selection names a facet; checked with the query's other names so the error says which (concept §4.11).</summary>
    public static void CheckSelections<T>(Dashboard<T> dashboard, Selections selections)
    {
        foreach ((string key, _) in selections)
        {
            if (!dashboard.TryGetFacetIndex(key, out _))
            {
                IEnumerable<string> facets = dashboard.Facets.Select(f => f.Key);
                throw Error($"'{key}' is not a facet of this dashboard, and selections name facets.{Suggest(key, facets)} Facets: {string.Join(", ", facets)}.", $"selections.{key}");
            }
        }
    }
}
