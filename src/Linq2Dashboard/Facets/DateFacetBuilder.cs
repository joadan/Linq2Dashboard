using Linq2Dashboard.Indexing;

namespace Linq2Dashboard;

/// <summary>Fluent configuration of a date facet (design §2.1).</summary>
public sealed class DateFacetBuilder<T>
{
    private readonly Action<string> setName;
    private readonly Action<TimeZoneInfo> setZone;
    private readonly Action<DateGranularity?> setGranularity;
    private readonly Action<int> setMaxPeriods;
    private readonly Action<DatePreset[]> setPresets;
    private readonly Action<bool> setSkipEmptyPresets;
    private readonly Action ensureMutable;

    internal DateFacetBuilder(
        string key,
        Action<string> setName,
        Action<TimeZoneInfo> setZone,
        Action<DateGranularity?> setGranularity,
        Action<int> setMaxPeriods,
        Action<DatePreset[]> setPresets,
        Action<bool> setSkipEmptyPresets,
        Action ensureMutable)
    {
        Key = key;
        this.setName = setName;
        this.setZone = setZone;
        this.setGranularity = setGranularity;
        this.setMaxPeriods = setMaxPeriods;
        this.setPresets = setPresets;
        this.setSkipEmptyPresets = setSkipEmptyPresets;
        this.ensureMutable = ensureMutable;
    }

    /// <summary>The facet key, used in selections, state and the Blazor components.</summary>
    public string Key { get; }

    /// <summary>Display name. Defaults to the key.</summary>
    public DateFacetBuilder<T> Name(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ensureMutable();
        setName(name);
        return this;
    }

    /// <summary>Time zone for calendar buckets and presets. Default is UTC (concept §5).</summary>
    public DateFacetBuilder<T> TimeZone(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ensureMutable();
        setZone(zone);
        return this;
    }

    /// <summary>
    /// Calendar period for buckets. Without it the period is derived from the data at build, see
    /// <see cref="AutoGranularity"/> (concept §5).
    /// </summary>
    public DateFacetBuilder<T> Granularity(DateGranularity granularity)
    {
        if (!Enum.IsDefined(granularity))
        {
            throw new ArgumentOutOfRangeException(nameof(granularity));
        }

        ensureMutable();
        setGranularity(granularity);
        return this;
    }

    /// <summary>
    /// Derive the period from the data at build, the default: the finest of day, week, month, quarter
    /// and year that lays at most about <paramref name="maxPeriods"/> periods over the body of the data,
    /// the 2nd to 98th percentile, so a few stray dates do not decide (concept §5). Default is 30. The
    /// state reports the period chosen. A dataset with no dates gets months.
    /// </summary>
    public DateFacetBuilder<T> AutoGranularity(int maxPeriods = DateGranularities.DefaultMaxPeriods)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPeriods, 1);
        ensureMutable();
        setGranularity(null);
        setMaxPeriods(maxPeriods);
        return this;
    }

    /// <summary>Relative presets to offer and count (concept §5). Default is none.</summary>
    public DateFacetBuilder<T> Presets(params DatePreset[] presets)
    {
        ArgumentNullException.ThrowIfNull(presets);
        foreach (DatePreset preset in presets)
        {
            if (!Enum.IsDefined(preset))
            {
                throw new ArgumentOutOfRangeException(nameof(presets), $"Unknown preset {preset}.");
            }
        }

        ensureMutable();
        setPresets(presets.Distinct().ToArray());
        return this;
    }

    /// <summary>
    /// Leave a preset out of the state when no row in the dataset falls in it, the way a value no
    /// row has is not a facet value (concept §5). Off by default. Resolved at each calculation, so
    /// a preset whose interval moves onto rows comes back; a selected preset is left out too, and
    /// clearing the facet still removes it.
    /// </summary>
    public DateFacetBuilder<T> SkipEmptyPresets(bool skip = true)
    {
        ensureMutable();
        setSkipEmptyPresets(skip);
        return this;
    }
}
