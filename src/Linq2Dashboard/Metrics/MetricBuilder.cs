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

    public string Key => definition.Key;

    /// <summary>Display name. Defaults to the key.</summary>
    public MetricBuilder<T> Title(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ensureMutable();
        definition.Title = title;
        return this;
    }
}
