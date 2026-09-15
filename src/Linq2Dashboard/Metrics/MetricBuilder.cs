using Linq2Dashboard.Metrics;

namespace Linq2Dashboard;

/// <summary>Fluent configuration of a metric (design §2.1).</summary>
public sealed class MetricBuilder<T>
{
    private readonly MetricDefinition<T> definition;
    private readonly Action ensureMutable;

    internal MetricBuilder(MetricDefinition<T> definition, Action ensureMutable)
    {
        this.definition = definition;
        this.ensureMutable = ensureMutable;
    }

    /// <summary>The metric key, used to read the metric from a state and by the Blazor components.</summary>
    public string Key => definition.Key;

    /// <summary>Display name. Defaults to the key.</summary>
    public MetricBuilder<T> Name(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ensureMutable();
        definition.Name = name;
        return this;
    }
}
