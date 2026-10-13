namespace Linq2Dashboard;

/// <summary>
/// What a query can ask of a dashboard (design §10.7): its facets, metrics and properties with what each allows,
/// the periods a date can be grouped by and the query limits. Built by <see cref="Dashboard{T}.Describe"/> for a
/// client or an agent, before it asks. Immutable.
/// </summary>
public sealed class DashboardDescription
{
    internal DashboardDescription(FacetDescription[] facets, MetricDescription[] metrics, PropertyDescription[] properties)
    {
        Facets = facets;
        Metrics = metrics;
        Properties = properties;
    }

    /// <summary>The facets in definition order: what a query groups by and selects with.</summary>
    public IReadOnlyList<FacetDescription> Facets { get; }

    /// <summary>The metrics in definition order: what a query reads per group.</summary>
    public IReadOnlyList<MetricDescription> Metrics { get; }

    /// <summary>The properties of the row type a query can name, in name order.</summary>
    public IReadOnlyList<PropertyDescription> Properties { get; }

    /// <summary>The periods a date can be grouped by.</summary>
    public IReadOnlyList<DateGranularity> Periods { get; } = Enum.GetValues<DateGranularity>();

    /// <summary>The most rows a row query returns (design §10.4).</summary>
    public int MaxRows => Queries.RowQueryRunner.MaxRows;

    /// <summary>The rows a row query returns without a take.</summary>
    public int DefaultRows => Queries.RowQueryRunner.DefaultRows;

    /// <summary>The most dimensions a group query has.</summary>
    public int MaxDimensions => 2;
}

/// <summary>
/// One facet as a query sees it (design §10.7). The kind decides which of the optional members are set: the values
/// of a value, boolean or multi-valued facet, the buckets and bounds of a range facet, the period, zone and presets of
/// a date facet.
/// </summary>
public sealed record FacetDescription(string Key, string Name, FacetKind Kind)
{
    /// <summary>The application's text for the facet, from <see cref="DescribeOptions.Descriptions"/>; null when it gave none.</summary>
    public string? Description { get; init; }

    /// <summary>The number of values in the dataset, or in the scope, the null value included; null for other kinds.</summary>
    public int? ValueCount { get; init; }

    /// <summary>True when values carry labels: a cell is then <c>{ value, label }</c>, and a selection names the value.</summary>
    public bool HasLabels { get; init; }

    /// <summary>
    /// The values, most common first, with their labels, when there are no more than <see cref="DescribeOptions.ValueLimit"/>;
    /// null above it, when a query grouped by the facet discovers them. A null entry's <c>Value</c> is the null value.
    /// </summary>
    public IReadOnlyList<QueryValueCell>? Values { get; init; }

    /// <summary>The number of buckets of a range facet.</summary>
    public int? BucketCount { get; init; }

    /// <summary>The smallest value of a range facet; null when it has none.</summary>
    public double? Min { get; init; }

    /// <summary>The largest value of a range facet; null when it has none.</summary>
    public double? Max { get; init; }

    /// <summary>The period a date facet groups by unless a query names another.</summary>
    public DateGranularity? Granularity { get; init; }

    /// <summary>The time zone a date facet's periods and presets are in, by id.</summary>
    public string? TimeZone { get; init; }

    /// <summary>The relative presets a date facet's selection can name, such as <c>last30Days</c> in a URL.</summary>
    public IReadOnlyList<DatePreset>? Presets { get; init; }
}

/// <summary>One metric as a query reads it (design §10.7).</summary>
public sealed record MetricDescription(string Key, string Name, Aggregation Aggregation)
{
    /// <summary>The application's text for the metric, from <see cref="DescribeOptions.Descriptions"/>; null when it gave none.</summary>
    public string? Description { get; init; }
}

/// <summary>One property of the row type as a query can use it (design §10.5, §10.7).</summary>
public sealed record PropertyDescription(string Name, PropertyKind Kind, IReadOnlyList<PropertyUse> Uses)
{
    /// <summary>The application's text for the property, from <see cref="DescribeOptions.Descriptions"/>; null when it gave none.</summary>
    public string? Description { get; init; }

    /// <summary>True when a facet has the same name: the name then means the facet, and the property is reached with <see cref="Dimension.Property(string)"/>.</summary>
    public bool BehindFacet { get; init; }

    /// <summary>The names of an enum property's values; null for every other type.</summary>
    public IReadOnlyList<string>? Values { get; init; }
}

/// <summary>What a property holds, in the vocabulary of JSON (design §10.7).</summary>
public enum PropertyKind
{
    /// <summary>A string, a character, an enum or a <see cref="Guid"/>.</summary>
    Text,

    /// <summary>A number of any numeric type.</summary>
    Number,

    /// <summary>True or false.</summary>
    Boolean,

    /// <summary>A <see cref="DateTime"/>, <see cref="DateTimeOffset"/> or <see cref="DateOnly"/>.</summary>
    Date,

    /// <summary>A collection other than a string, grouped by item.</summary>
    Collection,

    /// <summary>Anything else: an object, a time of day, a duration.</summary>
    Other,
}

/// <summary>What a query can do with a property (design §10.5).</summary>
public enum PropertyUse
{
    /// <summary>Group by its value, or by item for a collection.</summary>
    GroupBy,

    /// <summary>Group by a period: a date.</summary>
    Period,

    /// <summary>Sum, average, min and max: a number.</summary>
    Aggregate,

    /// <summary>Count its different values.</summary>
    Distinct,

    /// <summary>List it as a column of a row query: a simple type, or a collection of them.</summary>
    Column,
}

/// <summary>How <see cref="Dashboard{T}.Describe"/> describes a dashboard (design §10.7).</summary>
public sealed class DescribeOptions
{
    /// <summary>The names to describe: facet keys, metric keys and property names. Null describes everything. Use the list given to <see cref="QueryJsonOptions{T}.Allowed"/>, so a caller is never told about a name it cannot use.</summary>
    public IReadOnlyCollection<string>? Allowed { get; init; }

    /// <summary>
    /// The application's text per facet key, metric key or property name: what a value means, its unit, how it differs
    /// from a similar one. The core cannot know that a facet's value is an internal id and its label a name, and an
    /// agent cannot ask well without it (design §10.9). A name the dashboard does not know is an error.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Descriptions { get; init; }

    /// <summary>A facet with at most this many values lists them; above it only the count is given. 25 by default; 0 lists none.</summary>
    public int ValueLimit { get; init; } = 25;
}
