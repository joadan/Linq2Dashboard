namespace Linq2Dashboard;

/// <summary>Static description of a metric as configured at build.</summary>
public sealed record MetricInfo(string Key, string Name, Aggregation Aggregation);
