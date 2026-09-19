namespace Linq2Dashboard;

/// <summary>Fluent configuration of a date facet (design §2.1).</summary>
public sealed class DateFacetBuilder<T>
{
    private readonly Action<string> setName;
    private readonly Action<TimeZoneInfo> setZone;
    private readonly Action<DateGranularity> setGranularity;
    private readonly Action<DatePreset[]> setPresets;
    private readonly Action<bool> setSkipEmptyPresets;
    private readonly Action ensureMutable;

    internal DateFacetBuilder(
        string key,
        Action<string> setName,
        Action<TimeZoneInfo> setZone,
        Action<DateGranularity> setGranularity,
        Action<DatePreset[]> setPresets,
        Action<bool> setSkipEmptyPresets,
        Action ensureMutable)
    {
        Key = key;
        this.setName = setName;
        this.setZone = setZone;
        this.setGranularity = setGranularity;
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

    /// <summary>Calendar period for buckets. Default is month.</summary>
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
