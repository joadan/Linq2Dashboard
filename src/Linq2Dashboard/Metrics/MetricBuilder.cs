using Linq2Dashboard.Metrics;

namespace Linq2Dashboard;

/// <summary>Fluent configuration of a metric (design §2.1).</summary>
public sealed class MetricBuilder<T>
{
    private readonly MetricDefinition<T> _definition;
    private readonly Action _ensureMutable;

    internal MetricBuilder(MetricDefinition<T> definition, Action ensureMutable)
    {
        _definition = definition;
        _ensureMutable = ensureMutable;
    }

    public string Key => _definition.Key;

    /// <summary>Display name. Defaults to the key.</summary>
    public MetricBuilder<T> Title(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        _ensureMutable();
        _definition.Title = title;
        return this;
    }
}
